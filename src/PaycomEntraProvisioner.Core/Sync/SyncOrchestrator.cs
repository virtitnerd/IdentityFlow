using Microsoft.Extensions.Logging;
using PaycomEntraProvisioner.Core.Abstractions;
using PaycomEntraProvisioner.Core.Domain;
using PaycomEntraProvisioner.Core.Exceptions;
using PaycomEntraProvisioner.Core.GroupRules;
using PaycomEntraProvisioner.Core.Mapping;
using PaycomEntraProvisioner.Core.Scim;

namespace PaycomEntraProvisioner.Core.Sync;

/// <summary>
/// End-to-end sync pipeline: pull workers from Paycom, map them to SCIM
/// resources, submit to the Entra ID API-driven inbound provisioning job,
/// reconcile assigned-group membership, and record the outcome. Used
/// identically by the Azure Function's timer/HTTP triggers and by the
/// Razor Pages "run now" admin action, so behavior never diverges between
/// scheduled and manual runs.
/// </summary>
public sealed class SyncOrchestrator(
    IPaycomClient paycomClient,
    IEntraProvisioningClient provisioningClient,
    IEntraDirectoryClient directoryClient,
    IFieldMappingStore fieldMappingStore,
    IGroupAssignmentRuleStore groupRuleStore,
    ISyncRunStore syncRunStore,
    IEmployeeSnapshotStore snapshotStore,
    IDiscoveredFieldStore discoveredFieldStore,
    ILifecycleTaskStore lifecycleTaskStore,
    MappingEngine mappingEngine,
    GroupRuleEvaluator groupRuleEvaluator,
    ILogger<SyncOrchestrator> logger)
{
    /// <summary>
    /// How far back to look in the provisioning audit log when confirming
    /// previously-submitted records. Generous on purpose (comfortably
    /// covers a missed run or a slow provisioning cycle over a weekend)
    /// since the log read is skipped entirely when nothing is pending -
    /// a wide window costs nothing extra in the common case.
    /// </summary>
    private const int ProvisioningLogLookbackHours = 72;

    public async Task<SyncRun> RunAsync(
        SyncTrigger trigger,
        string? triggeredByUser,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await ReconcilePendingProvisioningConfirmationsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Confirming prior runs' outcomes is a correction, not a
            // precondition - never let it block this run from proceeding.
            logger.LogWarning(ex, "Failed to reconcile pending provisioning confirmations from a previous run; will retry next run.");
        }

        var run = new SyncRun
        {
            StartedAt = DateTimeOffset.UtcNow,
            Trigger = trigger,
            TriggeredByUser = triggeredByUser,
            DryRun = dryRun
        };
        await syncRunStore.CreateAsync(run, cancellationToken);

        try
        {
            // The Paycom pull is an independent external HTTP call - start
            // it before the sequential DB reads below rather than after, so
            // its latency overlaps with theirs instead of adding to them.
            // The three store calls stay sequential on purpose: they share
            // one scoped DbContext, which EF Core does not allow to run
            // more than one operation on concurrently.
            var employeesTask = paycomClient.GetAllEmployeesAsync(cancellationToken);
            var mappings = await fieldMappingStore.GetAllAsync(cancellationToken);
            var groupRules = await groupRuleStore.GetAllAsync(cancellationToken);
            var previousSnapshots = await snapshotStore.GetAllAsync(cancellationToken);
            var lifecycleTasks = await lifecycleTaskStore.GetAllAsync(cancellationToken);
            var employees = await employeesTask;

            run.EmployeesEvaluated = employees.Count;

            var observedFieldNames = employees.SelectMany(e => e.RawFields.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
            await discoveredFieldStore.RecordObservedFieldsAsync(observedFieldNames, run.StartedAt, cancellationToken);

            var asOfDate = DateOnly.FromDateTime(run.StartedAt.UtcDateTime);
            var enableAccountOffset = lifecycleTasks.FirstOrDefault(t => t.Enabled && t.TaskType == LifecycleTaskType.EnableAccount)?.DayOffset ?? 0;
            var disableAccountOffset = lifecycleTasks.FirstOrDefault(t => t.Enabled && t.TaskType == LifecycleTaskType.DisableAccount)?.DayOffset ?? 0;

            var operations = BuildBulkOperations(run, employees, mappings, asOfDate, enableAccountOffset, disableAccountOffset);
            AppendVanishedEmployeeDisableOps(run, employees, previousSnapshots, operations);

            await SubmitOperationsAsync(run, operations, dryRun, cancellationToken);
            await ReconcileGroupsAsync(run, employees, groupRules, mappings, dryRun, cancellationToken);
            await RunLeaverTasksAsync(run, employees, previousSnapshots, lifecycleTasks, mappings, asOfDate, dryRun, cancellationToken);

            await snapshotStore.SaveAsync(employees.Select(e => new EmployeeSnapshot
            {
                EmployeeCode = e.EmployeeCode,
                WorkEmail = e.WorkEmail,
                Status = e.Status,
                ContentHash = e.ComputeContentHash(),
                LastSeenAt = run.StartedAt
            }), cancellationToken);

            // Check every recorded outcome, not just RecordsFailed - group
            // rule/reconciliation errors set Success = false on their own
            // SyncRunEmployeeResult but don't increment RecordsFailed, so a
            // run with only group-side errors would otherwise report as
            // Succeeded.
            run.Status = run.EmployeeResults.Any(r => !r.Success)
                ? SyncRunStatus.CompletedWithErrors
                : SyncRunStatus.Succeeded;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sync run {RunId} failed", run.Id);
            run.Status = SyncRunStatus.Failed;
            run.ErrorSummary = ex.Message;
        }
        finally
        {
            run.CompletedAt = DateTimeOffset.UtcNow;
            await syncRunStore.UpdateAsync(run, cancellationToken);
        }

        return run;
    }

    /// <summary>
    /// Confirms records left <see cref="SyncOutcomes.Submitted"/> by a
    /// previous run against the Entra provisioning audit log - the
    /// authoritative source for per-record outcomes, per Microsoft's own
    /// reference implementation (see docs/architecture.md). Skips the
    /// Graph call entirely when nothing is pending, which is the steady
    /// state once confirmations catch up.
    /// </summary>
    private async Task ReconcilePendingProvisioningConfirmationsAsync(CancellationToken cancellationToken)
    {
        var pending = await syncRunStore.GetPendingSubmissionResultsAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return;
        }

        var logEntries = await provisioningClient.GetRecentProvisioningLogAsync(
            top: 500,
            since: DateTimeOffset.UtcNow.AddHours(-ProvisioningLogLookbackHours),
            cancellationToken: cancellationToken);

        if (logEntries.Count == 0)
        {
            return;
        }

        // Last-write-wins per employee code, in case the log has more than
        // one entry for the same record in the window (e.g. a retry on
        // Entra's own side).
        var latestByEmployeeCode = new Dictionary<string, ProvisioningLogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in logEntries)
        {
            if (entry.EmployeeExternalId is not { } code)
            {
                continue;
            }

            if (!latestByEmployeeCode.TryGetValue(code, out var existing) || entry.Timestamp > existing.Timestamp)
            {
                latestByEmployeeCode[code] = entry;
            }
        }

        var updates = new List<ProvisioningConfirmationUpdate>();
        foreach (var result in pending)
        {
            if (!latestByEmployeeCode.TryGetValue(result.EmployeeCode, out var logEntry))
            {
                // Not in the log yet - the provisioning cycle likely hasn't
                // processed it. Leave it Submitted; the next run tries again.
                continue;
            }

            updates.Add(logEntry.ProvisioningStatus switch
            {
                var s when s.Contains("success", StringComparison.OrdinalIgnoreCase) =>
                    new ProvisioningConfirmationUpdate(result.Id, true, SyncOutcomes.Provisioned, $"Confirmed by the provisioning log: {logEntry.Action}."),
                var s when s.Contains("skip", StringComparison.OrdinalIgnoreCase) =>
                    new ProvisioningConfirmationUpdate(result.Id, true, SyncOutcomes.ProvisioningSkipped, "Entra's provisioning job determined no change was needed."),
                var s =>
                    new ProvisioningConfirmationUpdate(result.Id, false, SyncOutcomes.SubmissionError, logEntry.ErrorMessage ?? $"Provisioning log reported status '{s}'.")
            });
        }

        if (updates.Count > 0)
        {
            await syncRunStore.ApplyProvisioningConfirmationsAsync(updates, cancellationToken);
            logger.LogInformation("Reconciled {Count} pending submission(s) against the provisioning audit log.", updates.Count);
        }
    }

    private List<ScimBulkOperation> BuildBulkOperations(
        SyncRun run,
        IReadOnlyList<EmployeeRecord> employees,
        IReadOnlyList<Configuration.FieldMapping> mappings,
        DateOnly asOfDate,
        int enableAccountOffset,
        int disableAccountOffset)
    {
        var operations = new List<ScimBulkOperation>();

        foreach (var employee in employees)
        {
            var displayName = $"{employee.FirstName} {employee.LastName}".Trim();

            if (!employee.IsProvisionable)
            {
                run.RecordsSkipped++;
                run.EmployeeResults.Add(new SyncRunEmployeeResult
                {
                    SyncRunId = run.Id,
                    EmployeeCode = employee.EmployeeCode,
                    DisplayName = displayName,
                    Outcome = SyncOutcomes.Skipped,
                    Success = true,
                    Detail = $"No work email present (status: {employee.Status}); cannot match to an Entra user."
                });
                continue;
            }

            try
            {
                var resource = mappingEngine.BuildScimResource(employee, mappings, asOfDate, enableAccountOffset, disableAccountOffset);
                operations.Add(new ScimBulkOperation { BulkId = employee.EmployeeCode, Data = resource });
                run.EmployeeResults.Add(new SyncRunEmployeeResult
                {
                    SyncRunId = run.Id,
                    EmployeeCode = employee.EmployeeCode,
                    DisplayName = displayName,
                    Outcome = run.DryRun ? SyncOutcomes.DryRunPreview : SyncOutcomes.Submitted,
                    Success = true,
                    Detail = $"active={resource.Active}"
                });
            }
            catch (ExpressionEvaluationException ex)
            {
                run.RecordsFailed++;
                run.EmployeeResults.Add(new SyncRunEmployeeResult
                {
                    SyncRunId = run.Id,
                    EmployeeCode = employee.EmployeeCode,
                    DisplayName = displayName,
                    Outcome = SyncOutcomes.MappingError,
                    Success = false,
                    Detail = ex.Message
                });
            }
        }

        return operations;
    }

    /// <summary>
    /// Safety net for workers who simply drop out of the Paycom feed
    /// instead of being sent with an explicit Terminated status. Anyone
    /// present in the last snapshot but absent from this run's pull gets a
    /// defensive disable operation queued, and is flagged prominently for
    /// admin review since we're working from stale, cached data for them.
    /// </summary>
    private static void AppendVanishedEmployeeDisableOps(
        SyncRun run,
        IReadOnlyList<EmployeeRecord> employees,
        IReadOnlyDictionary<string, EmployeeSnapshot> previousSnapshots,
        List<ScimBulkOperation> operations)
    {
        var currentCodes = employees.Select(e => e.EmployeeCode).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in previousSnapshots.Values)
        {
            if (currentCodes.Contains(snapshot.EmployeeCode) || snapshot.Status == EmploymentStatus.Terminated)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(snapshot.WorkEmail))
            {
                continue;
            }

            operations.Add(new ScimBulkOperation
            {
                BulkId = snapshot.EmployeeCode,
                Data = new Scim.ScimUserResource
                {
                    ExternalId = snapshot.EmployeeCode,
                    UserName = snapshot.WorkEmail,
                    Active = false
                }
            });

            run.EmployeeResults.Add(new SyncRunEmployeeResult
            {
                SyncRunId = run.Id,
                EmployeeCode = snapshot.EmployeeCode,
                DisplayName = snapshot.WorkEmail,
                Outcome = SyncOutcomes.VanishedFromFeedAutoDisabled,
                Success = true,
                Detail = "Employee was present in a previous sync but absent from this Paycom pull with no Terminated status. " +
                         "Queued a defensive disable; verify this against Paycom before the next run."
            });
        }
    }

    private async Task SubmitOperationsAsync(
        SyncRun run,
        List<ScimBulkOperation> operations,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (operations.Count == 0)
        {
            return;
        }

        if (dryRun)
        {
            run.RecordsSubmitted = operations.Count;
            return;
        }

        var result = await provisioningClient.SubmitBulkUploadAsync(operations, cancellationToken);

        var resultsByBulkId = result.Results
            .Where(r => r.BulkId is not null)
            .ToDictionary(r => r.BulkId!, StringComparer.OrdinalIgnoreCase);

        var employeeResultsByCode = run.EmployeeResults
            .ToDictionary(r => r.EmployeeCode, StringComparer.OrdinalIgnoreCase);

        foreach (var op in operations)
        {
            employeeResultsByCode.TryGetValue(op.BulkId, out var employeeResult);
            resultsByBulkId.TryGetValue(op.BulkId, out var opResult);
            var code = opResult?.Status?.Code;

            if (code is "200" or "201" or "202" or "204" or null)
            {
                // A missing per-op result is the *expected* case, not a
                // failure: Microsoft's own reference implementation doesn't
                // treat bulkUpload's synchronous response as a reliable
                // per-record result either (see docs/architecture.md) - it
                // stays Submitted and gets confirmed or corrected by
                // ReconcilePendingProvisioningConfirmationsAsync on a later
                // run, once the provisioning audit log has caught up.
                run.RecordsSubmitted++;
            }
            else
            {
                // Entra told us synchronously and explicitly that this
                // specific record failed - a real signal worth trusting
                // immediately rather than waiting a cycle to confirm.
                run.RecordsFailed++;
                if (employeeResult is not null)
                {
                    employeeResult.Success = false;
                    employeeResult.Outcome = SyncOutcomes.SubmissionError;
                    employeeResult.Detail = $"Entra returned status {code}";
                }
            }
        }

        foreach (var batchError in result.BatchErrors)
        {
            run.ErrorSummary = string.IsNullOrEmpty(run.ErrorSummary)
                ? batchError
                : $"{run.ErrorSummary}; {batchError}";
        }
    }

    private async Task ReconcileGroupsAsync(
        SyncRun run,
        IReadOnlyList<EmployeeRecord> employees,
        IReadOnlyList<Configuration.GroupAssignmentRule> groupRules,
        IReadOnlyList<Configuration.FieldMapping> mappings,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (groupRules.Count == 0)
        {
            return;
        }

        var fullyManagedGroups = groupRules
            .Where(r => r.Enabled && r.RemoveWhenConditionFails)
            .Select(r => r.TargetGroupObjectId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var desiredMembersByGroup = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var employee in employees.Where(e => e.IsProvisionable))
        {
            var evaluation = groupRuleEvaluator.EvaluateAll(employee, groupRules);

            foreach (var error in evaluation.Errors)
            {
                run.EmployeeResults.Add(new SyncRunEmployeeResult
                {
                    SyncRunId = run.Id,
                    EmployeeCode = employee.EmployeeCode,
                    DisplayName = $"{employee.FirstName} {employee.LastName}".Trim(),
                    Outcome = SyncOutcomes.GroupRuleError,
                    Success = false,
                    Detail = error.Message
                });
            }

            if (evaluation.GroupsToAdd.Count == 0)
            {
                continue;
            }

            // Resolve the same identifier actually submitted to Entra as
            // userPrincipalName (whatever FieldMapping targets it, with
            // whatever transform), rather than assuming WorkEmail matches
            // it - those diverge whenever the admin maps a different/
            // transformed UPN, which silently made every group lookup
            // fail (and, combined with a fully-managed group, wiped its
            // membership) before this fix.
            var matchingAttributes = mappingEngine.ResolveMatchingAttributes(employee, mappings);
            var upn = matchingAttributes.GetValueOrDefault("userPrincipalName")
                ?? matchingAttributes.GetValueOrDefault("username")
                ?? employee.WorkEmail!;

            foreach (var groupId in evaluation.GroupsToAdd)
            {
                (desiredMembersByGroup.TryGetValue(groupId, out var set)
                    ? set
                    : desiredMembersByGroup[groupId] = []).Add(upn);
            }
        }

        var allGroupIds = desiredMembersByGroup.Keys.Union(fullyManagedGroups, StringComparer.OrdinalIgnoreCase);

        if (dryRun)
        {
            foreach (var groupId in allGroupIds)
            {
                var desiredUpns = desiredMembersByGroup.GetValueOrDefault(groupId, []);
                run.EmployeeResults.Add(new SyncRunEmployeeResult
                {
                    SyncRunId = run.Id,
                    EmployeeCode = "(group)",
                    DisplayName = groupId,
                    Outcome = SyncOutcomes.DryRunGroupPreview,
                    Success = true,
                    Detail = $"{desiredUpns.Count} employee(s) currently match rules targeting this group."
                });
            }

            return;
        }

        // Resolve every distinct UPN across every group exactly once - an
        // employee who matches rules for 3 groups used to trigger the same
        // Graph lookup 3 times, once per group, instead of once overall.
        var distinctUpns = desiredMembersByGroup.Values.SelectMany(set => set).Distinct(StringComparer.OrdinalIgnoreCase);
        var objectIdsByUpn = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var failuresByUpn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var upn in distinctUpns)
        {
            try
            {
                objectIdsByUpn[upn] = await directoryClient.FindUserObjectIdAsync(upn, cancellationToken);
            }
            catch (Exception ex)
            {
                // Caught here (rather than letting it propagate to the outer
                // try/catch in RunAsync) for two reasons: one bad lookup
                // shouldn't fail the whole run when bulkUpload already
                // succeeded, and - more importantly - silently treating a
                // failed lookup the same as "not found" would shrink
                // desiredObjectIds and could wipe a fully-managed group's
                // entire membership below on a merely transient Graph error.
                failuresByUpn[upn] = ex.Message;
                logger.LogError(ex, "Failed resolving Entra object id for {Upn} during group reconciliation", upn);
            }
        }

        foreach (var groupId in allGroupIds)
        {
            var desiredUpns = desiredMembersByGroup.GetValueOrDefault(groupId, []);
            var failedUpns = desiredUpns.Where(failuresByUpn.ContainsKey).ToList();

            if (failedUpns.Count > 0)
            {
                run.EmployeeResults.Add(new SyncRunEmployeeResult
                {
                    SyncRunId = run.Id,
                    EmployeeCode = "(group)",
                    DisplayName = groupId,
                    Outcome = SyncOutcomes.GroupReconciliationError,
                    Success = false,
                    Detail = "Skipped reconciling this group this run because one or more member lookups failed " +
                             "(retrying next run rather than risk removing members based on an incomplete list): " +
                             string.Join("; ", failedUpns.Select(upn => $"{upn}: {failuresByUpn[upn]}"))
                });
                continue;
            }

            var desiredObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var upn in desiredUpns)
            {
                if (objectIdsByUpn.GetValueOrDefault(upn) is { } objectId)
                {
                    desiredObjectIds.Add(objectId);
                }
            }

            if (!fullyManagedGroups.Contains(groupId) && desiredObjectIds.Count == 0)
            {
                continue;
            }

            var reconciliation = await directoryClient.ReconcileGroupMembersAsync(groupId, desiredObjectIds, cancellationToken);
            run.GroupMembershipsAdded += reconciliation.MembersAdded.Count;
            run.GroupMembershipsRemoved += reconciliation.MembersRemoved.Count;

            if (reconciliation.Errors.Count > 0)
            {
                run.EmployeeResults.Add(new SyncRunEmployeeResult
                {
                    SyncRunId = run.Id,
                    EmployeeCode = "(group)",
                    DisplayName = groupId,
                    Outcome = SyncOutcomes.GroupReconciliationError,
                    Success = false,
                    Detail = string.Join("; ", reconciliation.Errors)
                });
            }
        }
    }

    /// <summary>
    /// Runs every enabled, one-time Leaver task (revoke sessions, remove
    /// from groups, delete account - not EnableAccount/DisableAccount,
    /// which are continuous state handled via the SCIM <c>active</c> flag
    /// in <see cref="BuildBulkOperations"/> instead) whose configured day
    /// offset from the employee's termination - or, for a worker who
    /// vanished from the feed entirely, their last-seen date - has been
    /// reached. Each (task, employee) pair runs at most once, ever,
    /// tracked via <see cref="ILifecycleTaskStore"/>.
    /// </summary>
    private async Task RunLeaverTasksAsync(
        SyncRun run,
        IReadOnlyList<EmployeeRecord> employees,
        IReadOnlyDictionary<string, EmployeeSnapshot> previousSnapshots,
        IReadOnlyList<Configuration.LifecycleTask> lifecycleTasks,
        IReadOnlyList<Configuration.FieldMapping> mappings,
        DateOnly asOfDate,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var oneTimeTasks = lifecycleTasks
            .Where(t => t.Enabled && t.Trigger == LifecycleTrigger.Leaver
                        && t.TaskType is not (LifecycleTaskType.EnableAccount or LifecycleTaskType.DisableAccount))
            .ToList();

        if (oneTimeTasks.Count == 0)
        {
            return;
        }

        // Trigger date + best-known UPN for every employee a Leaver task
        // could apply to: explicitly Terminated this run (using the real
        // EmployeeRecord to resolve the actual configured UPN), or
        // vanished from the feed entirely (falling back to the snapshot's
        // raw work email, the same best-effort limitation the vanished-
        // employee disable safety net already documents).
        var currentCodes = employees.Select(e => e.EmployeeCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var triggerInfo = new Dictionary<string, (DateOnly TriggerDate, string Upn)>(StringComparer.OrdinalIgnoreCase);

        foreach (var employee in employees.Where(e => e.Status == EmploymentStatus.Terminated && e.TerminationDate is not null))
        {
            var matchingAttributes = mappingEngine.ResolveMatchingAttributes(employee, mappings);
            var upn = matchingAttributes.GetValueOrDefault("userPrincipalName")
                ?? matchingAttributes.GetValueOrDefault("username")
                ?? employee.WorkEmail;

            if (upn is not null)
            {
                triggerInfo[employee.EmployeeCode] = (employee.TerminationDate!.Value, upn);
            }
        }

        foreach (var snapshot in previousSnapshots.Values)
        {
            if (currentCodes.Contains(snapshot.EmployeeCode)
                || triggerInfo.ContainsKey(snapshot.EmployeeCode)
                || string.IsNullOrWhiteSpace(snapshot.WorkEmail))
            {
                continue;
            }

            triggerInfo[snapshot.EmployeeCode] = (DateOnly.FromDateTime(snapshot.LastSeenAt.UtcDateTime), snapshot.WorkEmail);
        }

        if (triggerInfo.Count == 0)
        {
            return;
        }

        if (dryRun)
        {
            foreach (var task in oneTimeTasks)
            {
                var alreadyExecuted = await lifecycleTaskStore.GetSuccessfullyExecutedEmployeeCodesAsync(task.Id, cancellationToken);
                var due = triggerInfo
                    .Where(kv => !alreadyExecuted.Contains(kv.Key) && asOfDate >= kv.Value.TriggerDate.AddDays(task.DayOffset))
                    .Select(kv => kv.Key)
                    .ToList();

                if (due.Count > 0)
                {
                    run.EmployeeResults.Add(new SyncRunEmployeeResult
                    {
                        SyncRunId = run.Id,
                        EmployeeCode = "(lifecycle)",
                        DisplayName = task.TaskType.ToString(),
                        Outcome = SyncOutcomes.DryRunLifecyclePreview,
                        Success = true,
                        Detail = $"Would run for: {string.Join(", ", due)}"
                    });
                }
            }

            return;
        }

        // Resolve each employee's object id once, reused across every task
        // that applies to them, rather than once per task.
        var objectIdsByCode = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, info) in triggerInfo)
        {
            try
            {
                objectIdsByCode[code] = await directoryClient.FindUserObjectIdAsync(info.Upn, cancellationToken);
            }
            catch (Exception ex)
            {
                objectIdsByCode[code] = null;
                logger.LogError(ex, "Failed resolving Entra object id for {EmployeeCode} while running leaver tasks", code);
            }
        }

        foreach (var task in oneTimeTasks)
        {
            var alreadyExecuted = await lifecycleTaskStore.GetSuccessfullyExecutedEmployeeCodesAsync(task.Id, cancellationToken);

            foreach (var (code, info) in triggerInfo)
            {
                if (alreadyExecuted.Contains(code) || asOfDate < info.TriggerDate.AddDays(task.DayOffset))
                {
                    continue;
                }

                if (objectIdsByCode.GetValueOrDefault(code) is not { } objectId)
                {
                    continue;
                }

                var (success, detail) = await ExecuteLifecycleTaskAsync(task.TaskType, objectId, cancellationToken);

                await lifecycleTaskStore.RecordExecutionAsync(new LifecycleTaskExecution
                {
                    LifecycleTaskId = task.Id,
                    EmployeeCode = code,
                    TaskType = task.TaskType,
                    ExecutedAt = DateTimeOffset.UtcNow,
                    Success = success,
                    Detail = detail
                }, cancellationToken);

                run.EmployeeResults.Add(new SyncRunEmployeeResult
                {
                    SyncRunId = run.Id,
                    EmployeeCode = code,
                    DisplayName = $"{task.TaskType}: {info.Upn}",
                    Outcome = success ? SyncOutcomes.LifecycleTaskCompleted : SyncOutcomes.LifecycleTaskError,
                    Success = success,
                    Detail = detail
                });
            }
        }
    }

    private async Task<(bool Success, string Detail)> ExecuteLifecycleTaskAsync(
        LifecycleTaskType taskType,
        string objectId,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (taskType)
            {
                case LifecycleTaskType.RevokeSignInSessions:
                    await directoryClient.RevokeSignInSessionsAsync(objectId, cancellationToken);
                    return (true, "Sign-in sessions and refresh tokens revoked.");

                case LifecycleTaskType.RemoveFromAllAssignedGroups:
                    var cleanup = await directoryClient.RemoveUserFromAllGroupsAsync(objectId, cancellationToken);
                    return cleanup.Errors.Count == 0
                        ? (true, $"Removed from {cleanup.GroupsRemovedFrom.Count} assigned group(s).")
                        : (false, $"Removed from {cleanup.GroupsRemovedFrom.Count} group(s); errors: {string.Join("; ", cleanup.Errors)}");

                case LifecycleTaskType.DeleteAccount:
                    await directoryClient.DeleteUserAsync(objectId, cancellationToken);
                    return (true, "Account deleted (Entra soft-deletes for 30 days; recoverable in that window).");

                default:
                    return (false, $"{taskType} is not a one-time executable task.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lifecycle task {TaskType} failed for object {ObjectId}", taskType, objectId);
            return (false, ex.Message);
        }
    }
}

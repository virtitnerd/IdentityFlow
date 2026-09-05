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
    MappingEngine mappingEngine,
    GroupRuleEvaluator groupRuleEvaluator,
    ILogger<SyncOrchestrator> logger)
{
    public async Task<SyncRun> RunAsync(
        SyncTrigger trigger,
        string? triggeredByUser,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
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
            var employees = await paycomClient.GetAllEmployeesAsync(cancellationToken);
            var mappings = await fieldMappingStore.GetAllAsync(cancellationToken);
            var groupRules = await groupRuleStore.GetAllAsync(cancellationToken);
            var previousSnapshots = await snapshotStore.GetAllAsync(cancellationToken);

            run.EmployeesEvaluated = employees.Count;

            var observedFieldNames = employees.SelectMany(e => e.RawFields.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
            await discoveredFieldStore.RecordObservedFieldsAsync(observedFieldNames, run.StartedAt, cancellationToken);

            var operations = BuildBulkOperations(run, employees, mappings);
            AppendVanishedEmployeeDisableOps(run, employees, previousSnapshots, operations);

            await SubmitOperationsAsync(run, operations, dryRun, cancellationToken);
            await ReconcileGroupsAsync(run, employees, groupRules, dryRun, cancellationToken);

            await snapshotStore.SaveAsync(employees.Select(e => new EmployeeSnapshot
            {
                EmployeeCode = e.EmployeeCode,
                WorkEmail = e.WorkEmail,
                Status = e.Status,
                ContentHash = e.ComputeContentHash(),
                LastSeenAt = run.StartedAt
            }), cancellationToken);

            run.Status = run.RecordsFailed > 0 ? SyncRunStatus.CompletedWithErrors : SyncRunStatus.Succeeded;
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

    private List<ScimBulkOperation> BuildBulkOperations(
        SyncRun run,
        IReadOnlyList<EmployeeRecord> employees,
        IReadOnlyList<Configuration.FieldMapping> mappings)
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
                    Outcome = "Skipped",
                    Success = true,
                    Detail = $"No work email present (status: {employee.Status}); cannot match to an Entra user."
                });
                continue;
            }

            try
            {
                var resource = mappingEngine.BuildScimResource(employee, mappings);
                operations.Add(new ScimBulkOperation { BulkId = employee.EmployeeCode, Data = resource });
                run.EmployeeResults.Add(new SyncRunEmployeeResult
                {
                    SyncRunId = run.Id,
                    EmployeeCode = employee.EmployeeCode,
                    DisplayName = displayName,
                    Outcome = run.DryRun ? "DryRunPreview" : "Queued",
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
                    Outcome = "MappingError",
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

            run.RecordsFailed += 0; // not a failure, just needs attention
            run.EmployeeResults.Add(new SyncRunEmployeeResult
            {
                SyncRunId = run.Id,
                EmployeeCode = snapshot.EmployeeCode,
                DisplayName = snapshot.WorkEmail,
                Outcome = "VanishedFromFeed-AutoDisabled",
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

        foreach (var op in operations)
        {
            var employeeResult = run.EmployeeResults.FirstOrDefault(r =>
                string.Equals(r.EmployeeCode, op.BulkId, StringComparison.OrdinalIgnoreCase));

            var succeeded = resultsByBulkId.TryGetValue(op.BulkId, out var opResult)
                && opResult.Status?.Code is "200" or "201" or "204";

            if (succeeded)
            {
                run.RecordsSubmitted++;
            }
            else
            {
                run.RecordsFailed++;
                if (employeeResult is not null)
                {
                    employeeResult.Success = false;
                    employeeResult.Outcome = "SubmissionError";
                    employeeResult.Detail = opResult?.Status?.Code is { } code
                        ? $"Entra returned status {code}"
                        : "No result returned for this bulkId - see provisioning logs in the Entra admin center.";
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
                    Outcome = "GroupRuleError",
                    Success = false,
                    Detail = error.Message
                });
            }

            foreach (var groupId in evaluation.GroupsToAdd)
            {
                (desiredMembersByGroup.TryGetValue(groupId, out var set)
                    ? set
                    : desiredMembersByGroup[groupId] = []).Add(employee.WorkEmail!);
            }
        }

        var allGroupIds = desiredMembersByGroup.Keys.Union(fullyManagedGroups, StringComparer.OrdinalIgnoreCase);

        foreach (var groupId in allGroupIds)
        {
            var desiredUpns = desiredMembersByGroup.GetValueOrDefault(groupId, []);

            if (dryRun)
            {
                run.EmployeeResults.Add(new SyncRunEmployeeResult
                {
                    SyncRunId = run.Id,
                    EmployeeCode = "(group)",
                    DisplayName = groupId,
                    Outcome = "DryRunGroupPreview",
                    Success = true,
                    Detail = $"{desiredUpns.Count} employee(s) currently match rules targeting this group."
                });
                continue;
            }

            var desiredObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var upn in desiredUpns)
            {
                var objectId = await directoryClient.FindUserObjectIdAsync(upn, cancellationToken);
                if (objectId is not null)
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
                    Outcome = "GroupReconciliationError",
                    Success = false,
                    Detail = string.Join("; ", reconciliation.Errors)
                });
            }
        }
    }
}

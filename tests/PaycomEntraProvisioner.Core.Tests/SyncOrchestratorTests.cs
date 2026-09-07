using Microsoft.Extensions.Logging.Abstractions;
using PaycomEntraProvisioner.Core.Abstractions;
using PaycomEntraProvisioner.Core.Configuration;
using PaycomEntraProvisioner.Core.Domain;
using PaycomEntraProvisioner.Core.GroupRules;
using PaycomEntraProvisioner.Core.Mapping;
using PaycomEntraProvisioner.Core.Scim;
using PaycomEntraProvisioner.Core.Sync;
using Xunit;

namespace PaycomEntraProvisioner.Core.Tests;

public class SyncOrchestratorTests
{
    private static EmployeeRecord Employee(string code, string email, string department = "Sales") => new()
    {
        EmployeeCode = code,
        WorkEmail = email,
        Department = department,
        Status = EmploymentStatus.Active,
        RawFields = new Dictionary<string, string?>
        {
            ["EmployeeCode"] = code,
            ["WorkEmail"] = email,
            ["Department"] = department
        }
    };

    private static SyncOrchestrator CreateOrchestrator(
        IReadOnlyList<EmployeeRecord> employees,
        IEntraProvisioningClient provisioningClient,
        IEntraDirectoryClient directoryClient,
        IReadOnlyList<GroupAssignmentRule>? groupRules = null,
        IReadOnlyList<FieldMapping>? mappings = null,
        ISyncRunStore? syncRunStore = null,
        IReadOnlyList<LifecycleTask>? lifecycleTasks = null,
        ILifecycleTaskStore? lifecycleTaskStore = null,
        IReadOnlyDictionary<string, EmployeeSnapshot>? snapshots = null)
    {
        return new SyncOrchestrator(
            new FakePaycomClient(employees),
            provisioningClient,
            directoryClient,
            new FakeFieldMappingStore(mappings ?? []),
            new FakeGroupAssignmentRuleStore(groupRules ?? []),
            syncRunStore ?? new FakeSyncRunStore(),
            new FakeEmployeeSnapshotStore(snapshots),
            new FakeDiscoveredFieldStore(),
            lifecycleTaskStore ?? new FakeLifecycleTaskStore(lifecycleTasks ?? []),
            new MappingEngine(),
            new GroupRuleEvaluator(),
            NullLogger<SyncOrchestrator>.Instance);
    }

    [Fact]
    public async Task RunAsync_TreatsHttp202AsSuccess()
    {
        var employees = new List<EmployeeRecord> { Employee("E1", "e1@contoso.com") };
        var mappings = new List<FieldMapping>
        {
            new() { SourceField = "WorkEmail", TargetAttribute = "userPrincipalName", IsMatchingAttribute = true }
        };
        var provisioningClient = new FakeProvisioningClient(bulkId => new ScimStatus { Code = "202" });

        var orchestrator = CreateOrchestrator(employees, provisioningClient, new FakeDirectoryClient(), mappings: mappings);
        var run = await orchestrator.RunAsync(SyncTrigger.Manual, "tester", dryRun: false);

        Assert.Equal(1, run.RecordsSubmitted);
        Assert.Equal(0, run.RecordsFailed);
        Assert.Equal(SyncRunStatus.Succeeded, run.Status);
    }

    [Fact]
    public async Task RunAsync_GroupLookupFailure_DoesNotWipeFullyManagedGroup()
    {
        var employees = new List<EmployeeRecord> { Employee("E1", "e1@contoso.com") };
        var mappings = new List<FieldMapping>
        {
            new() { SourceField = "WorkEmail", TargetAttribute = "userPrincipalName", IsMatchingAttribute = true }
        };
        var groupRules = new List<GroupAssignmentRule>
        {
            new() { Name = "Sales", Condition = "employee.Department == \"Sales\"", TargetGroupObjectId = "grp-1", RemoveWhenConditionFails = true }
        };

        var directoryClient = new FakeDirectoryClient { ThrowOnLookup = true };
        var provisioningClient = new FakeProvisioningClient(_ => new ScimStatus { Code = "201" });

        var orchestrator = CreateOrchestrator(employees, provisioningClient, directoryClient, groupRules, mappings);
        var run = await orchestrator.RunAsync(SyncTrigger.Manual, "tester", dryRun: false);

        // The lookup failure must not translate into calling
        // ReconcileGroupMembersAsync with an empty desired set (which would
        // remove every existing member of a fully-managed group).
        Assert.False(directoryClient.ReconcileWasCalled);
        Assert.Contains(run.EmployeeResults, r => r.Outcome == "GroupReconciliationError" && !r.Success);
        Assert.Equal(SyncRunStatus.CompletedWithErrors, run.Status);
    }

    [Fact]
    public async Task RunAsync_UsesConfiguredUpnMappingForGroupLookup_NotRawWorkEmail()
    {
        var employees = new List<EmployeeRecord> { Employee("E1", "e1@personal-domain.com") };
        var mappings = new List<FieldMapping>
        {
            // UPN deliberately differs from WorkEmail via a transform, as a
            // real tenant's mapping legitimately might.
            new() { SourceField = "EmployeeCode", TargetAttribute = "userPrincipalName", IsMatchingAttribute = true, TransformExpression = "value + \"@contoso.com\"" }
        };
        var groupRules = new List<GroupAssignmentRule>
        {
            new() { Name = "Sales", Condition = "employee.Department == \"Sales\"", TargetGroupObjectId = "grp-1", RemoveWhenConditionFails = true }
        };

        var directoryClient = new FakeDirectoryClient();
        var provisioningClient = new FakeProvisioningClient(_ => new ScimStatus { Code = "201" });

        var orchestrator = CreateOrchestrator(employees, provisioningClient, directoryClient, groupRules, mappings);
        await orchestrator.RunAsync(SyncTrigger.Manual, "tester", dryRun: false);

        Assert.Contains("E1@contoso.com", directoryClient.LookedUpUpns);
        Assert.DoesNotContain("e1@personal-domain.com", directoryClient.LookedUpUpns);
    }

    [Fact]
    public async Task RunAsync_ResolvesEachEmployeeObjectIdOnceAcrossMultipleMatchingGroups()
    {
        // An employee matching more than one group rule used to trigger a
        // separate Graph lookup per group instead of once overall.
        var employees = new List<EmployeeRecord> { Employee("E1", "e1@contoso.com") };
        var mappings = new List<FieldMapping>
        {
            new() { SourceField = "WorkEmail", TargetAttribute = "userPrincipalName", IsMatchingAttribute = true }
        };
        var groupRules = new List<GroupAssignmentRule>
        {
            new() { Name = "Sales", Condition = "employee.Department == \"Sales\"", TargetGroupObjectId = "grp-1", RemoveWhenConditionFails = true },
            new() { Name = "AllStaff", Condition = "true", TargetGroupObjectId = "grp-2", RemoveWhenConditionFails = true }
        };

        var directoryClient = new FakeDirectoryClient();
        var provisioningClient = new FakeProvisioningClient(_ => new ScimStatus { Code = "201" });

        var orchestrator = CreateOrchestrator(employees, provisioningClient, directoryClient, groupRules, mappings);
        await orchestrator.RunAsync(SyncTrigger.Manual, "tester", dryRun: false);

        Assert.Single(directoryClient.LookedUpUpns);
        Assert.Equal("e1@contoso.com", directoryClient.LookedUpUpns[0]);
    }

    [Fact]
    public async Task RunAsync_WithNoPendingSubmissions_NeverQueriesTheProvisioningLog()
    {
        // Efficiency guard: the reconciliation pass must not call Graph at
        // all in the steady state where nothing needs confirming.
        var store = new FakeSyncRunStore();
        var provisioningClient = new FakeProvisioningClient(_ => new ScimStatus { Code = "201" });
        var orchestrator = CreateOrchestrator([], provisioningClient, new FakeDirectoryClient(), syncRunStore: store);

        await orchestrator.RunAsync(SyncTrigger.Manual, "tester", dryRun: false);

        Assert.Equal(0, provisioningClient.LogQueryCount);
    }

    [Fact]
    public async Task RunAsync_ReconcilesPendingSubmissionAsProvisionedWhenLogConfirmsSuccess()
    {
        var store = new FakeSyncRunStore();
        var mappings = new List<FieldMapping>
        {
            new() { SourceField = "WorkEmail", TargetAttribute = "userPrincipalName", IsMatchingAttribute = true }
        };

        // Run 1: submit with no per-op result in the sync response (the
        // common real-world case) - the record should land Submitted, not
        // SubmissionError, and stay that way until reconciled.
        var run1Employees = new List<EmployeeRecord> { Employee("E1", "e1@contoso.com") };
        var run1Client = new FakeProvisioningClient(_ => new ScimStatus { Code = null! });
        var orchestrator1 = CreateOrchestrator(run1Employees, run1Client, new FakeDirectoryClient(), mappings: mappings, syncRunStore: store);
        var run1 = await orchestrator1.RunAsync(SyncTrigger.Timer, null, dryRun: false);

        var pendingResult = Assert.Single(run1.EmployeeResults, r => r.EmployeeCode == "E1");
        Assert.Equal(SyncOutcomes.Submitted, pendingResult.Outcome);

        // Run 2: the provisioning log now confirms E1 succeeded.
        var logEntries = new List<ProvisioningLogEntry>
        {
            new("E1", "Create", "success", DateTimeOffset.UtcNow, null)
        };
        var run2Client = new FakeProvisioningClient(_ => new ScimStatus { Code = "201" }, logEntries);
        var orchestrator2 = CreateOrchestrator([], run2Client, new FakeDirectoryClient(), mappings: mappings, syncRunStore: store);
        await orchestrator2.RunAsync(SyncTrigger.Timer, null, dryRun: false);

        var confirmed = await store.GetByIdAsync(run1.Id);
        var e1Result = Assert.Single(confirmed!.EmployeeResults, r => r.EmployeeCode == "E1");
        Assert.Equal(SyncOutcomes.Provisioned, e1Result.Outcome);
        Assert.True(e1Result.Success);
    }

    [Fact]
    public async Task RunAsync_ReconcilesPendingSubmissionAsSubmissionErrorWhenLogConfirmsFailure()
    {
        var store = new FakeSyncRunStore();
        var mappings = new List<FieldMapping>
        {
            new() { SourceField = "WorkEmail", TargetAttribute = "userPrincipalName", IsMatchingAttribute = true }
        };

        var run1Employees = new List<EmployeeRecord> { Employee("E1", "e1@contoso.com") };
        var run1Client = new FakeProvisioningClient(_ => new ScimStatus { Code = null! });
        var orchestrator1 = CreateOrchestrator(run1Employees, run1Client, new FakeDirectoryClient(), mappings: mappings, syncRunStore: store);
        var run1 = await orchestrator1.RunAsync(SyncTrigger.Timer, null, dryRun: false);

        var logEntries = new List<ProvisioningLogEntry>
        {
            new("E1", "Update", "failure", DateTimeOffset.UtcNow, "Attribute 'department' failed validation.")
        };
        var run2Client = new FakeProvisioningClient(_ => new ScimStatus { Code = "201" }, logEntries);
        var orchestrator2 = CreateOrchestrator([], run2Client, new FakeDirectoryClient(), mappings: mappings, syncRunStore: store);
        await orchestrator2.RunAsync(SyncTrigger.Timer, null, dryRun: false);

        var confirmed = await store.GetByIdAsync(run1.Id);
        var e1Result = Assert.Single(confirmed!.EmployeeResults, r => r.EmployeeCode == "E1");
        Assert.Equal(SyncOutcomes.SubmissionError, e1Result.Outcome);
        Assert.False(e1Result.Success);
        Assert.Equal("Attribute 'department' failed validation.", e1Result.Detail);
    }

    [Fact]
    public async Task RunAsync_LeavesSubmissionPendingWhenNotYetInTheProvisioningLog()
    {
        var store = new FakeSyncRunStore();
        var mappings = new List<FieldMapping>
        {
            new() { SourceField = "WorkEmail", TargetAttribute = "userPrincipalName", IsMatchingAttribute = true }
        };

        var run1Employees = new List<EmployeeRecord> { Employee("E1", "e1@contoso.com") };
        var run1Client = new FakeProvisioningClient(_ => new ScimStatus { Code = null! });
        var orchestrator1 = CreateOrchestrator(run1Employees, run1Client, new FakeDirectoryClient(), mappings: mappings, syncRunStore: store);
        var run1 = await orchestrator1.RunAsync(SyncTrigger.Timer, null, dryRun: false);

        // The log has entries, but none for E1 yet - the provisioning cycle
        // presumably hasn't processed it.
        var logEntries = new List<ProvisioningLogEntry>
        {
            new("SomeoneElse", "Create", "success", DateTimeOffset.UtcNow, null)
        };
        var run2Client = new FakeProvisioningClient(_ => new ScimStatus { Code = "201" }, logEntries);
        var orchestrator2 = CreateOrchestrator([], run2Client, new FakeDirectoryClient(), mappings: mappings, syncRunStore: store);
        await orchestrator2.RunAsync(SyncTrigger.Timer, null, dryRun: false);

        var confirmed = await store.GetByIdAsync(run1.Id);
        var e1Result = Assert.Single(confirmed!.EmployeeResults, r => r.EmployeeCode == "E1");
        Assert.Equal(SyncOutcomes.Submitted, e1Result.Outcome);
    }

    [Fact]
    public async Task RunAsync_RunsLeaverTask_ForTerminatedEmployeeOnceOffsetElapses()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var employees = new List<EmployeeRecord>
        {
            new()
            {
                EmployeeCode = "E1",
                WorkEmail = "e1@contoso.com",
                Status = EmploymentStatus.Terminated,
                TerminationDate = today,
                RawFields = new Dictionary<string, string?> { ["WorkEmail"] = "e1@contoso.com" }
            }
        };
        var mappings = new List<FieldMapping>
        {
            new() { SourceField = "WorkEmail", TargetAttribute = "userPrincipalName", IsMatchingAttribute = true }
        };
        var task = new LifecycleTask { Id = 1, Trigger = LifecycleTrigger.Leaver, TaskType = LifecycleTaskType.RevokeSignInSessions, DayOffset = 0, Enabled = true };
        var directoryClient = new FakeDirectoryClient();
        var provisioningClient = new FakeProvisioningClient(_ => new ScimStatus { Code = "201" });

        var orchestrator = CreateOrchestrator(employees, provisioningClient, directoryClient, mappings: mappings, lifecycleTasks: [task]);
        var run = await orchestrator.RunAsync(SyncTrigger.Manual, "tester", dryRun: false);

        Assert.Contains("obj-e1@contoso.com", directoryClient.RevokedSessionsForObjectIds);
        Assert.Contains(run.EmployeeResults, r => r.EmployeeCode == "E1" && r.Outcome == SyncOutcomes.LifecycleTaskCompleted);
    }

    [Fact]
    public async Task RunAsync_DoesNotRunLeaverTask_BeforeItsDayOffsetElapses()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var employees = new List<EmployeeRecord>
        {
            new()
            {
                EmployeeCode = "E1",
                WorkEmail = "e1@contoso.com",
                Status = EmploymentStatus.Terminated,
                TerminationDate = today,
                RawFields = new Dictionary<string, string?> { ["WorkEmail"] = "e1@contoso.com" }
            }
        };
        var mappings = new List<FieldMapping>
        {
            new() { SourceField = "WorkEmail", TargetAttribute = "userPrincipalName", IsMatchingAttribute = true }
        };
        // Offset far in the future relative to today's termination date - not due yet.
        var task = new LifecycleTask { Id = 1, Trigger = LifecycleTrigger.Leaver, TaskType = LifecycleTaskType.DeleteAccount, DayOffset = 30, Enabled = true };
        var directoryClient = new FakeDirectoryClient();
        var provisioningClient = new FakeProvisioningClient(_ => new ScimStatus { Code = "201" });

        var orchestrator = CreateOrchestrator(employees, provisioningClient, directoryClient, mappings: mappings, lifecycleTasks: [task]);
        await orchestrator.RunAsync(SyncTrigger.Manual, "tester", dryRun: false);

        Assert.Empty(directoryClient.DeletedObjectIds);
    }

    [Fact]
    public async Task RunAsync_NeverRunsTheSameLeaverTaskTwiceForTheSameEmployee()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var employees = new List<EmployeeRecord>
        {
            new()
            {
                EmployeeCode = "E1",
                WorkEmail = "e1@contoso.com",
                Status = EmploymentStatus.Terminated,
                TerminationDate = today,
                RawFields = new Dictionary<string, string?> { ["WorkEmail"] = "e1@contoso.com" }
            }
        };
        var mappings = new List<FieldMapping>
        {
            new() { SourceField = "WorkEmail", TargetAttribute = "userPrincipalName", IsMatchingAttribute = true }
        };
        var task = new LifecycleTask { Id = 1, Trigger = LifecycleTrigger.Leaver, TaskType = LifecycleTaskType.RemoveFromAllAssignedGroups, DayOffset = 0, Enabled = true };
        var directoryClient = new FakeDirectoryClient();
        var provisioningClient = new FakeProvisioningClient(_ => new ScimStatus { Code = "201" });
        var lifecycleTaskStore = new FakeLifecycleTaskStore([task]);

        var orchestrator1 = CreateOrchestrator(employees, provisioningClient, directoryClient, mappings: mappings, lifecycleTaskStore: lifecycleTaskStore);
        await orchestrator1.RunAsync(SyncTrigger.Manual, "tester", dryRun: false);

        var orchestrator2 = CreateOrchestrator(employees, provisioningClient, directoryClient, mappings: mappings, lifecycleTaskStore: lifecycleTaskStore);
        await orchestrator2.RunAsync(SyncTrigger.Manual, "tester", dryRun: false);

        Assert.Single(directoryClient.RemovedFromGroupsForObjectIds);
    }

    [Fact]
    public async Task RunAsync_RunsLeaverTask_ForEmployeeThatVanishedFromFeedUsingLastSeenDate()
    {
        var lastSeen = DateTimeOffset.UtcNow.AddDays(-10);
        var snapshots = new Dictionary<string, EmployeeSnapshot>
        {
            ["E1"] = new() { EmployeeCode = "E1", WorkEmail = "e1@contoso.com", Status = EmploymentStatus.Active, ContentHash = "h", LastSeenAt = lastSeen }
        };
        var task = new LifecycleTask { Id = 1, Trigger = LifecycleTrigger.Leaver, TaskType = LifecycleTaskType.RevokeSignInSessions, DayOffset = 0, Enabled = true };
        var directoryClient = new FakeDirectoryClient();
        var provisioningClient = new FakeProvisioningClient(_ => new ScimStatus { Code = "201" });

        var orchestrator = CreateOrchestrator([], provisioningClient, directoryClient, lifecycleTasks: [task], snapshots: snapshots);
        var run = await orchestrator.RunAsync(SyncTrigger.Manual, "tester", dryRun: false);

        Assert.Contains("obj-e1@contoso.com", directoryClient.RevokedSessionsForObjectIds);
        Assert.Contains(run.EmployeeResults, r => r.EmployeeCode == "E1" && r.Outcome == SyncOutcomes.LifecycleTaskCompleted);
    }

    [Fact]
    public async Task RunAsync_DryRun_PreviewsDueLeaverTasksWithoutExecutingThem()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var employees = new List<EmployeeRecord>
        {
            new()
            {
                EmployeeCode = "E1",
                WorkEmail = "e1@contoso.com",
                Status = EmploymentStatus.Terminated,
                TerminationDate = today,
                RawFields = new Dictionary<string, string?> { ["WorkEmail"] = "e1@contoso.com" }
            }
        };
        var mappings = new List<FieldMapping>
        {
            new() { SourceField = "WorkEmail", TargetAttribute = "userPrincipalName", IsMatchingAttribute = true }
        };
        var task = new LifecycleTask { Id = 1, Trigger = LifecycleTrigger.Leaver, TaskType = LifecycleTaskType.RevokeSignInSessions, DayOffset = 0, Enabled = true };
        var directoryClient = new FakeDirectoryClient();
        var provisioningClient = new FakeProvisioningClient(_ => new ScimStatus { Code = "201" });

        var orchestrator = CreateOrchestrator(employees, provisioningClient, directoryClient, mappings: mappings, lifecycleTasks: [task]);
        var run = await orchestrator.RunAsync(SyncTrigger.Manual, "tester", dryRun: true);

        Assert.Empty(directoryClient.RevokedSessionsForObjectIds);
        Assert.Contains(run.EmployeeResults, r => r.Outcome == SyncOutcomes.DryRunLifecyclePreview && r.Detail!.Contains("E1"));
    }

    private sealed class FakePaycomClient(IReadOnlyList<EmployeeRecord> employees) : IPaycomClient
    {
        public Task<IReadOnlyList<EmployeeRecord>> GetAllEmployeesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(employees);
    }

    private sealed class FakeProvisioningClient(Func<string, ScimStatus> statusFor, IReadOnlyList<ProvisioningLogEntry>? logEntries = null) : IEntraProvisioningClient
    {
        public int LogQueryCount { get; private set; }

        public Task<BulkUploadResult> SubmitBulkUploadAsync(IReadOnlyList<ScimBulkOperation> operations, CancellationToken cancellationToken = default)
        {
            var results = operations.Select(op => new ScimBulkOperationResult { BulkId = op.BulkId, Status = statusFor(op.BulkId) }).ToList();
            return Task.FromResult(new BulkUploadResult(1, results, []));
        }

        public Task<IReadOnlyList<ProvisioningLogEntry>> GetRecentProvisioningLogAsync(int top = 100, DateTimeOffset? since = null, CancellationToken cancellationToken = default)
        {
            LogQueryCount++;
            return Task.FromResult(logEntries ?? (IReadOnlyList<ProvisioningLogEntry>)[]);
        }
    }

    private sealed class FakeDirectoryClient : IEntraDirectoryClient
    {
        public bool ThrowOnLookup { get; set; }
        public bool ReconcileWasCalled { get; private set; }
        public List<string> LookedUpUpns { get; } = [];

        public Task<string?> FindUserObjectIdAsync(string userPrincipalName, CancellationToken cancellationToken = default)
        {
            LookedUpUpns.Add(userPrincipalName);
            if (ThrowOnLookup)
            {
                throw new InvalidOperationException("Simulated transient Graph failure.");
            }

            return Task.FromResult<string?>($"obj-{userPrincipalName}");
        }

        public Task<GroupReconciliationResult> ReconcileGroupMembersAsync(string groupObjectId, IReadOnlySet<string> desiredMemberObjectIds, CancellationToken cancellationToken = default)
        {
            ReconcileWasCalled = true;
            return Task.FromResult(new GroupReconciliationResult(groupObjectId, [], [], []));
        }

        public Task<IReadOnlyList<string>> GetCustomExtensionAttributeNamesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public List<string> RevokedSessionsForObjectIds { get; } = [];
        public List<string> RemovedFromGroupsForObjectIds { get; } = [];
        public List<string> DeletedObjectIds { get; } = [];

        public Task RevokeSignInSessionsAsync(string userObjectId, CancellationToken cancellationToken = default)
        {
            RevokedSessionsForObjectIds.Add(userObjectId);
            return Task.CompletedTask;
        }

        public Task<LeaverGroupCleanupResult> RemoveUserFromAllGroupsAsync(string userObjectId, CancellationToken cancellationToken = default)
        {
            RemovedFromGroupsForObjectIds.Add(userObjectId);
            return Task.FromResult(new LeaverGroupCleanupResult(["grp-1"], []));
        }

        public Task DeleteUserAsync(string userObjectId, CancellationToken cancellationToken = default)
        {
            DeletedObjectIds.Add(userObjectId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFieldMappingStore(IReadOnlyList<FieldMapping> mappings) : IFieldMappingStore
    {
        public Task<IReadOnlyList<FieldMapping>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(mappings);
        public Task<FieldMapping> UpsertAsync(FieldMapping mapping, CancellationToken cancellationToken = default) => Task.FromResult(mapping);
        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeGroupAssignmentRuleStore(IReadOnlyList<GroupAssignmentRule> rules) : IGroupAssignmentRuleStore
    {
        public Task<IReadOnlyList<GroupAssignmentRule>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(rules);
        public Task<GroupAssignmentRule> UpsertAsync(GroupAssignmentRule rule, CancellationToken cancellationToken = default) => Task.FromResult(rule);
        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSyncRunStore : ISyncRunStore
    {
        private readonly Dictionary<Guid, SyncRun> _runs = [];
        private int _nextResultId = 1;

        public Task CreateAsync(SyncRun run, CancellationToken cancellationToken = default)
        {
            _runs[run.Id] = run;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(SyncRun run, CancellationToken cancellationToken = default)
        {
            _runs[run.Id] = run;

            // Simulate DB-generated identity column assignment, which only
            // happens at persist time in the real store.
            foreach (var result in run.EmployeeResults.Where(r => r.Id == 0))
            {
                result.Id = _nextResultId++;
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SyncRun>> GetRecentAsync(int count = 25, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SyncRun>>([.. _runs.Values]);

        public Task<SyncRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_runs.GetValueOrDefault(id));

        public Task<IReadOnlyList<SyncRunEmployeeResult>> GetPendingSubmissionResultsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SyncRunEmployeeResult>>(
                [.. _runs.Values.SelectMany(r => r.EmployeeResults).Where(r => r.Outcome == SyncOutcomes.Submitted)]);

        public Task ApplyProvisioningConfirmationsAsync(IReadOnlyList<ProvisioningConfirmationUpdate> updates, CancellationToken cancellationToken = default)
        {
            var allResults = _runs.Values.SelectMany(r => r.EmployeeResults).ToDictionary(r => r.Id);
            foreach (var update in updates)
            {
                if (allResults.TryGetValue(update.EmployeeResultId, out var result))
                {
                    result.Success = update.Success;
                    result.Outcome = update.Outcome;
                    result.Detail = update.Detail;
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeEmployeeSnapshotStore(IReadOnlyDictionary<string, EmployeeSnapshot>? seed = null) : IEmployeeSnapshotStore
    {
        public Task<IReadOnlyDictionary<string, EmployeeSnapshot>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(seed ?? new Dictionary<string, EmployeeSnapshot>());

        public Task SaveAsync(IEnumerable<EmployeeSnapshot> snapshots, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeDiscoveredFieldStore : IDiscoveredFieldStore
    {
        public Task<IReadOnlyList<string>> GetKnownFieldNamesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task RecordObservedFieldsAsync(IEnumerable<string> fieldNames, DateTimeOffset observedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeLifecycleTaskStore(IReadOnlyList<LifecycleTask> tasks) : ILifecycleTaskStore
    {
        private int _nextId = 1;
        private readonly Dictionary<int, LifecycleTask> _tasks = tasks.ToDictionary(t => t.Id == 0 ? t.Id = -1 : t.Id);
        private readonly List<LifecycleTaskExecution> _executions = [];

        public Task<IReadOnlyList<LifecycleTask>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LifecycleTask>>([.. _tasks.Values]);

        public Task<LifecycleTask> UpsertAsync(LifecycleTask task, CancellationToken cancellationToken = default)
        {
            if (task.Id == 0)
            {
                task.Id = _nextId++;
            }

            _tasks[task.Id] = task;
            return Task.FromResult(task);
        }

        public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            _tasks.Remove(id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlySet<string>> GetSuccessfullyExecutedEmployeeCodesAsync(int taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(
                _executions.Where(e => e.LifecycleTaskId == taskId && e.Success).Select(e => e.EmployeeCode).ToHashSet(StringComparer.OrdinalIgnoreCase));

        public Task RecordExecutionAsync(LifecycleTaskExecution execution, CancellationToken cancellationToken = default)
        {
            _executions.Add(execution);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LifecycleTaskExecution>> GetRecentExecutionsAsync(int count = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LifecycleTaskExecution>>([.. _executions.OrderByDescending(e => e.ExecutedAt).Take(count)]);
    }
}

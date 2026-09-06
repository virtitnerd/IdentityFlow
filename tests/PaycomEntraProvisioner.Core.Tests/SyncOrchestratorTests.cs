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
        Status = EmploymentStatus.Active
    };

    private static SyncOrchestrator CreateOrchestrator(
        IReadOnlyList<EmployeeRecord> employees,
        IEntraProvisioningClient provisioningClient,
        IEntraDirectoryClient directoryClient,
        IReadOnlyList<GroupAssignmentRule>? groupRules = null,
        IReadOnlyList<FieldMapping>? mappings = null)
    {
        return new SyncOrchestrator(
            new FakePaycomClient(employees),
            provisioningClient,
            directoryClient,
            new FakeFieldMappingStore(mappings ?? []),
            new FakeGroupAssignmentRuleStore(groupRules ?? []),
            new FakeSyncRunStore(),
            new FakeEmployeeSnapshotStore(),
            new FakeDiscoveredFieldStore(),
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

    private sealed class FakePaycomClient(IReadOnlyList<EmployeeRecord> employees) : IPaycomClient
    {
        public Task<IReadOnlyList<EmployeeRecord>> GetAllEmployeesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(employees);
    }

    private sealed class FakeProvisioningClient(Func<string, ScimStatus> statusFor) : IEntraProvisioningClient
    {
        public Task<BulkUploadResult> SubmitBulkUploadAsync(IReadOnlyList<ScimBulkOperation> operations, CancellationToken cancellationToken = default)
        {
            var results = operations.Select(op => new ScimBulkOperationResult { BulkId = op.BulkId, Status = statusFor(op.BulkId) }).ToList();
            return Task.FromResult(new BulkUploadResult(1, results, []));
        }

        public Task<IReadOnlyList<ProvisioningLogEntry>> GetRecentProvisioningLogAsync(int top = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProvisioningLogEntry>>([]);
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

        public Task CreateAsync(SyncRun run, CancellationToken cancellationToken = default)
        {
            _runs[run.Id] = run;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(SyncRun run, CancellationToken cancellationToken = default)
        {
            _runs[run.Id] = run;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SyncRun>> GetRecentAsync(int count = 25, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SyncRun>>([.. _runs.Values]);

        public Task<SyncRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_runs.GetValueOrDefault(id));
    }

    private sealed class FakeEmployeeSnapshotStore : IEmployeeSnapshotStore
    {
        public Task<IReadOnlyDictionary<string, EmployeeSnapshot>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, EmployeeSnapshot>>(new Dictionary<string, EmployeeSnapshot>());

        public Task SaveAsync(IEnumerable<EmployeeSnapshot> snapshots, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeDiscoveredFieldStore : IDiscoveredFieldStore
    {
        public Task<IReadOnlyList<string>> GetKnownFieldNamesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task RecordObservedFieldsAsync(IEnumerable<string> fieldNames, DateTimeOffset observedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

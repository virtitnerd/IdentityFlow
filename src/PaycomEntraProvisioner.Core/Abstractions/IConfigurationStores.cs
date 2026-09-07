using PaycomEntraProvisioner.Core.Configuration;
using PaycomEntraProvisioner.Core.Domain;

namespace PaycomEntraProvisioner.Core.Abstractions;

public interface IFieldMappingStore
{
    Task<IReadOnlyList<FieldMapping>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<FieldMapping> UpsertAsync(FieldMapping mapping, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public interface IGroupAssignmentRuleStore
{
    Task<IReadOnlyList<GroupAssignmentRule>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<GroupAssignmentRule> UpsertAsync(GroupAssignmentRule rule, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public interface ISyncRunStore
{
    Task CreateAsync(SyncRun run, CancellationToken cancellationToken = default);
    Task UpdateAsync(SyncRun run, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SyncRun>> GetRecentAsync(int count = 25, CancellationToken cancellationToken = default);
    Task<SyncRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every <see cref="SyncRunEmployeeResult"/> still awaiting confirmation
    /// from the Entra provisioning audit log (Outcome "Submitted") across
    /// any run, oldest first. Empty in the steady state once confirmations
    /// catch up - callers should skip the provisioning-log call entirely
    /// when this comes back empty rather than querying Graph for nothing.
    /// </summary>
    Task<IReadOnlyList<SyncRunEmployeeResult>> GetPendingSubmissionResultsAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies confirmed outcomes from the provisioning log to previously-submitted results in one batch.</summary>
    Task ApplyProvisioningConfirmationsAsync(IReadOnlyList<ProvisioningConfirmationUpdate> updates, CancellationToken cancellationToken = default);
}

public sealed record ProvisioningConfirmationUpdate(int EmployeeResultId, bool Success, string Outcome, string? Detail);

public interface IEmployeeSnapshotStore
{
    Task<IReadOnlyDictionary<string, EmployeeSnapshot>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IEnumerable<EmployeeSnapshot> snapshots, CancellationToken cancellationToken = default);
}

/// <summary>
/// Tracks every Paycom field name actually observed across sync runs, so
/// the admin UI can suggest real source field names for
/// <see cref="FieldMapping.SourceField"/> instead of a static guess.
/// </summary>
public interface IDiscoveredFieldStore
{
    Task<IReadOnlyList<string>> GetKnownFieldNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>Upserts each field name's first/last-seen timestamp for this run.</summary>
    Task RecordObservedFieldsAsync(IEnumerable<string> fieldNames, DateTimeOffset observedAt, CancellationToken cancellationToken = default);
}

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
}

public interface IEmployeeSnapshotStore
{
    Task<IReadOnlyDictionary<string, EmployeeSnapshot>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IEnumerable<EmployeeSnapshot> snapshots, CancellationToken cancellationToken = default);
}

using Microsoft.EntityFrameworkCore;
using PaycomEntraProvisioner.Core.Abstractions;
using PaycomEntraProvisioner.Core.Domain;

namespace PaycomEntraProvisioner.Data.Repositories;

/// <summary>
/// Stores the "last seen" state of every employee. <see cref="SaveAsync"/>
/// performs a full replace so the table always reflects exactly who was in
/// the most recent Paycom pull - this is what lets the orchestrator detect
/// a worker vanishing from the feed entirely.
/// </summary>
public sealed class EmployeeSnapshotStore(ProvisionerDbContext db) : IEmployeeSnapshotStore
{
    public async Task<IReadOnlyDictionary<string, EmployeeSnapshot>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var all = await db.EmployeeSnapshots.AsNoTracking().ToListAsync(cancellationToken);
        return all.ToDictionary(x => x.EmployeeCode, StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveAsync(IEnumerable<EmployeeSnapshot> snapshots, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.EmployeeSnapshots.ExecuteDeleteAsync(cancellationToken);
        db.EmployeeSnapshots.AddRange(snapshots);
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}

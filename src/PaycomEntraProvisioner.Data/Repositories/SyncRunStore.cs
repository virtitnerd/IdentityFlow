using Microsoft.EntityFrameworkCore;
using PaycomEntraProvisioner.Core.Abstractions;
using PaycomEntraProvisioner.Core.Domain;

namespace PaycomEntraProvisioner.Data.Repositories;

public sealed class SyncRunStore(ProvisionerDbContext db) : ISyncRunStore
{
    public async Task CreateAsync(SyncRun run, CancellationToken cancellationToken = default)
    {
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(SyncRun run, CancellationToken cancellationToken = default)
    {
        var existing = await db.SyncRuns
            .Include(x => x.EmployeeResults)
            .FirstOrDefaultAsync(x => x.Id == run.Id, cancellationToken);

        if (existing is null)
        {
            db.SyncRuns.Add(run);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(run);
            existing.EmployeeResults.AddRange(run.EmployeeResults.Where(r => r.Id == 0));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SyncRun>> GetRecentAsync(int count = 25, CancellationToken cancellationToken = default) =>
        await db.SyncRuns.AsNoTracking()
            .OrderByDescending(x => x.StartedAt)
            .Take(count)
            .ToListAsync(cancellationToken);

    public async Task<SyncRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.SyncRuns.AsNoTracking()
            .Include(x => x.EmployeeResults)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
}

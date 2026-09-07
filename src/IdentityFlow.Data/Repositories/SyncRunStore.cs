using Microsoft.EntityFrameworkCore;
using IdentityFlow.Core.Abstractions;
using IdentityFlow.Core.Domain;

namespace IdentityFlow.Data.Repositories;

public sealed class SyncRunStore(ProvisionerDbContext db) : ISyncRunStore
{
    public async Task CreateAsync(SyncRun run, CancellationToken cancellationToken = default)
    {
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(SyncRun run, CancellationToken cancellationToken = default)
    {
        // The normal path: SyncOrchestrator holds one `run` instance for the
        // whole pipeline, so by the time UpdateAsync runs, this same
        // DbContext is already tracking it (CreateAsync added it earlier in
        // the same scope) and every EmployeeResults.Add(...) call along the
        // way is already reflected on the tracked instance. Re-querying and
        // merging in that case is not just redundant - `existing` would be
        // the SAME object as `run` (EF's identity resolution), making
        // `existing.EmployeeResults.AddRange(run.EmployeeResults.Where(...))`
        // mutate a list while enumerating a filtered view of itself, which
        // throws InvalidOperationException on virtually every real run.
        if (db.ChangeTracker.Entries<SyncRun>().Any(e => e.Entity.Id == run.Id))
        {
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Fallback for a genuinely different DbContext/process updating a
        // run it didn't create (not the normal path, but kept for safety).
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

    public async Task<IReadOnlyList<SyncRunEmployeeResult>> GetPendingSubmissionResultsAsync(CancellationToken cancellationToken = default) =>
        await db.SyncRunEmployeeResults.AsNoTracking()
            .Where(r => r.Outcome == SyncOutcomes.Submitted)
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

    public async Task ApplyProvisioningConfirmationsAsync(IReadOnlyList<ProvisioningConfirmationUpdate> updates, CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0)
        {
            return;
        }

        var ids = updates.Select(u => u.EmployeeResultId).ToList();
        var tracked = await db.SyncRunEmployeeResults
            .Where(r => ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        foreach (var update in updates)
        {
            if (!tracked.TryGetValue(update.EmployeeResultId, out var entity))
            {
                continue;
            }

            entity.Success = update.Success;
            entity.Outcome = update.Outcome;
            entity.Detail = update.Detail;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

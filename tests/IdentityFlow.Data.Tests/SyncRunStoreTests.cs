using Microsoft.EntityFrameworkCore;
using IdentityFlow.Core.Domain;
using IdentityFlow.Data.Repositories;
using Xunit;

namespace IdentityFlow.Data.Tests;

public class SyncRunStoreTests
{
    private static ProvisionerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ProvisionerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task UpdateAsync_WithResultsAddedAfterCreate_DoesNotThrow()
    {
        // Regression test: SyncOrchestrator creates a SyncRun, appends
        // SyncRunEmployeeResult entries to it in memory throughout the run,
        // then calls UpdateAsync on the SAME instance from the SAME scoped
        // DbContext - the exact scenario that used to throw
        // "Collection was modified; enumeration operation may not execute."
        await using var db = CreateContext();
        var store = new SyncRunStore(db);

        var run = new SyncRun { StartedAt = DateTimeOffset.UtcNow, Trigger = SyncTrigger.Manual };
        await store.CreateAsync(run);

        run.EmployeeResults.Add(new SyncRunEmployeeResult { SyncRunId = run.Id, EmployeeCode = "E1", Outcome = "Queued", Success = true });
        run.EmployeeResults.Add(new SyncRunEmployeeResult { SyncRunId = run.Id, EmployeeCode = "E2", Outcome = "Queued", Success = true });
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.Status = SyncRunStatus.Succeeded;

        await store.UpdateAsync(run);

        var persisted = await store.GetByIdAsync(run.Id);
        Assert.NotNull(persisted);
        Assert.Equal(2, persisted!.EmployeeResults.Count);
        Assert.Equal(SyncRunStatus.Succeeded, persisted.Status);
    }

    [Fact]
    public async Task UpdateAsync_CalledTwice_DoesNotDuplicateEmployeeResults()
    {
        await using var db = CreateContext();
        var store = new SyncRunStore(db);

        var run = new SyncRun { StartedAt = DateTimeOffset.UtcNow, Trigger = SyncTrigger.Timer };
        await store.CreateAsync(run);

        run.EmployeeResults.Add(new SyncRunEmployeeResult { SyncRunId = run.Id, EmployeeCode = "E1", Outcome = "Queued", Success = true });
        await store.UpdateAsync(run);
        await store.UpdateAsync(run);

        var persisted = await store.GetByIdAsync(run.Id);
        Assert.Single(persisted!.EmployeeResults);
    }

    [Fact]
    public async Task UpdateAsync_OnDetachedRunFromDifferentContext_MergesEmployeeResults()
    {
        // The fallback path: a run created and updated from two genuinely
        // different DbContext instances (not the normal SyncOrchestrator
        // flow, but should still work rather than silently no-op).
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<ProvisionerDbContext>().UseInMemoryDatabase(dbName).Options;

        Guid runId;
        await using (var db1 = new ProvisionerDbContext(options))
        {
            var store1 = new SyncRunStore(db1);
            var run = new SyncRun { StartedAt = DateTimeOffset.UtcNow, Trigger = SyncTrigger.Api };
            await store1.CreateAsync(run);
            runId = run.Id;
        }

        await using var db2 = new ProvisionerDbContext(options);
        var store2 = new SyncRunStore(db2);
        var detachedRun = new SyncRun
        {
            Id = runId,
            StartedAt = DateTimeOffset.UtcNow,
            Trigger = SyncTrigger.Api,
            Status = SyncRunStatus.Succeeded
        };
        detachedRun.EmployeeResults.Add(new SyncRunEmployeeResult { SyncRunId = runId, EmployeeCode = "E1", Outcome = "Queued", Success = true });

        await store2.UpdateAsync(detachedRun);

        var persisted = await store2.GetByIdAsync(runId);
        Assert.Single(persisted!.EmployeeResults);
        Assert.Equal(SyncRunStatus.Succeeded, persisted.Status);
    }
}

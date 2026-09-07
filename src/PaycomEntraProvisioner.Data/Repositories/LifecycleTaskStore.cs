using Microsoft.EntityFrameworkCore;
using PaycomEntraProvisioner.Core.Abstractions;
using PaycomEntraProvisioner.Core.Configuration;
using PaycomEntraProvisioner.Core.Domain;

namespace PaycomEntraProvisioner.Data.Repositories;

public sealed class LifecycleTaskStore(ProvisionerDbContext db) : ILifecycleTaskStore
{
    public async Task<IReadOnlyList<LifecycleTask>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.LifecycleTasks.AsNoTracking()
            .OrderBy(x => x.Trigger).ThenBy(x => x.TaskType)
            .ToListAsync(cancellationToken);

    public async Task<LifecycleTask> UpsertAsync(LifecycleTask task, CancellationToken cancellationToken = default)
    {
        db.LifecycleTasks.Upsert(task, task.Id);
        await db.SaveChangesAsync(cancellationToken);
        return task;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await db.LifecycleTasks.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<string>> GetSuccessfullyExecutedEmployeeCodesAsync(int taskId, CancellationToken cancellationToken = default)
    {
        var codes = await db.LifecycleTaskExecutions.AsNoTracking()
            .Where(x => x.LifecycleTaskId == taskId && x.Success)
            .Select(x => x.EmployeeCode)
            .ToListAsync(cancellationToken);

        return codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task RecordExecutionAsync(LifecycleTaskExecution execution, CancellationToken cancellationToken = default)
    {
        db.LifecycleTaskExecutions.Add(execution);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LifecycleTaskExecution>> GetRecentExecutionsAsync(int count = 100, CancellationToken cancellationToken = default) =>
        await db.LifecycleTaskExecutions.AsNoTracking()
            .OrderByDescending(x => x.ExecutedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
}

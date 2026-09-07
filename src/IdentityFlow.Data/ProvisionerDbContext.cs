using Microsoft.EntityFrameworkCore;
using IdentityFlow.Core.Configuration;
using IdentityFlow.Core.Domain;

namespace IdentityFlow.Data;

public sealed class ProvisionerDbContext(DbContextOptions<ProvisionerDbContext> options) : DbContext(options)
{
    public DbSet<FieldMapping> FieldMappings => Set<FieldMapping>();
    public DbSet<GroupAssignmentRule> GroupAssignmentRules => Set<GroupAssignmentRule>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<SyncRunEmployeeResult> SyncRunEmployeeResults => Set<SyncRunEmployeeResult>();
    public DbSet<EmployeeSnapshot> EmployeeSnapshots => Set<EmployeeSnapshot>();
    public DbSet<DiscoveredSourceField> DiscoveredSourceFields => Set<DiscoveredSourceField>();
    public DbSet<LifecycleTask> LifecycleTasks => Set<LifecycleTask>();
    public DbSet<LifecycleTaskExecution> LifecycleTaskExecutions => Set<LifecycleTaskExecution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProvisionerDbContext).Assembly);
    }
}

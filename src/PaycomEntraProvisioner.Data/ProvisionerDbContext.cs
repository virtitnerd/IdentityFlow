using Microsoft.EntityFrameworkCore;
using PaycomEntraProvisioner.Core.Configuration;
using PaycomEntraProvisioner.Core.Domain;

namespace PaycomEntraProvisioner.Data;

public sealed class ProvisionerDbContext(DbContextOptions<ProvisionerDbContext> options) : DbContext(options)
{
    public DbSet<FieldMapping> FieldMappings => Set<FieldMapping>();
    public DbSet<GroupAssignmentRule> GroupAssignmentRules => Set<GroupAssignmentRule>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<SyncRunEmployeeResult> SyncRunEmployeeResults => Set<SyncRunEmployeeResult>();
    public DbSet<EmployeeSnapshot> EmployeeSnapshots => Set<EmployeeSnapshot>();
    public DbSet<DiscoveredSourceField> DiscoveredSourceFields => Set<DiscoveredSourceField>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProvisionerDbContext).Assembly);
    }
}

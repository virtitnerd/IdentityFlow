using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaycomEntraProvisioner.Core.Abstractions;
using PaycomEntraProvisioner.Data.Repositories;

namespace PaycomEntraProvisioner.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProvisionerData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetSection("Sql")["ConnectionString"]
            ?? throw new InvalidOperationException("Missing configuration value 'Sql:ConnectionString'.");

        services.AddDbContext<ProvisionerDbContext>(options => options.UseSqlServer(connectionString));

        services.AddScoped<IFieldMappingStore, FieldMappingStore>();
        services.AddScoped<IGroupAssignmentRuleStore, GroupAssignmentRuleStore>();
        services.AddScoped<ISyncRunStore, SyncRunStore>();
        services.AddScoped<IEmployeeSnapshotStore, EmployeeSnapshotStore>();
        services.AddScoped<IDiscoveredFieldStore, DiscoveredFieldStore>();
        services.AddScoped<ILifecycleTaskStore, LifecycleTaskStore>();

        return services;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PaycomEntraProvisioner.Data;

/// <summary>Lets `dotnet ef migrations add` construct the context without a running host. Not used at runtime.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ProvisionerDbContext>
{
    public ProvisionerDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<ProvisionerDbContext>();
        builder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=PaycomEntraProvisioner;Trusted_Connection=True;");
        return new ProvisionerDbContext(builder.Options);
    }
}

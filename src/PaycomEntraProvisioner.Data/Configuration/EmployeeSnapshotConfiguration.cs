using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaycomEntraProvisioner.Core.Domain;

namespace PaycomEntraProvisioner.Data.Configuration;

public sealed class EmployeeSnapshotConfiguration : IEntityTypeConfiguration<EmployeeSnapshot>
{
    public void Configure(EntityTypeBuilder<EmployeeSnapshot> builder)
    {
        builder.ToTable("EmployeeSnapshots");
        builder.HasKey(x => x.EmployeeCode);
        builder.Property(x => x.EmployeeCode).HasMaxLength(100);
        builder.Property(x => x.WorkEmail).HasMaxLength(320);
        builder.Property(x => x.ContentHash).IsRequired().HasMaxLength(64);
        builder.Property(x => x.EntraObjectId).HasMaxLength(64);
    }
}

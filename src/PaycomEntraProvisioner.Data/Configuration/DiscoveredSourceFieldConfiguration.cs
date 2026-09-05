using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaycomEntraProvisioner.Core.Domain;

namespace PaycomEntraProvisioner.Data.Configuration;

public sealed class DiscoveredSourceFieldConfiguration : IEntityTypeConfiguration<DiscoveredSourceField>
{
    public void Configure(EntityTypeBuilder<DiscoveredSourceField> builder)
    {
        builder.ToTable("DiscoveredSourceFields");
        builder.HasKey(x => x.FieldName);
        builder.Property(x => x.FieldName).HasMaxLength(200);
    }
}

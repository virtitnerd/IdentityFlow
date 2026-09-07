using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IdentityFlow.Core.Domain;

namespace IdentityFlow.Data.Configuration;

public sealed class DiscoveredSourceFieldConfiguration : IEntityTypeConfiguration<DiscoveredSourceField>
{
    public void Configure(EntityTypeBuilder<DiscoveredSourceField> builder)
    {
        builder.ToTable("DiscoveredSourceFields");
        builder.HasKey(x => x.FieldName);
        builder.Property(x => x.FieldName).HasMaxLength(200);
    }
}

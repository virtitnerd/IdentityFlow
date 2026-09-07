using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IdentityFlow.Core.Configuration;

namespace IdentityFlow.Data.Configuration;

public sealed class FieldMappingConfiguration : IEntityTypeConfiguration<FieldMapping>
{
    public void Configure(EntityTypeBuilder<FieldMapping> builder)
    {
        builder.ToTable("FieldMappings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceField).IsRequired().HasMaxLength(200);
        builder.Property(x => x.TargetAttribute).IsRequired().HasMaxLength(200);
        builder.Property(x => x.TransformExpression).HasMaxLength(2000);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.HasIndex(x => new { x.SourceField, x.TargetAttribute }).IsUnique();
    }
}

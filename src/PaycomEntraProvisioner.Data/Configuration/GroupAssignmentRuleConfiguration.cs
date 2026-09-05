using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaycomEntraProvisioner.Core.Configuration;

namespace PaycomEntraProvisioner.Data.Configuration;

public sealed class GroupAssignmentRuleConfiguration : IEntityTypeConfiguration<GroupAssignmentRule>
{
    public void Configure(EntityTypeBuilder<GroupAssignmentRule> builder)
    {
        builder.ToTable("GroupAssignmentRules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Condition).IsRequired().HasMaxLength(2000);
        builder.Property(x => x.TargetGroupObjectId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.TargetGroupDisplayName).HasMaxLength(256);
        builder.Property(x => x.Notes).HasMaxLength(1000);
    }
}

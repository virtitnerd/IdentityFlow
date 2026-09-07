using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaycomEntraProvisioner.Core.Configuration;

namespace PaycomEntraProvisioner.Data.Configuration;

public sealed class LifecycleTaskConfiguration : IEntityTypeConfiguration<LifecycleTask>
{
    public void Configure(EntityTypeBuilder<LifecycleTask> builder)
    {
        builder.ToTable("LifecycleTasks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Trigger).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.TaskType).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.HasIndex(x => new { x.Trigger, x.TaskType }).IsUnique();
    }
}

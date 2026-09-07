using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IdentityFlow.Core.Domain;

namespace IdentityFlow.Data.Configuration;

public sealed class LifecycleTaskExecutionConfiguration : IEntityTypeConfiguration<LifecycleTaskExecution>
{
    public void Configure(EntityTypeBuilder<LifecycleTaskExecution> builder)
    {
        builder.ToTable("LifecycleTaskExecutions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EmployeeCode).IsRequired().HasMaxLength(100);
        builder.Property(x => x.TaskType).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.Detail).HasMaxLength(2000);

        // The idempotency check queries by (LifecycleTaskId, EmployeeCode,
        // Success) - index it directly rather than scanning.
        builder.HasIndex(x => new { x.LifecycleTaskId, x.EmployeeCode });
    }
}

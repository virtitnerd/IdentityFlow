using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaycomEntraProvisioner.Core.Domain;

namespace PaycomEntraProvisioner.Data.Configuration;

public sealed class SyncRunConfiguration : IEntityTypeConfiguration<SyncRun>
{
    public void Configure(EntityTypeBuilder<SyncRun> builder)
    {
        builder.ToTable("SyncRuns");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TriggeredByUser).HasMaxLength(256);
        builder.Property(x => x.ErrorSummary).HasMaxLength(4000);
        builder.HasIndex(x => x.StartedAt);

        builder.HasMany(x => x.EmployeeResults)
            .WithOne()
            .HasForeignKey(x => x.SyncRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SyncRunEmployeeResultConfiguration : IEntityTypeConfiguration<SyncRunEmployeeResult>
{
    public void Configure(EntityTypeBuilder<SyncRunEmployeeResult> builder)
    {
        builder.ToTable("SyncRunEmployeeResults");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.EmployeeCode).IsRequired().HasMaxLength(100);
        builder.Property(x => x.DisplayName).HasMaxLength(256);
        builder.Property(x => x.Outcome).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Detail).HasMaxLength(4000);
        builder.HasIndex(x => x.SyncRunId);
    }
}

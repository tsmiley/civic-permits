using CivicPermits.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicPermits.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.Action).HasMaxLength(AuditEntry.MaximumCodeLength).IsRequired();
        builder.Property(entry => entry.SubjectType).HasMaxLength(AuditEntry.MaximumCodeLength).IsRequired();
        builder.Property(entry => entry.Summary).HasMaxLength(AuditEntry.MaximumSummaryLength).IsRequired();

        // Read newest first; the identifier breaks ties so a page never repeats or skips an entry.
        builder.HasIndex(entry => new { entry.OccurredOn, entry.Id });
    }
}

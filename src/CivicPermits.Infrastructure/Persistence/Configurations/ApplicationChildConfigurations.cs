using CivicPermits.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicPermits.Infrastructure.Persistence.Configurations;

internal sealed class StatusChangeConfiguration : IEntityTypeConfiguration<StatusChange>
{
    public void Configure(EntityTypeBuilder<StatusChange> builder)
    {
        builder.ToTable("StatusChanges");

        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).ValueGeneratedNever();

        builder.Property(change => change.From).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(change => change.To).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(change => change.Action).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(change => change.ActorRole).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(change => change.Note).HasMaxLength(1_000);

        builder.HasIndex(PermitApplicationConfiguration.ApplicationKey, nameof(StatusChange.At));
    }
}

internal sealed class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    public void Configure(EntityTypeBuilder<Inspection> builder)
    {
        builder.ToTable("Inspections");

        builder.HasKey(inspection => inspection.Id);
        builder.Property(inspection => inspection.Id).ValueGeneratedNever();

        builder.Property(inspection => inspection.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(inspection => inspection.Notes).HasMaxLength(1_000);

        // An inspector's day: "what am I booked for".
        builder.HasIndex(inspection => new { inspection.InspectorId, inspection.ScheduledFor });
    }
}

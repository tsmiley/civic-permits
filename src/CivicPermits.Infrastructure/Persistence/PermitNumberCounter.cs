using CivicPermits.Domain.Permits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicPermits.Infrastructure.Persistence;

/// <summary>The last permit number handed out for a year. A persistence detail: the domain never sees it.</summary>
internal sealed class PermitNumberCounter
{
    public int Year { get; init; }

    public int LastSequence { get; set; }
}

internal sealed class PermitNumberCounterConfiguration : IEntityTypeConfiguration<PermitNumberCounter>
{
    public void Configure(EntityTypeBuilder<PermitNumberCounter> builder)
    {
        builder.ToTable("PermitNumberCounters");

        builder.HasKey(counter => counter.Year);
        builder.Property(counter => counter.Year).ValueGeneratedNever();

        // The sequence doubles as the concurrency token: the update says "set it to N+1 where it is still N", so of
        // two requests that read N only one can write, and the other is told to try again.
        builder.Property(counter => counter.LastSequence).IsConcurrencyToken();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_PermitNumberCounters_LastSequence",
            $"[LastSequence] >= 0 AND [LastSequence] <= {PermitNumber.MaximumSequence}"));
    }
}

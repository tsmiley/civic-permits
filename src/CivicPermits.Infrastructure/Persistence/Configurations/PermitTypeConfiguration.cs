using CivicPermits.Domain.Permits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicPermits.Infrastructure.Persistence.Configurations;

internal sealed class PermitTypeConfiguration : IEntityTypeConfiguration<PermitType>
{
    public void Configure(EntityTypeBuilder<PermitType> builder)
    {
        builder.ToTable("PermitTypes");

        builder.HasKey(type => type.Id);
        builder.Property(type => type.Id).ValueGeneratedNever();

        builder.Property(type => type.Category).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(type => type.Name).HasMaxLength(80).IsRequired();
        builder.Property(type => type.Description).HasMaxLength(500).IsRequired();

        builder.HasIndex(type => type.Name).IsUnique();
    }
}

using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Permits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicPermits.Infrastructure.Persistence.Configurations;

internal sealed class PermitApplicationConfiguration : IEntityTypeConfiguration<PermitApplication>
{
    /// <summary>The foreign key the two child tables use. A shadow property: the domain does not carry it.</summary>
    internal const string ApplicationKey = "PermitApplicationId";

    public void Configure(EntityTypeBuilder<PermitApplication> builder)
    {
        builder.ToTable("PermitApplications");

        builder.HasKey(application => application.Id);
        builder.Property(application => application.Id).ValueGeneratedNever();

        builder.Property(application => application.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(application => application.ProjectDescription).HasMaxLength(2_000).IsRequired();
        builder.Property(application => application.DecisionNote).HasMaxLength(1_000);

        // The printed number is unique once it exists. Drafts have none, and any number of them may share NULL.
        builder.Property(application => application.Number).HasMaxLength(14);
        builder.HasIndex(application => application.Number).IsUnique();

        builder.ComplexProperty(
            application => application.SiteAddress,
            address =>
            {
                address.Property(part => part.Line1).HasMaxLength(120).IsRequired();
                address.Property(part => part.Line2).HasMaxLength(60);
                address.Property(part => part.City).HasMaxLength(80).IsRequired();
                address.Property(part => part.Region).HasMaxLength(2).IsRequired();
                address.Property(part => part.PostalCode).HasMaxLength(5).IsRequired();
            });

        // An application must refer to a real permit type, and a type that has applications can never be deleted.
        builder.HasOne<PermitType>()
            .WithMany()
            .HasForeignKey(application => application.PermitTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // The applicant is an Identity user, held by id only. The domain does not know about Identity, and the
        // sample data names no real person, so there is deliberately no foreign key to the user table.
        builder.HasIndex(application => application.ApplicantId);
        builder.HasIndex(application => new { application.Status, application.SubmittedOn });

        builder.HasMany(application => application.History)
            .WithOne()
            .HasForeignKey(ApplicationKey)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(application => application.History)
            .HasField("_history")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(application => application.Inspections)
            .WithOne()
            .HasForeignKey(ApplicationKey)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(application => application.Inspections)
            .HasField("_inspections")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

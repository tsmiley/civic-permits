using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;
using CivicPermits.Domain.Permits;
using CivicPermits.Infrastructure.Persistence.Configurations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CivicPermits.Infrastructure.Persistence;

/// <summary>
/// The one database context: the Identity tables and the permit model. Entity configurations are discovered
/// from this assembly, so adding an entity never means editing this class.
/// </summary>
public class CivicPermitsDbContext(DbContextOptions<CivicPermitsDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    /// <summary>The name of the concurrency token every application carries.</summary>
    internal const string VersionProperty = "Version";

    /// <summary>The kinds of permit the office offers.</summary>
    public DbSet<PermitType> PermitTypes => Set<PermitType>();

    /// <summary>The audit record. Only ever added to.</summary>
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <summary>Every application, with its history and inspections.</summary>
    public DbSet<PermitApplication> PermitApplications => Set<PermitApplication>();

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RaiseApplicationVersions();

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RaiseApplicationVersions();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        // Applied to every property of these types, so no entity can forget one. See Converters.cs.
        configurationBuilder.Properties<Money>().HaveConversion<MoneyConverter>();
        configurationBuilder.Properties<PermitNumber>().HaveConversion<PermitNumberConverter>();
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(CivicPermitsDbContext).Assembly);
    }

    /// <summary>
    /// Raises the version of every application that is about to be updated, including one whose only change is a
    /// new history entry or inspection, which would otherwise leave the application row untouched and unguarded.
    /// </summary>
    private void RaiseApplicationVersions()
    {
        ChangeTracker.DetectChanges();

        HashSet<Guid> changedIds = [];

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            switch (entry.Entity)
            {
                case PermitApplication application when entry.State == EntityState.Modified:
                    changedIds.Add(application.Id);
                    break;
                case StatusChange or Inspection when entry.Metadata.FindProperty(PermitApplicationConfiguration.ApplicationKey) is not null:
                    changedIds.Add((Guid)entry.Property(PermitApplicationConfiguration.ApplicationKey).CurrentValue!);
                    break;
            }
        }

        foreach (var entry in ChangeTracker.Entries<PermitApplication>())
        {
            if (entry.State is EntityState.Modified or EntityState.Unchanged && changedIds.Contains(entry.Entity.Id))
            {
                var version = entry.Property<long>(VersionProperty);
                version.CurrentValue = version.OriginalValue + 1;
            }
        }
    }
}

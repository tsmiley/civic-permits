using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;
using CivicPermits.Domain.Permits;
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
    /// <summary>The kinds of permit the office offers.</summary>
    public DbSet<PermitType> PermitTypes => Set<PermitType>();

    /// <summary>Every application, with its history and inspections.</summary>
    public DbSet<PermitApplication> PermitApplications => Set<PermitApplication>();

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
}

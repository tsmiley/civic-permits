using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CivicPermits.Infrastructure.Persistence;

/// <summary>
/// The one database context. It holds the Identity tables today; the permit model arrives with the
/// domain in the next slice. Entity configurations are discovered from this assembly, so adding an
/// entity never means editing this class.
/// </summary>
public class CivicPermitsDbContext(DbContextOptions<CivicPermitsDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(CivicPermitsDbContext).Assembly);
    }
}

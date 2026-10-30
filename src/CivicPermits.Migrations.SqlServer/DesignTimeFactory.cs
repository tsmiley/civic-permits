using CivicPermits.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CivicPermits.Migrations.SqlServer;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without starting a web host. The connection string points at LocalDB
/// and is never opened while scaffolding a migration.
/// </summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<CivicPermitsDbContext>
{
    public CivicPermitsDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<CivicPermitsDbContext> options = new();
        options.UseSqlServer(
            "Server=(localdb)\\MSSQLLocalDB;Database=CivicPermitsDesignTime;Trusted_Connection=True;",
            sql => sql.MigrationsAssembly(MigrationAssemblies.SqlServer));

        return new CivicPermitsDbContext(options.Options);
    }
}

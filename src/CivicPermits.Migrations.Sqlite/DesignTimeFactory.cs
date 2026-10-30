using CivicPermits.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CivicPermits.Migrations.Sqlite;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without starting a web host. The connection string is a throwaway
/// file name: scaffolding a migration never opens the database.
/// </summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<CivicPermitsDbContext>
{
    public CivicPermitsDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<CivicPermitsDbContext> options = new();
        options.UseSqlite("Data Source=design-time.db", sqlite => sqlite.MigrationsAssembly(MigrationAssemblies.Sqlite));

        return new CivicPermitsDbContext(options.Options);
    }
}

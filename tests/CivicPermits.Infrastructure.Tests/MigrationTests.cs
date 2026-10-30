using CivicPermits.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicPermits.Infrastructure.Tests;

public class MigrationTests
{
    [Fact]
    public async Task The_sqlite_migrations_build_a_database_that_matches_the_model()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using CivicPermitsDbContext context = database.CreateContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Single(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.False(context.Database.HasPendingModelChanges(), "The model changed but no migration was added for SQLite.");
    }

    [Fact]
    public void The_sql_server_migrations_match_the_model_without_needing_a_server()
    {
        DbContextOptions<CivicPermitsDbContext> options = new DbContextOptionsBuilder<CivicPermitsDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\MSSQLLocalDB;Database=NeverOpened;Trusted_Connection=True;",
                sql => sql.MigrationsAssembly(MigrationAssemblies.SqlServer))
            .Options;

        using CivicPermitsDbContext context = new(options);

        Assert.False(context.Database.HasPendingModelChanges(), "The model changed but no migration was added for SQL Server.");
    }

    [Fact]
    public async Task Every_table_the_domain_needs_exists_after_migration()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using CivicPermitsDbContext context = database.CreateContext();

        Assert.Equal(0, await context.PermitTypes.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.PermitApplications.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.Users.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.Roles.CountAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite, MigrationAssemblies.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer, MigrationAssemblies.SqlServer)]
    public void Each_provider_has_its_own_migration_assembly(DatabaseProvider provider, string expected)
    {
        Assert.Equal(expected, MigrationAssemblies.For(provider));
    }
}

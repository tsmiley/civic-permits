using CivicPermits.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CivicPermits.Infrastructure.Tests;

/// <summary>A throwaway SQLite file, migrated with the real migrations, deleted when the test is done.</summary>
internal sealed class SqliteTestDatabase : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"civic-permits-test-{Guid.NewGuid():N}.db");

    /// <summary>The database file.</summary>
    public string FilePath => _path;

    /// <summary>A fixed afternoon, so sample data always lands on the same dates.</summary>
    public static readonly DateTimeOffset SeedTime = new(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);

    public FakeTimeProvider Time { get; } = new(SeedTime);

    /// <summary>A new context on the same file. Each call is a fresh unit of work, as in a request.</summary>
    public CivicPermitsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CivicPermitsDbContext>()
            .UseSqlite($"Data Source={_path}", sqlite => sqlite.MigrationsAssembly(MigrationAssemblies.Sqlite))
            .Options);

    /// <summary>Creates the database by running every migration.</summary>
    public static async Task<SqliteTestDatabase> MigratedAsync()
    {
        SqliteTestDatabase database = new();

        await using CivicPermitsDbContext context = database.CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        return database;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (string suffix in new[] { string.Empty, "-shm", "-wal" })
        {
            try
            {
                File.Delete(_path + suffix);
            }
            catch (IOException)
            {
                // Left for the temp folder to clean up.
            }
        }
    }
}

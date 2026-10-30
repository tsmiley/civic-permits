namespace CivicPermits.Infrastructure.Persistence;

/// <summary>
/// Where the data lives. Bound from the <c>Database</c> section and validated when the host starts, so a
/// missing or malformed value stops the process with a clear message instead of failing on the first request.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Database";

    /// <summary>Which engine to use. Defaults to <see cref="DatabaseProvider.Sqlite"/>.</summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>
    /// The connection string. It carries no secret in this project: SQLite is a file path and LocalDB uses
    /// the signed-in Windows account. A deployment that needs a password supplies it by environment variable.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Whether to apply any pending migrations when the host starts. Convenient for a demonstration and for
    /// development; a real deployment applies migrations as a deliberate step before the new version runs.
    /// </summary>
    public bool MigrateOnStartup { get; set; }

    /// <summary>
    /// Whether to add the fictional town's sample data when the host starts. Needs <see cref="MigrateOnStartup"/>,
    /// and is refused in the Production environment.
    /// </summary>
    public bool SeedSampleData { get; set; }
}

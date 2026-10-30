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
}

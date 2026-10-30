namespace CivicPermits.Infrastructure.Persistence;

/// <summary>The database engines the application can run on. The model is the same for both.</summary>
public enum DatabaseProvider
{
    /// <summary>A file database: the zero-setup default for development and most tests.</summary>
    Sqlite = 0,

    /// <summary>SQL Server, including the free LocalDB edition that ships with Visual Studio.</summary>
    SqlServer = 1,
}

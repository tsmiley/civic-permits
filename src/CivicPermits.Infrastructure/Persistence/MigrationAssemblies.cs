namespace CivicPermits.Infrastructure.Persistence;

/// <summary>
/// The assemblies that hold each provider's migrations. SQLite and SQL Server need different column types and
/// different SQL, so each has its own history, kept out of Infrastructure so the two never mix.
/// </summary>
public static class MigrationAssemblies
{
    /// <summary>The SQLite migrations.</summary>
    public const string Sqlite = "CivicPermits.Migrations.Sqlite";

    /// <summary>The SQL Server migrations, which also serve LocalDB.</summary>
    public const string SqlServer = "CivicPermits.Migrations.SqlServer";

    /// <summary>The assembly that holds the migrations for a provider.</summary>
    public static string For(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.Sqlite => Sqlite,
        DatabaseProvider.SqlServer => SqlServer,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported database provider."),
    };
}

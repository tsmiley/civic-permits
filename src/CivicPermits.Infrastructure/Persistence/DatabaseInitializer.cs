using CivicPermits.Infrastructure.Identity;
using CivicPermits.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CivicPermits.Infrastructure.Persistence;

/// <summary>Applies migrations and sample data at start-up, when the configuration asks for them.</summary>
public static partial class DatabaseInitializer
{
    /// <summary>
    /// Brings the database up to date, then seeds it. Does nothing unless <see cref="DatabaseOptions.MigrateOnStartup"/>
    /// is set. Sample data, with its sample sign-ins, is refused anywhere but Development, whatever the configuration says.
    /// </summary>
    /// <exception cref="InvalidOperationException">Sample data was requested outside the Development environment.</exception>
    public static async Task InitializeDatabaseAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;

        DatabaseOptions options = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        if (options.SeedSampleData && !app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Sample data can only be loaded in the Development environment, not in {app.Environment.EnvironmentName}.");
        }

        if (!options.MigrateOnStartup)
        {
            return;
        }

        CivicPermitsDbContext database = services.GetRequiredService<CivicPermitsDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
        LogMigrated(logger, options.Provider);

        await services.GetRequiredService<IdentityBootstrapper>().EnsureRolesAsync();

        if (options.SeedSampleData)
        {
            (int types, int applications) = await new SampleDataSeeder(database, services.GetRequiredService<TimeProvider>())
                .SeedAsync(cancellationToken);
            LogSeeded(logger, types, applications);

            if (!string.IsNullOrEmpty(options.SampleUserPassword))
            {
                int people = await services.GetRequiredService<IdentityBootstrapper>().EnsureSampleUsersAsync(options.SampleUserPassword);
                LogSampleUsers(logger, people);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database migrations are up to date ({Provider}).")]
    private static partial void LogMigrated(ILogger logger, DatabaseProvider provider);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sample sign-ins: created {People}.")]
    private static partial void LogSampleUsers(ILogger logger, int people);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sample data: added {PermitTypes} permit types and {Applications} applications.")]
    private static partial void LogSeeded(ILogger logger, int permitTypes, int applications);
}

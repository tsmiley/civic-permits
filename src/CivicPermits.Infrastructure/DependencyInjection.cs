using CivicPermits.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CivicPermits.Infrastructure;

/// <summary>Registers persistence and identity.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds the database context for the configured provider, ASP.NET Core Identity stores, and the
    /// readiness check. Options are validated at start-up.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();

        services.AddDbContext<CivicPermitsDbContext>((provider, builder) =>
        {
            DatabaseOptions database = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            switch (database.Provider)
            {
                case DatabaseProvider.SqlServer:
                    builder.UseSqlServer(database.ConnectionString);
                    break;
                case DatabaseProvider.Sqlite:
                    builder.UseSqlite(database.ConnectionString);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported database provider '{database.Provider}'.");
            }
        });

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Length beats composition rules. Twelve characters, no forced symbol soup.
                options.Password.RequiredLength = 12;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<CivicPermitsDbContext>();

        services
            .AddHealthChecks()
            .AddDbContextCheck<CivicPermitsDbContext>("database", tags: [HealthTags.Ready]);

        return services;
    }
}

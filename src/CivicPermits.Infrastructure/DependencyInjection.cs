using CivicPermits.Application.Abstractions;
using CivicPermits.Infrastructure.Identity;
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
                    builder.UseSqlServer(database.ConnectionString, sql => sql.MigrationsAssembly(MigrationAssemblies.SqlServer));
                    break;
                case DatabaseProvider.Sqlite:
                    builder.UseSqlite(database.ConnectionString, sqlite => sqlite.MigrationsAssembly(MigrationAssemblies.Sqlite));
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported database provider '{database.Provider}'.");
            }
        });

        // The sign-in manager needs the authentication services. Each host adds the schemes it uses (bearer tokens for
        // the API, cookies for the web site).
        services.AddAuthentication();

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
            .AddSignInManager()
            .AddEntityFrameworkStores<CivicPermitsDbContext>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IPermitTypeStore, PermitTypeStore>();
        services.AddScoped<IPermitApplicationStore, PermitApplicationStore>();
        services.AddScoped<IPermitNumberAllocator, PermitNumberAllocator>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IdentityBootstrapper>();

        services
            .AddHealthChecks()
            .AddDbContextCheck<CivicPermitsDbContext>("database", tags: [HealthTags.Ready]);

        return services;
    }
}

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
                // NIST SP 800-63B-4: length and a blocklist (CommonPasswordValidator), not rules about which kinds of
                // character to include. Fifteen characters; spaces and 64 or more are accepted; no composition rules.
                options.Password.RequiredLength = PasswordPolicy.MinimumLength;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddPasswordValidator<CommonPasswordValidator>()
            .AddSignInManager()
            .AddEntityFrameworkStores<CivicPermitsDbContext>();

        // The security baseline (docs/THREAT_MODEL.md) asks for at least 220,000 PBKDF2 iterations; Identity's default is 100,000. Hashes made with fewer are
        // upgraded the next time their owner signs in.
        services.Configure<PasswordHasherOptions>(hasher => hasher.IterationCount = PasswordPolicy.IterationCount);

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IPermitTypeStore, PermitTypeStore>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IPermitApplicationStore, PermitApplicationStore>();
        services.AddScoped<IPermitNumberAllocator, PermitNumberAllocator>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IdentityBootstrapper>();
        services.AddScoped<RegistrationService>();
        services.AddScoped<AbsentAccountTiming>();
        services.AddScoped<SignInActivity>();
        services.AddScoped<AccountClosing>();
        services.AddScoped<IRetentionService, RetentionService>();

        services
            .AddHealthChecks()
            .AddDbContextCheck<CivicPermitsDbContext>("database", tags: [HealthTags.Ready]);

        return services;
    }
}

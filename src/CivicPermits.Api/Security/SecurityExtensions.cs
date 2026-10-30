using CivicPermits.Domain.Applications;
using CivicPermits.Infrastructure.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace CivicPermits.Api.Security;

/// <summary>The names of the authorization policies, so an endpoint never repeats a string.</summary>
internal static class Policies
{
    public const string Staff = "Staff";
    public const string Administrator = "Administrator";
    public const string Applicant = "Applicant";
    public const string Inspection = "Inspection";
    public const string SignedIn = "SignedIn";
}

internal static class SecurityExtensions
{
    /// <summary>Bearer tokens for sign-in, role policies, and the rate limiter.</summary>
    public static IServiceCollection AddCivicPermitsSecurity(this IServiceCollection services)
    {
        // Opaque, data-protected tokens: nothing to sign with, so no key in configuration. Short-lived access,
        // refresh for a working day.
        services
            .AddAuthentication(IdentityConstants.BearerScheme)
            .AddBearerToken(IdentityConstants.BearerScheme, options =>
            {
                options.BearerTokenExpiration = TimeSpan.FromMinutes(15);
                options.RefreshTokenExpiration = TimeSpan.FromHours(8);
            });

        services.AddAuthorizationBuilder()
            // Authenticated by default: an endpoint added later without a policy is closed, not open. The eight public
            // operations say so explicitly with AllowAnonymous, and a test pins exactly that set.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.SignedIn, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(Policies.Staff, policy => policy.RequireRole(nameof(ActorRole.Clerk), nameof(ActorRole.Administrator)))
            .AddPolicy(Policies.Administrator, policy => policy.RequireRole(nameof(ActorRole.Administrator)))
            .AddPolicy(Policies.Applicant, policy => policy.RequireRole(nameof(ActorRole.Applicant)))
            .AddPolicy(Policies.Inspection, policy => policy.RequireRole(nameof(ActorRole.Inspector), nameof(ActorRole.Administrator)));

        services.AddCivicPermitsRateLimiting();

        return services;
    }
}

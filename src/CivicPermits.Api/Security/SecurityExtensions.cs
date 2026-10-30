using System.Threading.RateLimiting;
using CivicPermits.Domain.Applications;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

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

/// <summary>The names of the rate-limit policies.</summary>
internal static class RateLimits
{
    public const string Auth = "auth";
    public const string Public = "public";
}

/// <summary>How many requests are allowed per window. Small numbers on the endpoints anyone can reach.</summary>
internal sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Sign-in, registration and token refresh, per client address.</summary>
    public int AuthPerWindow { get; set; } = 10;

    /// <summary>The public permit lookup, per client address.</summary>
    public int PublicPerWindow { get; set; } = 30;

    /// <summary>Everything else, per signed-in person or client address.</summary>
    public int GlobalPerWindow { get; set; } = 600;

    /// <summary>The length of a window, in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;
}

internal sealed class RateLimitingOptionsValidator : IValidateOptions<RateLimitingOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options) =>
        options is { AuthPerWindow: > 0, PublicPerWindow: > 0, GlobalPerWindow: > 0, WindowSeconds: > 0 }
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{RateLimitingOptions.SectionName}: every limit and the window must be greater than zero.");
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
            .SetFallbackPolicy(null)
            .AddPolicy(Policies.SignedIn, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(Policies.Staff, policy => policy.RequireRole(nameof(ActorRole.Clerk), nameof(ActorRole.Administrator)))
            .AddPolicy(Policies.Administrator, policy => policy.RequireRole(nameof(ActorRole.Administrator)))
            .AddPolicy(Policies.Applicant, policy => policy.RequireRole(nameof(ActorRole.Applicant)))
            .AddPolicy(Policies.Inspection, policy => policy.RequireRole(nameof(ActorRole.Inspector), nameof(ActorRole.Administrator)));

        services.AddOptions<RateLimitingOptions>().BindConfiguration(RateLimitingOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<RateLimitingOptions>, RateLimitingOptionsValidator>();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitingOptions>>(ConfigureRateLimiter);

        return services;
    }

    private static void ConfigureRateLimiter(RateLimiterOptions limiter, IOptions<RateLimitingOptions> configured)
    {
        RateLimitingOptions settings = configured.Value;
        TimeSpan window = TimeSpan.FromSeconds(settings.WindowSeconds);

        limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        limiter.OnRejected = async (context, _) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            IProblemDetailsService problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

            await problems.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context.HttpContext,
                ProblemDetails =
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests",
                    Detail = "You are making requests too quickly. Wait a moment and try again.",
                },
            });
        };

        // Everything is limited a little; the endpoints anyone can reach are limited a lot.
        limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                "global:" + ClientKey(context),
                _ => Window(settings.GlobalPerWindow, window)));

        limiter.AddPolicy(RateLimits.Auth, context =>
            RateLimitPartition.GetFixedWindowLimiter("auth:" + ClientKey(context), _ => Window(settings.AuthPerWindow, window)));
        limiter.AddPolicy(RateLimits.Public, context =>
            RateLimitPartition.GetFixedWindowLimiter("public:" + ClientKey(context), _ => Window(settings.PublicPerWindow, window)));
    }

    private static FixedWindowRateLimiterOptions Window(int permits, TimeSpan window) => new()
    {
        PermitLimit = permits,
        Window = window,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    /// <summary>
    /// Who a request counts against: the signed-in person where there is one, otherwise the connecting address.
    /// Behind a reverse proxy the address is the proxy's until forwarded headers are configured for it.
    /// </summary>
    private static string ClientKey(HttpContext context) =>
        context.User.Identity is { IsAuthenticated: true } && context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier) is { } id
            ? "user:" + id.Value
            : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
}

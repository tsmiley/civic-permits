using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CivicPermits.Infrastructure.Hosting;

/// <summary>The names of the rate-limit policies an endpoint can opt into.</summary>
public static class RateLimits
{
    /// <summary>Sign-in, registration and token refresh.</summary>
    public const string Auth = "auth";

    /// <summary>The public permit lookup.</summary>
    public const string Public = "public";
}

/// <summary>How many requests are allowed per window. Small numbers on the endpoints anyone can reach.</summary>
public sealed class RateLimitingOptions
{
    /// <summary>The configuration section these options bind from.</summary>
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

/// <summary>Per-address and per-person request limits shared by the API and the web site.</summary>
public static class RateLimitingExtensions
{
    /// <summary>
    /// Registers the limiter. A rejected request gets 429 and a <c>Retry-After</c>; a browser (it asks for HTML)
    /// gets an empty body so the site's status page can explain, anything else gets a problem document.
    /// </summary>
    public static IServiceCollection AddCivicPermitsRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<RateLimitingOptions>().BindConfiguration(RateLimitingOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<RateLimitingOptions>, RateLimitingOptionsValidator>();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitingOptions>>(Configure);

        return services;
    }

    private static void Configure(RateLimiterOptions limiter, IOptions<RateLimitingOptions> configured)
    {
        RateLimitingOptions settings = configured.Value;
        TimeSpan window = TimeSpan.FromSeconds(settings.WindowSeconds);

        limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        limiter.OnRejected = async (context, _) =>
        {
            HttpContext http = context.HttpContext;

            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
            {
                http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            if (http.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await http.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = http,
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
            RateLimitPartition.GetFixedWindowLimiter("global:" + ClientKey(context), _ => Window(settings.GlobalPerWindow, window)));

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
        context.User.Identity is { IsAuthenticated: true } && context.User.FindFirst(ClaimTypes.NameIdentifier) is { } id
            ? "user:" + id.Value
            : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
}

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace CivicPermits.Infrastructure.Hosting;

/// <summary>
/// Writes one log line for every sign-in failure, every 401, 403 and 429, and every "that is not yours to know about"
/// 404, naming who asked and which route, so a pattern of refusals can be seen and followed. The line carries no
/// email address, no token and no value from the request: the account is an identifier and the route is the route
/// pattern (<c>/applications/{id}</c>), not the address that was asked for.
/// </summary>
public sealed partial class RefusalLogMiddleware(RequestDelegate next, ILogger<RefusalLogMiddleware> logger)
{
    /// <summary>Set by whatever refused the request: the stable reason, such as <c>application.not_found</c>.</summary>
    public const string CodeKey = "refusal.code";

    /// <summary>
    /// Set by whatever refused the request when the framework has already forgotten the route (the exception handler and
    /// the status-code pages clear it): the route pattern, such as <c>/applications/{id}</c>.
    /// </summary>
    public const string RouteKey = "refusal.route";

    /// <summary>Set when a sign-in failed for an account that exists: its identifier. Never an email address.</summary>
    public const string AccountKey = "refusal.account";

    /// <summary>Runs the rest of the pipeline, then logs the request if it was refused.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        int status = context.Response.StatusCode;
        string? code = context.Items[CodeKey] as string;

        if (code is null && status is not (StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden or StatusCodes.Status429TooManyRequests)
            && !IsRedirectToSignIn(context))
        {
            return;
        }

        code ??= status switch
        {
            StatusCodes.Status401Unauthorized => "unauthenticated",
            StatusCodes.Status403Forbidden => "forbidden",
            StatusCodes.Status429TooManyRequests => "rate_limited",
            _ => "unauthenticated",
        };

        string account = context.Items[AccountKey] as string
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "anonymous";
        string route = context.Items[RouteKey] as string ?? (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(no route)";

        Refused(logger, account, context.Request.Method, route, status, code);
    }

    // The site answers someone who is not signed in with a redirect to the sign-in page rather than a 401.
    private static bool IsRedirectToSignIn(HttpContext context) =>
        context.Response.StatusCode is StatusCodes.Status302Found or StatusCodes.Status303SeeOther
        && context.Response.Headers.Location.ToString().StartsWith("/account/login", StringComparison.OrdinalIgnoreCase)
        && !context.Request.Path.StartsWithSegments("/account", StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused: account {Account} {Method} {Route} answered {Status} ({Code})")]
    private static partial void Refused(ILogger logger, string account, string method, string route, int status, string code);
}

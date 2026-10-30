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
        string? redirected = RefusalOfRedirect(context);

        if (code is null && redirected is null && status is not (StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden or StatusCodes.Status429TooManyRequests))
        {
            return;
        }

        // The page a refused signed-in person is sent to answers 403 itself; the line was already written for the request
        // that was refused, with the route that was asked for, so this one would only repeat it with a less useful route.
        if (code is null && redirected is null && context.Request.Path.StartsWithSegments(DeniedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        code ??= redirected ?? status switch
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

    private const string LoginPath = "/account/login";
    private const string DeniedPath = "/account/denied";

    // The site answers someone who is not signed in with a redirect to the sign-in page rather than a 401, and someone who
    // is signed in without the role with a redirect to the access-denied page rather than a 403.
    private static string? RefusalOfRedirect(HttpContext context)
    {
        if (context.Response.StatusCode is not (StatusCodes.Status302Found or StatusCodes.Status303SeeOther)
            || context.Request.Path.StartsWithSegments("/account", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string path = PathOf(context.Response.Headers.Location.ToString());

        if (path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase))
        {
            return "unauthenticated";
        }

        return path.Equals(DeniedPath, StringComparison.OrdinalIgnoreCase) ? "forbidden" : null;
    }

    // The framework sends an absolute address (http://host/account/login?ReturnUrl=...); only its path says where it leads.
    private static string PathOf(string location)
    {
        string path = Uri.TryCreate(location, UriKind.Absolute, out Uri? absolute) ? absolute.AbsolutePath : location;
        int end = path.IndexOfAny(['?', '#']);

        return end < 0 ? path : path[..end];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused: account {Account} {Method} {Route} answered {Status} ({Code})")]
    private static partial void Refused(ILogger logger, string account, string method, string route, int status, string code);
}

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace CivicPermits.Infrastructure.Hosting;

/// <summary>
/// Gives every request an identifier that appears on the response, in every log line and in every error body,
/// so one failed request can be followed from the user's screen to the log.
/// </summary>
internal sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        string id = context.Request.Headers[HeaderName].FirstOrDefault() is { } supplied && Acceptable().IsMatch(supplied)
            ? supplied
            : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = id;

        // Written when the response starts, not now: the exception handler clears the response, headers
        // included, and the id matters most on exactly those error responses.
        context.Response.OnStarting(static state =>
        {
            (HttpContext http, string value) = ((HttpContext, string))state;
            http.Response.Headers[HeaderName] = value;

            return Task.CompletedTask;
        }, (context, id));

        using (LogContext.PushProperty(ItemKey, id))
        {
            await next(context);
        }
    }

    // A caller-supplied value ends up in logs and headers, so it is restricted to a safe, short alphabet.
    [GeneratedRegex("^[A-Za-z0-9-]{8,64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Acceptable();
}

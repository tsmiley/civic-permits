using Microsoft.AspNetCore.Http;

namespace CivicPermits.Infrastructure.Hosting;

/// <summary>Adds the security headers to every response, including error responses.</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, SecurityHeadersPolicy policy)
{
    public Task InvokeAsync(HttpContext context)
    {
        // Set when the response starts, so a header is present even when a later component writes the body.
        context.Response.OnStarting(static state =>
        {
            (HttpContext http, SecurityHeadersPolicy value) = ((HttpContext, SecurityHeadersPolicy))state;
            IHeaderDictionary headers = http.Response.Headers;

            headers["Content-Security-Policy"] = value.ContentSecurityPolicy;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";

            return Task.CompletedTask;
        }, (context, policy));

        return next(context);
    }
}

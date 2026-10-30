using Microsoft.AspNetCore.Http;

namespace CivicPermits.Infrastructure.Hosting;

/// <summary>Which responses must not be kept by a browser or a shared cache.</summary>
public enum NoStoreScope
{
    /// <summary>Every response under <c>/api</c>: tokens, accounts and applications are all personal.</summary>
    Api = 1,

    /// <summary>Pages a signed-in person receives, and the account pages. Static files stay cacheable.</summary>
    SignedInPages = 2,
}

/// <summary>
/// Sends <c>Cache-Control: no-store</c> on any response that carries a person's data, so a back button, a shared computer
/// or an intermediary cannot show it to the next person.
/// </summary>
internal sealed class NoStoreMiddleware(RequestDelegate next, NoStoreScope scope)
{
    public Task InvokeAsync(HttpContext context)
    {
        // Decided when the response starts, because who is signed in is only known after authentication has run.
        context.Response.OnStarting(static state =>
        {
            (HttpContext http, NoStoreScope value) = ((HttpContext, NoStoreScope))state;

            if (Applies(http, value))
            {
                http.Response.Headers.CacheControl = "no-store";
                http.Response.Headers.Pragma = "no-cache";
            }

            return Task.CompletedTask;
        }, (context, scope));

        return next(context);
    }

    private static bool Applies(HttpContext http, NoStoreScope scope) => scope switch
    {
        NoStoreScope.Api => http.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase),
        NoStoreScope.SignedInPages => !Path.HasExtension(http.Request.Path.Value)
            && (http.User.Identity?.IsAuthenticated == true || http.Request.Path.StartsWithSegments("/account", StringComparison.OrdinalIgnoreCase)),
        _ => false,
    };
}

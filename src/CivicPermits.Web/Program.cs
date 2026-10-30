using CivicPermits.Infrastructure.Hosting;
using CivicPermits.Infrastructure.Persistence;
using CivicPermits.Web.Components;
using Microsoft.AspNetCore.Identity;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddCivicPermitsHosting();
builder.Services.AddCivicPermitsRateLimiting();
builder.Services.AddRazorComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

// The browser keeps a cookie, not a token. It cannot be read by script, is not sent with cross-site requests that
// change anything, and is only ever sent over HTTPS outside development.
CookieSecurePolicy securePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies(identity => identity.ApplicationCookie?.Configure(options =>
    {
        options.Cookie.Name = "cp.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = securePolicy;
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    }));

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "cp.antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = securePolicy;
});

WebApplication app = builder.Build();

await app.InitializeDatabaseAsync();

app.UseCivicPermitsCommon(SecurityHeadersPolicy.ForWebSite, NoStoreScope.SignedInPages);

if (!app.Environment.IsDevelopment())
{
    // People get a page, not a problem document; the correlation id is still in the log.
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/status/{0}", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapCivicPermitsHealth();
app.MapRazorComponents<App>();

app.Run();

/// <summary>Exposed so tests can find the assembly that holds the site.</summary>
public partial class Program;

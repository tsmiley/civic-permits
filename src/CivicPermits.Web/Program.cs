using CivicPermits.Infrastructure.Hosting;
using CivicPermits.Web.Components;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddCivicPermitsHosting();
builder.Services.AddRazorComponents();

WebApplication app = builder.Build();

app.UseCivicPermitsCommon(SecurityHeadersPolicy.ForWebSite);

if (!app.Environment.IsDevelopment())
{
    // People get a page, not a problem document; the correlation id is still in the log.
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapCivicPermitsHealth();
app.MapRazorComponents<App>();

app.Run();

/// <summary>Exposed so tests can find the assembly that holds the site.</summary>
public partial class Program;

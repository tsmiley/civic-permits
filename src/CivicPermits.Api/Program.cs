using CivicPermits.Api.Endpoints;
using CivicPermits.Api.Problems;
using CivicPermits.Api.Security;
using CivicPermits.Infrastructure.Hosting;
using CivicPermits.Infrastructure.Persistence;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddCivicPermitsHosting();
builder.Services.AddCivicPermitsSecurity();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi(options => options.AddBearerSecurity());

WebApplication app = builder.Build();

await app.InitializeDatabaseAsync();

app.UseCivicPermitsCommon(SecurityHeadersPolicy.ForApi, NoStoreScope.Api);

// Expected failures become problem documents with a stable code; anything else becomes a bare 500.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapCivicPermitsHealth();

RouteGroupBuilder api = app.MapGroup("/api/v1");
api.MapSystemEndpoints();
api.MapAuthEndpoints();
api.MapPermitEndpoints();

app.Run();

/// <summary>Exposed so the integration tests can host the API in memory.</summary>
public partial class Program;

using CivicPermits.Api.Endpoints;
using CivicPermits.Infrastructure.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddCivicPermitsHosting();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.UseCivicPermitsCommon(SecurityHeadersPolicy.ForApi);

// Unhandled exceptions become an RFC 9457 problem document with no stack trace and no exception text.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapCivicPermitsHealth();
app.MapGroup("/api/v1").MapSystemEndpoints();

app.Run();

/// <summary>Exposed so the integration tests can host the API in memory.</summary>
public partial class Program;

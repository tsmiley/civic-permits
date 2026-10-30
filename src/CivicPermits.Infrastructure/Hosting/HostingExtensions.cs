using System.Globalization;
using CivicPermits.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace CivicPermits.Infrastructure.Hosting;

/// <summary>The set-up the API and the web site share, so the two hosts cannot drift apart.</summary>
public static class HostingExtensions
{
    /// <summary>
    /// Registers logging, problem details, the application layer and infrastructure. Call once per host.
    /// </summary>
    public static WebApplicationBuilder AddCivicPermitsHosting(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Host.UseSerilog((context, services, logger) => logger
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}",
                formatProvider: CultureInfo.InvariantCulture));

        builder.Services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                if (context.HttpContext.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)
                {
                    context.ProblemDetails.Extensions["correlationId"] = correlationId;
                }
            });

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);

        return builder;
    }

    /// <summary>
    /// Adds the middleware both hosts need, in the order that matters: the correlation id first so everything
    /// after it is traceable, then the security headers, then request logging.
    /// </summary>
    public static WebApplication UseCivicPermitsCommon(this WebApplication app, SecurityHeadersPolicy headers)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(headers);

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>(headers);
        app.UseSerilogRequestLogging();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();

            // Behind a TLS-terminating proxy the redirect belongs to the proxy, so it can be switched off.
            if (app.Configuration.GetValue("Hosting:RedirectToHttps", true))
            {
                app.UseHttpsRedirection();
            }
        }

        return app;
    }

    /// <summary>
    /// Maps <c>/health/live</c> (the process is up; runs no checks) and <c>/health/ready</c> (dependencies such as
    /// the database answer). Neither reveals more than a status word.
    /// </summary>
    public static WebApplication MapCivicPermitsHealth(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = static _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = static check => check.Tags.Contains(HealthTags.Ready),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        });

        return app;
    }
}

using System.Net;
using System.Text.Json;
using CivicPermits.Application.Messaging;
using CivicPermits.Infrastructure.Persistence;
using CivicPermits.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CivicPermits.Api.IntegrationTests;

public class ApiTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_info_endpoint_describes_the_service_without_signing_in()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/info", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ApplicationInfoResponse? info = await response.Content.ReadFromJsonAsync<ApplicationInfoResponse>(Token);
        Assert.NotNull(info);
        Assert.Equal("Civic Permits", info.Name);
        Assert.False(string.IsNullOrWhiteSpace(info.Version));
    }

    [Fact]
    public async Task Every_response_carries_the_security_headers_and_a_correlation_id()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/info", Token);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{32}$", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task A_sensible_caller_supplied_correlation_id_is_kept_and_a_hostile_one_is_replaced()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage fine = new(HttpMethod.Get, "/api/v1/info");
        fine.Headers.Add("X-Correlation-ID", "client-trace-0001");
        using HttpRequestMessage hostile = new(HttpMethod.Get, "/api/v1/info");
        hostile.Headers.TryAddWithoutValidation("X-Correlation-ID", "x\" onload=\"alert(1)");

        HttpResponseMessage kept = await client.SendAsync(fine, Token);
        HttpResponseMessage replaced = await client.SendAsync(hostile, Token);

        Assert.Equal("client-trace-0001", kept.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Matches("^[0-9a-f]{32}$", replaced.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task Liveness_needs_no_dependencies_and_readiness_waits_for_the_database()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage live = await client.GetAsync("/health/live", Token);
        HttpResponseMessage notReady = await client.GetAsync("/health/ready", Token);

        // Alive does not mean ready: with no database yet the service must not ask for traffic.
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, notReady.StatusCode);

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            CivicPermitsDbContext database = scope.ServiceProvider.GetRequiredService<CivicPermitsDbContext>();
            await database.Database.EnsureCreatedAsync(Token);
        }

        HttpResponseMessage ready = await client.GetAsync("/health/ready", Token);

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task Migrating_and_seeding_on_start_make_the_api_ready_with_the_sample_town()
    {
        using ApiFactory factory = new ApiFactory()
            .WithSetting("Database:MigrateOnStartup", "true")
            .WithSetting("Database:SeedSampleData", "true");
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage ready = await client.GetAsync("/health/ready", Token);

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        using IServiceScope scope = factory.Services.CreateScope();
        CivicPermitsDbContext database = scope.ServiceProvider.GetRequiredService<CivicPermitsDbContext>();

        Assert.Equal(9, await database.PermitTypes.CountAsync(Token));
        Assert.Equal(12, await database.PermitApplications.CountAsync(Token));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public void Sample_data_and_its_sign_ins_are_refused_outright_anywhere_but_development(string environment)
    {
        using ApiFactory factory = new ApiFactory()
            .InEnvironment(environment)
            .WithSetting("Database:MigrateOnStartup", "true")
            .WithSetting("Database:SeedSampleData", "true");

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Development", refused.Message, StringComparison.Ordinal);
        Assert.Contains(environment, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Seeding_without_migrating_is_a_configuration_error()
    {
        using ApiFactory factory = new ApiFactory()
            .WithSetting("Database:SeedSampleData", "true");

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task An_unknown_route_is_a_404_problem_document_for_someone_signed_in_and_a_401_for_someone_who_is_not()
    {
        using SeededApi api = new();
        using HttpClient anonymous = api.Anonymous();
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);

        HttpResponseMessage signedIn = await applicant.GetAsync("/api/v1/nothing-here", Token);

        Assert.Equal(HttpStatusCode.NotFound, signedIn.StatusCode);
        Assert.Equal("application/problem+json", signedIn.Content.Headers.ContentType?.MediaType);

        // The API is closed by default, so a stranger cannot tell a route that does not exist from one that does.
        HttpResponseMessage stranger = await anonymous.GetAsync("/api/v1/nothing-here", Token);
        HttpResponseMessage realButClosed = await anonymous.GetAsync("/api/v1/applications", Token);

        Assert.Equal(HttpStatusCode.Unauthorized, stranger.StatusCode);
        Assert.Equal(realButClosed.StatusCode, stranger.StatusCode);
        Assert.Equal("application/problem+json", stranger.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task An_unexpected_failure_is_a_500_problem_that_reveals_nothing_and_still_carries_the_correlation_id()
    {
        using ApiFactory factory = new ApiFactory()
            .InEnvironment("Production")
            .WithServices(services => services.Replace(ServiceDescriptor.Scoped<IDispatcher, ApiFactory.ThrowingDispatcher>()));
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/info", Token);
        string body = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("SECRET-INTERNAL-DETAIL", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.Equal(
            response.Headers.GetValues("X-Correlation-ID").Single(),
            JsonDocument.Parse(body).RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public void A_missing_connection_string_stops_the_host_at_start_up_with_a_clear_message()
    {
        using ApiFactory factory = new ApiFactory().WithSetting("Database:ConnectionString", string.Empty);

        OptionsValidationException failure = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Database:ConnectionString is required", failure.Message, StringComparison.Ordinal);
    }
}

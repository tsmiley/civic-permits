using System.Net;
using CivicPermits.Application.Abstractions;
using CivicPermits.Application.Messaging;
using CivicPermits.Domain.Common;
using CivicPermits.Infrastructure.Hosting;
using CivicPermits.Infrastructure.Persistence;
using CivicPermits.Infrastructure.Persistence.Seeding;
using CivicPermits.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Serilog.Core;

namespace CivicPermits.Api.IntegrationTests;

/// <summary>What a security review of the API asked to be pinned: headers, caching, logging, who is public, expiry, mapping, limits.</summary>
public class HardeningTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly string[] RulebookHeaders =
    [
        "Content-Security-Policy", "X-Content-Type-Options", "X-Frame-Options", "Referrer-Policy",
        "Permissions-Policy", "Cross-Origin-Opener-Policy", "Cross-Origin-Resource-Policy", "X-Permitted-Cross-Domain-Policies",
    ];

    [Theory]
    [InlineData("abcdef12-3456", true)]
    [InlineData("short", false)]
    [InlineData("abcdef12-3456\n", false)] // a trailing newline slipped past a regex that ended with a dollar sign
    [InlineData("abcdef12-3456\r\nX-Injected: yes", false)]
    [InlineData("abcdef12 3456", false)]
    public async Task A_caller_supplied_correlation_id_is_kept_only_when_it_is_the_safe_alphabet_to_the_last_character(string supplied, bool kept)
    {
        DefaultHttpContext context = new();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = supplied;
        CorrelationIdMiddleware middleware = new(static _ => Task.CompletedTask);

        await middleware.InvokeAsync(context);
        string used = (string)context.Items[CorrelationIdMiddleware.ItemKey]!;

        Assert.Equal(kept, used == supplied);
        Assert.Matches(@"^[A-Za-z0-9-]{8,64}$", used);
    }

    private static void AssertEveryHeader(HttpResponseMessage response, string where)
    {
        foreach (string header in RulebookHeaders)
        {
            Assert.True(response.Headers.Contains(header), $"{where}: the {header} header is missing.");
        }

        Assert.Equal("none", response.Headers.GetValues("X-Permitted-Cross-Domain-Policies").Single());
        Assert.Equal("same-site", response.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.False(response.Headers.Contains("Server"), $"{where}: the Server header should not be sent.");
        Assert.False(response.Headers.Contains("X-Powered-By"), $"{where}: X-Powered-By should not be sent.");
        Assert.True(response.Headers.Contains("X-Correlation-Id"), $"{where}: no correlation id.");
    }

    // ---- Headers and caching ----

    [Fact]
    public async Task Every_rulebook_header_is_on_a_success_a_refusal_and_a_miss()
    {
        using SeededApi api = new();
        using HttpClient anonymous = api.Anonymous();
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);

        AssertEveryHeader(await anonymous.GetAsync("/api/v1/info", Token), "200 info");
        AssertEveryHeader(await anonymous.GetAsync("/api/v1/applications", Token), "401 no sign-in");
        AssertEveryHeader(await applicant.PostAsJsonAsync("/api/v1/permit-types", new PermitTypeRequest("Sign", "Nope nope", "Nothing at all here.", 0, 0, false, 365, 5), Token), "403 wrong role");
        AssertEveryHeader(await applicant.GetAsync($"/api/v1/applications/{Guid.NewGuid()}", Token), "404 not found");
        AssertEveryHeader(await anonymous.GetAsync("/api/v1/no-such-thing", Token), "404 no route");
        AssertEveryHeader(await anonymous.GetAsync("/openapi/v1.json", Token), "200 openapi document");
    }

    [Fact]
    public async Task Responses_that_carry_a_persons_data_are_never_kept()
    {
        using SeededApi api = new();
        using HttpClient anonymous = api.Anonymous();
        HttpResponseMessage login = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(SeededApi.ApplicantOne, api.Password), Token);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal("no-store", login.Headers.CacheControl?.ToString());

        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        PagedResponse<ApplicationSummaryResponse> mine = (await applicant.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>("/api/v1/applications", Token))!;
        HttpResponseMessage one = await applicant.GetAsync($"/api/v1/applications/{mine.Items[0].Id}", Token);
        HttpResponseMessage me = await applicant.GetAsync("/api/v1/auth/me", Token);

        Assert.Equal("no-store", one.Headers.CacheControl?.ToString());
        Assert.Equal("no-store", me.Headers.CacheControl?.ToString());
        Assert.Equal("no-store", (await applicant.GetAsync("/api/v1/applications", Token)).Headers.CacheControl?.ToString());
        Assert.Contains("no-cache", login.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_sends_strict_transport_security_over_https()
    {
        using ApiFactory factory = new ApiFactory().InEnvironment("Production");
        using HttpClient client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://permits.example") });

        HttpResponseMessage response = await client.GetAsync("/health/live", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("max-age=", response.Headers.GetValues("Strict-Transport-Security").Single(), StringComparison.Ordinal);
        AssertEveryHeader(response, "production health");
    }

    // ---- The refusal log ----

    [Fact]
    public async Task A_failed_sign_in_for_a_real_account_is_logged_by_account_and_route_and_never_by_address()
    {
        CapturingSink sink = new();
        using SeededApi api = new(factory => factory.WithServices(services => services.AddSingleton<ILogEventSink>(sink)));
        using HttpClient anonymous = api.Anonymous();

        await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(SeededApi.ApplicantOne, "not-the-password-1A"), Token);
        await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("nobody@bramblewick.test", "not-the-password-1A"), Token);

        Assert.True(sink.Events.Count > 0, "The test sink received no log events at all: the host did not use it.");
        IReadOnlyList<Serilog.Events.LogEvent> lines = sink.Refusals;
        Assert.True(lines.Count == 2, $"Expected two refusal lines, found {lines.Count}. Events seen: " + string.Join(" | ", sink.Events.Select(entry => entry.RenderMessage(System.Globalization.CultureInfo.InvariantCulture)).Take(15)));

        Serilog.Events.LogEvent real = lines[0];
        Assert.Equal(SampleIdentities.ApplicantOne.ToString(), CapturingSink.Property(real, "Account"));
        Assert.Equal("/api/v1/auth/login", CapturingSink.Property(real, "Route"));
        Assert.Equal("401", CapturingSink.Property(real, "Status"));
        Assert.Equal("auth.sign_in_failed", CapturingSink.Property(real, "Code"));
        Assert.Equal("POST", CapturingSink.Property(real, "Method"));

        Assert.Equal("anonymous", CapturingSink.Property(lines[1], "Account"));

        // Nothing the person typed reaches the log: no address, no password.
        string everything = string.Join("\n", sink.Events.Select(entry => entry.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + string.Join(",", entry.Properties.Select(p => p.Value.ToString()))));
        Assert.DoesNotContain("bramblewick.test", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("not-the-password", everything, StringComparison.Ordinal);
        Assert.DoesNotContain(api.Password, everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_sign_in_a_wrong_role_and_a_hidden_application_each_leave_one_line_with_the_reason()
    {
        CapturingSink sink = new();
        using SeededApi api = new(factory => factory.WithServices(services => services.AddSingleton<ILogEventSink>(sink)));
        using HttpClient anonymous = api.Anonymous();
        using HttpClient applicantOne = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        using HttpClient applicantTwo = await api.SignInAsync(SeededApi.ApplicantTwo, Token);
        Guid someoneElses = (await applicantOne.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>("/api/v1/applications", Token))!.Items[0].Id;

        await anonymous.GetAsync("/api/v1/applications", Token);
        await applicantOne.PostAsync("/api/v1/inspectors/../permit-types/x/close", null, Token);
        await applicantOne.GetAsync("/api/v1/inspectors", Token);
        await applicantTwo.GetAsync($"/api/v1/applications/{someoneElses}", Token);

        IReadOnlyList<Serilog.Events.LogEvent> lines = sink.Refusals;
        List<string?> codes = [.. lines.Select(line => CapturingSink.Property(line, "Code"))];

        Assert.Contains("unauthenticated", codes);
        Assert.Contains("forbidden", codes);
        Assert.Contains("application.not_found", codes);

        Serilog.Events.LogEvent hidden = lines.Single(line => CapturingSink.Property(line, "Code") == "application.not_found");
        Assert.Equal("404", CapturingSink.Property(hidden, "Status"));
        Assert.Equal("/api/v1/applications/{id:guid}", CapturingSink.Property(hidden, "Route"));
        Assert.NotEqual("anonymous", CapturingSink.Property(hidden, "Account"));
        Assert.DoesNotContain(someoneElses.ToString(), string.Join("\n", sink.Events.Select(entry => entry.RenderMessage(System.Globalization.CultureInfo.InvariantCulture))), StringComparison.Ordinal);
        Assert.Equal("anonymous", CapturingSink.Property(lines.First(line => CapturingSink.Property(line, "Code") == "unauthenticated"), "Account"));
    }

    // ---- Who is public ----

    [Fact]
    public async Task Exactly_the_ten_public_operations_are_open_to_anyone_and_everything_else_needs_a_sign_in_by_default()
    {
        using SeededApi api = new();
        IEnumerable<EndpointDataSource> sources = api.Factory.Services.GetServices<EndpointDataSource>();

        List<string> open = [.. sources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"])
                .Select(method => $"{method} {endpoint.RoutePattern.RawText!.TrimEnd('/')}"))
            .Order(StringComparer.Ordinal)];

        string[] expected =
        [
            "ANY /health/live",
            "ANY /health/ready",
            "GET /api/v1/info",
            "GET /api/v1/permit-types",
            "GET /api/v1/permit-types/{id:guid}",
            "GET /api/v1/public/permits/{number}",
            "GET /openapi/{documentName}.json",
            "POST /api/v1/auth/login",
            "POST /api/v1/auth/refresh",
            "POST /api/v1/auth/register",
        ];

        Assert.Equal(expected, open);

        // The default for any endpoint with no rule of its own is "must be signed in".
        AuthorizationPolicy? fallback = await api.Factory.Services.GetRequiredService<IAuthorizationPolicyProvider>().GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback.Requirements, requirement => requirement is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    // ---- Expiry, mapping and limits ----

    [Fact]
    public async Task A_refresh_token_stops_working_when_its_time_is_up()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        using SeededApi api = new(factory => factory.WithServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock))));
        using HttpClient client = api.Anonymous();
        TokenResponse early = (await (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(SeededApi.ApplicantOne, api.Password), Token)).Content.ReadFromJsonAsync<TokenResponse>(Token))!;
        TokenResponse late = (await (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(SeededApi.ApplicantOne, api.Password), Token)).Content.ReadFromJsonAsync<TokenResponse>(Token))!;

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(early.RefreshToken), Token)).StatusCode);

        // A refresh token lasts eight hours; at nine it is refused, however genuine it is.
        clock.Advance(TimeSpan.FromHours(8));
        HttpResponseMessage expired = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(late.RefreshToken), Token);

        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Theory]
    [InlineData("workflow.not_your_application", 404)]
    [InlineData("inspection.unknown", 404)]
    [InlineData("inspection.not_assigned", 403)]
    [InlineData("workflow.role_not_allowed", 403)]
    [InlineData("workflow.transition_not_allowed", 409)]
    [InlineData("application.not_editable", 409)]
    [InlineData("issue.fee_unpaid", 409)]
    [InlineData("payment.already_recorded", 409)]
    [InlineData("inspection.closed", 409)]
    [InlineData("address.postal_code", 422)]
    [InlineData("application.reason_required", 422)]
    [InlineData("a.code.nobody.has.seen", 422)]
    public async Task Each_kind_of_broken_rule_is_answered_with_its_own_status_and_its_code(string code, int expectedStatus)
    {
        using SeededApi api = new(factory => factory.WithServices(services =>
            services.Replace(ServiceDescriptor.Scoped<IDispatcher>(_ => new FailingDispatcher(new DomainRuleException(code, "A message for the person."))))));
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);

        HttpResponseMessage response = await applicant.GetAsync("/api/v1/applications", Token);
        Problem problem = await Problem.ReadAsync(response, Token);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal(code, problem.Code);
        Assert.False(string.IsNullOrEmpty(problem.CorrelationId));
    }

    [Theory]
    [InlineData(RequestFailure.Unauthorized, 401)]
    [InlineData(RequestFailure.Forbidden, 403)]
    [InlineData(RequestFailure.NotFound, 404)]
    [InlineData(RequestFailure.Conflict, 409)]
    public async Task Each_kind_of_refused_request_is_answered_with_its_own_status(RequestFailure failure, int expectedStatus)
    {
        using SeededApi api = new(factory => factory.WithServices(services =>
            services.Replace(ServiceDescriptor.Scoped<IDispatcher>(_ => new FailingDispatcher(new RequestException(failure, "a.reason", "A message for the person."))))));
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);

        HttpResponseMessage response = await applicant.GetAsync("/api/v1/applications", Token);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal("a.reason", (await Problem.ReadAsync(response, Token)).Code);
    }

    [Fact]
    public void The_rate_limit_defaults_are_pinned()
    {
        RateLimitingOptions defaults = new();

        Assert.Equal(10, defaults.AuthPerWindow);
        Assert.Equal(30, defaults.PublicPerWindow);
        Assert.Equal(600, defaults.GlobalPerWindow);
        Assert.Equal(60, defaults.WindowSeconds);
    }

    [Fact]
    public async Task With_no_settings_at_all_the_eleventh_sign_in_attempt_in_a_minute_is_refused()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        for (int attempt = 1; attempt <= 10; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(null, null), Token)).StatusCode);
        }

        HttpResponseMessage limited = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(null, null), Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
    }

    [Fact]
    public async Task An_address_nobody_registered_costs_as_many_password_checks_as_one_that_exists()
    {
        CountingHasher hasher = new();
        using SeededApi api = new(factory => factory.WithServices(services => services.Replace(ServiceDescriptor.Singleton<IPasswordHasher<ApplicationUser>>(hasher))));
        using HttpClient client = api.Anonymous();
        int afterSetUp = hasher.Checks;

        await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(SeededApi.ApplicantOne, "not-the-password-1A"), Token);
        int forARealAccount = hasher.Checks - afterSetUp;

        await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("nobody@bramblewick.test", "not-the-password-1A"), Token);
        int forAnUnknownAddress = hasher.Checks - afterSetUp - forARealAccount;

        Assert.Equal(1, forARealAccount);
        Assert.Equal(forARealAccount, forAnUnknownAddress);
    }

    // ---- Roles at the endpoint ----

    [Theory]
    [InlineData("start-review")]
    [InlineData("request-corrections")]
    [InlineData("approve")]
    [InlineData("deny")]
    [InlineData("issue")]
    public async Task Every_staff_step_is_closed_to_an_applicant_at_the_endpoint_whatever_the_application_status(string step)
    {
        using SeededApi api = new();
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        Guid own = (await applicant.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>("/api/v1/applications", Token))!.Items[0].Id;

        HttpResponseMessage response = await applicant.PostAsJsonAsync($"/api/v1/applications/{own}/{step}", new ReasonRequest("Because I would like it."), Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("submit")]
    [InlineData("withdraw")]
    public async Task Every_applicant_step_is_closed_to_staff_at_the_endpoint(string step)
    {
        using SeededApi api = new();
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        using HttpClient inspector = await api.SignInAsync(SeededApi.InspectorOne, Token);
        Guid any = (await clerk.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>("/api/v1/applications", Token))!.Items[0].Id;

        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PostAsJsonAsync($"/api/v1/applications/{any}/{step}", new ReasonRequest(null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await inspector.PostAsJsonAsync($"/api/v1/applications/{any}/{step}", new ReasonRequest(null), Token)).StatusCode);
    }

    // ---- Inspections ----

    private static async Task<(Guid ApplicationId, InspectionResponse Inspection)> KitchenAsync(HttpClient staff)
    {
        ApplicationSummaryResponse kitchen = (await staff.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>("/api/v1/applications?status=Approved&pageSize=50", Token))!
            .Items.Single(item => item.ProjectDescription.StartsWith("Remodel the kitchen", StringComparison.Ordinal));
        ApplicationResponse full = (await staff.GetFromJsonAsync<ApplicationResponse>($"/api/v1/applications/{kitchen.Id}", Token))!;

        return (kitchen.Id, full.Inspections.Single());
    }

    [Fact]
    public async Task A_clerk_can_cancel_a_booked_inspection_and_the_result_can_then_no_longer_be_recorded()
    {
        using SeededApi api = new();
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        using HttpClient inspector = await api.SignInAsync(SeededApi.InspectorOne, Token);
        (Guid id, InspectionResponse booked) = await KitchenAsync(clerk);

        HttpResponseMessage cancelled = await clerk.PostAsJsonAsync($"/api/v1/applications/{id}/inspections/{booked.Id}/cancel", new ReasonRequest("The inspector is unwell."), Token);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        InspectionResponse after = (await cancelled.Content.ReadFromJsonAsync<ApplicationResponse>(Token))!.Inspections.Single();
        Assert.Equal("Cancelled", after.Status);
        Assert.Equal("The inspector is unwell.", after.Notes);

        HttpResponseMessage late = await inspector.PostAsJsonAsync($"/api/v1/applications/{id}/inspections/{booked.Id}/result", new InspectionResultRequest(true, null), Token);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("inspection.closed", (await Problem.ReadAsync(late, Token)).Code);
    }

    [Fact]
    public async Task Cancelling_an_inspection_is_closed_to_applicants_inspectors_and_the_signed_out()
    {
        using SeededApi api = new();
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        using HttpClient inspector = await api.SignInAsync(SeededApi.InspectorOne, Token);
        using HttpClient anonymous = api.Anonymous();
        (Guid id, InspectionResponse booked) = await KitchenAsync(clerk);
        string path = $"/api/v1/applications/{id}/inspections/{booked.Id}/cancel";

        Assert.Equal(HttpStatusCode.Forbidden, (await applicant.PostAsJsonAsync(path, new ReasonRequest(null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await inspector.PostAsJsonAsync(path, new ReasonRequest(null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(path, new ReasonRequest(null), Token)).StatusCode);
        Assert.Equal("Scheduled", (await clerk.GetFromJsonAsync<ApplicationResponse>($"/api/v1/applications/{id}", Token))!.Inspections.Single().Status);
    }

    [Fact]
    public async Task An_inspection_that_belongs_to_a_different_application_is_not_found_when_cancelled_here()
    {
        using SeededApi api = new();
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        (_, InspectionResponse kitchenInspection) = await KitchenAsync(clerk);
        ApplicationSummaryResponse stall = (await clerk.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>("/api/v1/applications?status=Approved&pageSize=50", Token))!
            .Items.Single(item => item.ProjectDescription.StartsWith("A produce stall", StringComparison.Ordinal));

        HttpResponseMessage response = await clerk.PostAsJsonAsync($"/api/v1/applications/{stall.Id}/inspections/{kitchenInspection.Id}/cancel", new ReasonRequest(null), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("inspection.unknown", (await Problem.ReadAsync(response, Token)).Code);
    }

    [Fact]
    public async Task An_inspector_who_can_see_an_application_but_is_not_assigned_to_an_inspection_is_forbidden_not_unprocessable()
    {
        using SeededApi api = new();
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        using HttpClient inspectorOne = await api.SignInAsync(SeededApi.InspectorOne, Token);

        // The deck application failed an inspection by inspector one, so inspector one can see it. Book inspector two.
        ApplicationSummaryResponse deck = (await clerk.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>("/api/v1/applications?status=Approved&pageSize=50", Token))!
            .Items.Single(item => item.ProjectDescription.StartsWith("Build a raised timber deck", StringComparison.Ordinal));
        HttpResponseMessage booked = await clerk.PostAsJsonAsync(
            $"/api/v1/applications/{deck.Id}/inspections",
            new ScheduleInspectionRequest(SampleIdentities.InspectorTwo, DateTimeOffset.UtcNow.AddDays(5)),
            Token);
        InspectionResponse second = (await booked.Content.ReadFromJsonAsync<ApplicationResponse>(Token))!.Inspections.Single(inspection => inspection.Status == "Scheduled");

        HttpResponseMessage response = await inspectorOne.PostAsJsonAsync(
            $"/api/v1/applications/{deck.Id}/inspections/{second.Id}/result", new InspectionResultRequest(true, null), Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("inspection.not_assigned", (await Problem.ReadAsync(response, Token)).Code);
    }
}

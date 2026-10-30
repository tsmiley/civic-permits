using System.Net;
using System.Text.Json;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Api.IntegrationTests;

public class PublicAndOpenApiTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<string> IssuedNumberAsync(SeededApi api)
    {
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        PagedResponse<ApplicationSummaryResponse> issued = (await clerk.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>("/api/v1/applications?status=Issued", Token))!;

        return issued.Items[0].Number!;
    }

    [Fact]
    public async Task A_sample_permit_can_be_looked_up_with_nothing_but_its_number()
    {
        using SeededApi api = new();
        string number = await IssuedNumberAsync(api);
        using HttpClient anonymous = api.Anonymous();

        PublicPermitStatusResponse? status = await anonymous.GetFromJsonAsync<PublicPermitStatusResponse>($"/api/v1/public/permits/{number}", Token);

        Assert.NotNull(status);
        Assert.Equal(number, status.Number);
        Assert.Equal("Issued", status.Status);
        Assert.NotNull(status.IssuedOn);
        Assert.NotNull(status.ExpiresOn);
    }

    [Fact]
    public async Task The_public_answer_has_exactly_the_five_fields_and_no_others()
    {
        using SeededApi api = new();
        string number = await IssuedNumberAsync(api);
        using HttpClient anonymous = api.Anonymous();

        using JsonDocument document = JsonDocument.Parse(await anonymous.GetStringAsync($"/api/v1/public/permits/{number}", Token));

        Assert.Equal(
            ["expiresOn", "issuedOn", "number", "permitTypeName", "status"],
            document.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("CP-2026-999999")]
    [InlineData("cp-2026-000001")]
    [InlineData("CP-2026-1")]
    [InlineData("CP-1999-000001")]
    [InlineData("CP-2026-000001%0A")]
    [InlineData("1%20OR%201=1")]
    [InlineData("not-a-number")]
    public async Task A_number_that_does_not_exist_and_one_that_is_malformed_get_the_same_answer(string number)
    {
        using SeededApi api = new();
        using HttpClient anonymous = api.Anonymous();

        HttpResponseMessage response = await anonymous.GetAsync($"/api/v1/public/permits/{number}", Token);
        Problem problem = await Problem.ReadAsync(response, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("permit.not_found", problem.Code);
        Assert.Equal("No permit was found with that number.", problem.Detail);
    }

    [Fact]
    public async Task A_draft_has_no_number_so_it_cannot_be_looked_up_by_guessing()
    {
        using SeededApi api = new();
        using HttpClient anonymous = api.Anonymous();

        // Sample application 1 is a draft. Whatever number is tried, it is not found.
        HttpResponseMessage response = await anonymous.GetAsync("/api/v1/public/permits/CP-2026-000001", Token);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });
        Assert.DoesNotContain("pergola", await response.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_lookup_is_rate_limited_per_client()
    {
        using SeededApi api = new(factory => factory.WithSetting("RateLimiting:PublicPerWindow", "4"));
        using HttpClient anonymous = api.Anonymous();

        for (int attempt = 0; attempt < 4; attempt++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/v1/public/permits/CP-2026-999999", Token)).StatusCode);
        }

        HttpResponseMessage limited = await anonymous.GetAsync("/api/v1/public/permits/CP-2026-999999", Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
    }

    [Fact]
    public async Task The_openapi_document_lists_the_endpoints_and_says_which_need_a_bearer_token()
    {
        using SeededApi api = new();
        using HttpClient anonymous = api.Anonymous();

        using JsonDocument document = JsonDocument.Parse(await anonymous.GetStringAsync("/openapi/v1.json", Token));
        JsonElement root = document.RootElement;
        JsonElement paths = root.GetProperty("paths");

        Assert.Equal("Civic Permits API", root.GetProperty("info").GetProperty("title").GetString());
        Assert.True(paths.TryGetProperty("/api/v1/applications", out JsonElement applications));
        Assert.True(paths.TryGetProperty("/api/v1/public/permits/{number}", out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/login", out JsonElement login));

        Assert.Equal("http", root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("type").GetString());
        Assert.True(applications.GetProperty("get").TryGetProperty("security", out JsonElement secured));
        Assert.Equal(JsonValueKind.Array, secured.ValueKind);
        Assert.False(login.GetProperty("post").TryGetProperty("security", out _));
    }

    [Fact]
    public async Task Every_operation_has_a_name_and_a_summary()
    {
        using SeededApi api = new();
        using HttpClient anonymous = api.Anonymous();

        using JsonDocument document = JsonDocument.Parse(await anonymous.GetStringAsync("/openapi/v1.json", Token));
        List<string> missing = [];

        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                if (!operation.Value.TryGetProperty("operationId", out _) || !operation.Value.TryGetProperty("summary", out _))
                {
                    missing.Add($"{operation.Name.ToUpperInvariant()} {path.Name}");
                }
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public async Task Responses_carry_the_security_headers_and_a_correlation_id()
    {
        using SeededApi api = new();
        using HttpClient anonymous = api.Anonymous();

        HttpResponseMessage response = await anonymous.GetAsync("/api/v1/permit-types", Token);

        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }
}

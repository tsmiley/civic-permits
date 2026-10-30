using System.Net.Http.Headers;
using System.Text.Json;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Api.IntegrationTests;

/// <summary>
/// The real API on a throwaway database with the sample town loaded and sample sign-ins created. The password is
/// made up fresh for every instance, so no credential exists in the repository for a test to depend on.
/// </summary>
internal sealed class SeededApi : IDisposable
{
    public const string ApplicantOne = "applicant.one@bramblewick.test";
    public const string ApplicantTwo = "applicant.two@bramblewick.test";
    public const string ApplicantThree = "applicant.three@bramblewick.test";
    public const string Clerk = "clerk@bramblewick.test";
    public const string Administrator = "administrator@bramblewick.test";
    public const string InspectorOne = "inspector.one@bramblewick.test";
    public const string InspectorTwo = "inspector.two@bramblewick.test";

    private readonly List<HttpClient> _clients = [];

    public SeededApi(Action<ApiFactory>? configure = null)
    {
        Factory = new ApiFactory()
            .WithSetting("Database:MigrateOnStartup", "true")
            .WithSetting("Database:SeedSampleData", "true")
            .WithSetting("Database:SampleUserPassword", Password)
            // Generous, so a test that signs in many times is not throttled by accident. Rate-limit tests lower it.
            .WithSetting("RateLimiting:AuthPerWindow", "1000");

        configure?.Invoke(Factory);
    }

    public ApiFactory Factory { get; }

    public string Password { get; } = $"Aa1-{Guid.NewGuid():N}";

    public HttpClient Anonymous()
    {
        HttpClient client = Factory.CreateClient();
        _clients.Add(client);

        return client;
    }

    public async Task<HttpClient> SignInAsync(string email, CancellationToken cancellationToken)
    {
        HttpClient client = Anonymous();
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password), cancellationToken);
        response.EnsureSuccessStatusCode();

        TokenResponse token = (await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        return client;
    }

    public void Dispose()
    {
        foreach (HttpClient client in _clients)
        {
            client.Dispose();
        }

        Factory.Dispose();
    }
}

/// <summary>A problem document, reduced to what a test asserts on.</summary>
internal sealed record Problem(int Status, string? Code, string? Title, string? Detail, string? CorrelationId)
{
    public static async Task<Problem> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        JsonElement root = document.RootElement;

        string? Text(string name) => root.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;

        return new Problem((int)response.StatusCode, Text("code"), Text("title"), Text("detail"), Text("correlationId"));
    }
}

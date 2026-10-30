using System.Net;
using System.Net.Http.Headers;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Api.IntegrationTests;

public class AuthTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_sample_user_signs_in_and_is_told_their_role()
    {
        using SeededApi api = new();
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);

        CurrentUserResponse? me = await clerk.GetFromJsonAsync<CurrentUserResponse>("/api/v1/auth/me", Token);

        Assert.NotNull(me);
        Assert.Equal("Clerk", me.Role);
        Assert.Equal("Sample Clerk", me.DisplayName);
    }

    [Fact]
    public async Task The_most_senior_role_wins_for_a_person_with_one()
    {
        using SeededApi api = new();
        using HttpClient admin = await api.SignInAsync(SeededApi.Administrator, Token);

        Assert.Equal("Administrator", (await admin.GetFromJsonAsync<CurrentUserResponse>("/api/v1/auth/me", Token))!.Role);
    }

    [Fact]
    public async Task A_wrong_password_an_unknown_address_and_a_missing_body_all_get_the_same_answer()
    {
        using SeededApi api = new();
        using HttpClient client = api.Anonymous();

        HttpResponseMessage wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(SeededApi.Clerk, "not-the-password-1A"), Token);
        HttpResponseMessage unknown = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("nobody@bramblewick.test", api.Password), Token);
        HttpResponseMessage empty = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(null, null), Token);

        Problem a = await Problem.ReadAsync(wrongPassword, Token);
        Problem b = await Problem.ReadAsync(unknown, Token);
        Problem c = await Problem.ReadAsync(empty, Token);

        Assert.Equal(401, a.Status);
        Assert.Equal((a.Status, a.Title, a.Detail), (b.Status, b.Title, b.Detail));
        Assert.Equal((a.Status, a.Title, a.Detail), (c.Status, c.Title, c.Detail));
        Assert.Equal("The email address or password is not right.", a.Detail);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_against_the_right_password()
    {
        using SeededApi api = new();
        using HttpClient client = api.Anonymous();

        for (int attempt = 0; attempt < 5; attempt++)
        {
            HttpResponseMessage wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(SeededApi.ApplicantOne, "wrong-password-1A"), Token);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        HttpResponseMessage correctButLocked = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(SeededApi.ApplicantOne, api.Password), Token);

        Assert.Equal(HttpStatusCode.Unauthorized, correctButLocked.StatusCode);
        Assert.Equal("The email address or password is not right.", (await Problem.ReadAsync(correctButLocked, Token)).Detail);
    }

    [Fact]
    public async Task A_refresh_token_gives_a_new_working_access_token_and_a_made_up_one_is_refused()
    {
        using SeededApi api = new();
        using HttpClient client = api.Anonymous();

        TokenResponse first = (await (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(SeededApi.ApplicantOne, api.Password), Token))
            .Content.ReadFromJsonAsync<TokenResponse>(Token))!;
        HttpResponseMessage refreshed = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(first.RefreshToken), Token);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        TokenResponse second = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>(Token))!;
        Assert.Equal("Bearer", second.TokenType);
        Assert.True(second.ExpiresIn is > 0 and <= 900);

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request, Token)).StatusCode);

        HttpResponseMessage bogus = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest("not-a-token"), Token);
        Assert.Equal(HttpStatusCode.Unauthorized, bogus.StatusCode);
    }

    [Fact]
    public async Task Calls_that_need_a_sign_in_are_refused_without_one_with_a_problem_document()
    {
        using SeededApi api = new();
        using HttpClient client = api.Anonymous();

        HttpResponseMessage response = await client.GetAsync("/api/v1/applications", Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(401, (await Problem.ReadAsync(response, Token)).Status);
    }

    [Fact]
    public async Task A_garbage_bearer_token_is_refused()
    {
        using SeededApi api = new();
        using HttpClient client = api.Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "CfDJ8-this-is-not-a-real-token");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me", Token)).StatusCode);
    }

    [Fact]
    public async Task Someone_can_register_as_an_applicant_and_then_sign_in()
    {
        using SeededApi api = new();
        using HttpClient client = api.Anonymous();
        string password = $"Zz9-{Guid.NewGuid():N}";

        HttpResponseMessage registered = await client.PostAsJsonAsync(
            "/api/v1/auth/register", new RegisterRequest("new.person@bramblewick.test", password, "New Person"), Token);
        HttpResponseMessage signedIn = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("new.person@bramblewick.test", password), Token);

        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);

        TokenResponse token = (await signedIn.Content.ReadFromJsonAsync<TokenResponse>(Token))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        Assert.Equal("Applicant", (await client.GetFromJsonAsync<CurrentUserResponse>("/api/v1/auth/me", Token))!.Role);
    }

    [Fact]
    public async Task Registering_an_address_that_exists_looks_exactly_like_registering_a_new_one()
    {
        using SeededApi api = new();
        using HttpClient client = api.Anonymous();

        HttpResponseMessage fresh = await client.PostAsJsonAsync(
            "/api/v1/auth/register", new RegisterRequest("someone.else@bramblewick.test", $"Qq7-{Guid.NewGuid():N}", "Someone Else"), Token);
        HttpResponseMessage taken = await client.PostAsJsonAsync(
            "/api/v1/auth/register", new RegisterRequest(SeededApi.Clerk, $"Qq7-{Guid.NewGuid():N}", "Impostor"), Token);

        Assert.Equal(HttpStatusCode.Accepted, fresh.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, taken.StatusCode);
        Assert.Equal(await fresh.Content.ReadAsStringAsync(Token), await taken.Content.ReadAsStringAsync(Token));

        // And the existing account is unchanged: the impostor's password does not work.
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        Assert.Equal("Clerk", (await clerk.GetFromJsonAsync<CurrentUserResponse>("/api/v1/auth/me", Token))!.Role);
    }

    [Theory]
    [InlineData("not-an-email", "Aa1-longenoughpassword", "Valid Name", "email")]
    [InlineData("ok@bramblewick.test", "short1A", "Valid Name", "password")]
    [InlineData("ok@bramblewick.test", "fourteen-char1", "Valid Name", "password")]
    [InlineData("ok@bramblewick.test", "Aa1-longenoughpassword", "X", "displayName")]
    public async Task A_registration_with_a_bad_value_is_refused_and_says_which(string email, string password, string name, string field)
    {
        using SeededApi api = new();
        using HttpClient client = api.Anonymous();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, name), Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains($"\"{field}\"", await response.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_endpoints_anyone_can_reach_are_rate_limited_with_a_retry_hint()
    {
        using SeededApi api = new(factory => factory.WithSetting("RateLimiting:AuthPerWindow", "3"));
        using HttpClient client = api.Anonymous();
        LoginRequest wrong = new(SeededApi.ApplicantTwo, "wrong-password-1A");

        for (int attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", wrong, Token)).StatusCode);
        }

        HttpResponseMessage limited = await client.PostAsJsonAsync("/api/v1/auth/login", wrong, Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta is { TotalSeconds: > 0 });
        Assert.Equal(429, (await Problem.ReadAsync(limited, Token)).Status);
    }
}

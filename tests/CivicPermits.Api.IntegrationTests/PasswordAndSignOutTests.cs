using System.Net;
using System.Net.Http.Headers;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Api.IntegrationTests;

/// <summary>The password policy as the API enforces it, and the sign-out that ends refresh tokens.</summary>
public class PasswordAndSignOutTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ---- The policy at the API ----

    [Theory]
    [InlineData("password123456789")]
    [InlineData("correcthorsebatterystaple")]
    [InlineData("Qwerty-Qwerty-Qwerty-9")]
    public async Task A_listed_password_is_refused_on_registration_with_a_422_naming_the_password_field(string password)
    {
        using SeededApi api = new();
        using HttpClient anonymous = api.Anonymous();

        HttpResponseMessage response = await anonymous.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("new.person@bramblewick.test", password, "New Person"), Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(Token);
        Assert.Contains("\"password\"", body, StringComparison.Ordinal);
        Assert.Contains("first that people guess", body, StringComparison.Ordinal);
        Assert.DoesNotContain(password, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fourteen_characters_are_refused_and_fifteen_of_one_kind_are_accepted_on_registration()
    {
        using SeededApi api = new();
        using HttpClient anonymous = api.Anonymous();

        HttpResponseMessage shortOne = await anonymous.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("new.person@bramblewick.test", "vkxjqpzmwnbthy", "New Person"), Token);
        HttpResponseMessage fifteen = await anonymous.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("new.person@bramblewick.test", "vkxjqpzmwnbtyhd", "New Person"), Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, shortOne.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, fifteen.StatusCode);

        // And the new account can sign in with it.
        HttpResponseMessage login = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("new.person@bramblewick.test", "vkxjqpzmwnbtyhd"), Token);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    // ---- Sign-out ----

    private static async Task<TokenResponse> LoginAsync(SeededApi api, HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(SeededApi.ApplicantOne, api.Password), Token);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Token))!;
    }

    [Fact]
    public async Task After_signing_out_the_refresh_token_made_before_it_no_longer_works_and_signing_in_again_does()
    {
        using SeededApi api = new();
        using HttpClient client = api.Anonymous();
        TokenResponse before = await LoginAsync(api, client);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(before.RefreshToken), Token)).StatusCode);

        using HttpRequestMessage signOut = new(HttpMethod.Post, "/api/v1/auth/sign-out");
        signOut.Headers.Authorization = new AuthenticationHeaderValue("Bearer", before.AccessToken);
        HttpResponseMessage ended = await client.SendAsync(signOut, Token);

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        HttpResponseMessage refused = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(before.RefreshToken), Token);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        // A fresh sign-in gets a working refresh token again.
        TokenResponse after = await LoginAsync(api, client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(after.RefreshToken), Token)).StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_refresh_tokens_of_that_account_only()
    {
        using SeededApi api = new();
        using HttpClient client = api.Anonymous();
        TokenResponse first = await LoginAsync(api, client);
        HttpResponseMessage otherLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(SeededApi.ApplicantTwo, api.Password), Token);
        TokenResponse other = (await otherLogin.Content.ReadFromJsonAsync<TokenResponse>(Token))!;

        using HttpRequestMessage signOut = new(HttpMethod.Post, "/api/v1/auth/sign-out");
        signOut.Headers.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        await client.SendAsync(signOut, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(first.RefreshToken), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(other.RefreshToken), Token)).StatusCode);
    }

    [Fact]
    public async Task Signing_out_needs_a_sign_in()
    {
        using SeededApi api = new();
        using HttpClient anonymous = api.Anonymous();

        HttpResponseMessage response = await anonymous.PostAsync("/api/v1/auth/sign-out", content: null, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

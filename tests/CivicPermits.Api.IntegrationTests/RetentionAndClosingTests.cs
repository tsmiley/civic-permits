using System.Net;
using System.Net.Http.Headers;
using CivicPermits.Infrastructure.Persistence;
using CivicPermits.Infrastructure.Persistence.Seeding;
using CivicPermits.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicPermits.Api.IntegrationTests;

/// <summary>The administrator's retention purge and the applicant's closing of their own account, through the API.</summary>
public class RetentionAndClosingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ---- The purge ----

    [Fact]
    public async Task Only_an_administrator_can_run_the_purge()
    {
        using SeededApi api = new();
        using HttpClient administrator = await api.SignInAsync(SeededApi.Administrator, Token);
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        using HttpClient anonymous = api.Anonymous();

        Assert.Equal(HttpStatusCode.OK, (await administrator.PostAsync("/api/v1/retention/purge?dryRun=true", content: null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PostAsync("/api/v1/retention/purge", content: null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await applicant.PostAsync("/api/v1/retention/purge", content: null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/v1/retention/purge", content: null, Token)).StatusCode);
    }

    [Fact]
    public async Task On_fresh_sample_data_nothing_is_old_enough_and_a_real_run_records_that_it_ran()
    {
        using SeededApi api = new();
        using HttpClient administrator = await api.SignInAsync(SeededApi.Administrator, Token);

        RetentionResponse dry = (await (await administrator.PostAsync("/api/v1/retention/purge?dryRun=true", content: null, Token)).Content.ReadFromJsonAsync<RetentionResponse>(Token))!;
        PagedResponse<AuditEntryResponse> afterDry = (await administrator.GetFromJsonAsync<PagedResponse<AuditEntryResponse>>("/api/v1/audit", Token))!;
        RetentionResponse real = (await (await administrator.PostAsync("/api/v1/retention/purge", content: null, Token)).Content.ReadFromJsonAsync<RetentionResponse>(Token))!;
        PagedResponse<AuditEntryResponse> afterReal = (await administrator.GetFromJsonAsync<PagedResponse<AuditEntryResponse>>("/api/v1/audit", Token))!;

        Assert.Equal(new RetentionResponse(true, 0, 0, 0, 0), dry);
        Assert.Equal(new RetentionResponse(false, 0, 0, 0, 0), real);
        Assert.Empty(afterDry.Items); // a dry run is not recorded
        AuditEntryResponse entry = Assert.Single(afterReal.Items);
        Assert.Equal("retention.purged", entry.Action);
        Assert.Equal(SampleIdentities.Administrator, entry.ActorId);
        Assert.Contains("0 decided", entry.Summary, StringComparison.Ordinal);
    }

    // ---- Closing an account ----

    private static async Task<(HttpClient Client, TokenResponse Token)> SignInWithTokenAsync(SeededApi api, string email)
    {
        HttpClient client = api.Anonymous();
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, api.Password), Token);
        response.EnsureSuccessStatusCode();
        TokenResponse token = (await response.Content.ReadFromJsonAsync<TokenResponse>(Token))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        return (client, token);
    }

    [Fact]
    public async Task A_wrong_password_closes_nothing()
    {
        using SeededApi api = new();
        (HttpClient applicant, _) = await SignInWithTokenAsync(api, SeededApi.ApplicantOne);
        using HttpClient client = applicant;

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/close-account", new CloseAccountRequest("not-the-password-1A"), Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("\"password\"", await response.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me", Token)).StatusCode);
        using HttpClient again = api.Anonymous();
        Assert.Equal(HttpStatusCode.OK, (await again.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(SeededApi.ApplicantOne, api.Password), Token)).StatusCode);
    }

    [Fact]
    public async Task Closing_removes_the_name_and_email_ends_the_refresh_token_stops_sign_in_and_keeps_the_applications()
    {
        using SeededApi api = new();
        (HttpClient applicant, TokenResponse token) = await SignInWithTokenAsync(api, SeededApi.ApplicantOne);
        using HttpClient client = applicant;
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        int applicationsBefore = (await clerk.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>("/api/v1/applications?pageSize=100", Token))!.TotalCount;

        HttpResponseMessage closed = await client.PostAsJsonAsync("/api/v1/auth/close-account", new CloseAccountRequest(api.Password), Token);

        Assert.Equal(HttpStatusCode.NoContent, closed.StatusCode);

        // It cannot sign in again, and the refresh token it held is dead.
        using HttpClient later = api.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await later.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(SeededApi.ApplicantOne, api.Password), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await later.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(token.RefreshToken), Token)).StatusCode);

        // Nothing of the person is left on the account, and the audit record says it was closed.
        await using (AsyncServiceScope scope = api.Factory.Services.CreateAsyncScope())
        {
            CivicPermitsDbContext database = scope.ServiceProvider.GetRequiredService<CivicPermitsDbContext>();
            ApplicationUser account = await database.Users.SingleAsync(user => user.Id == SampleIdentities.ApplicantOne, Token);

            Assert.Equal(string.Empty, account.DisplayName);
            Assert.DoesNotContain("applicant.one", account.Email!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("applicant.one", account.UserName!, StringComparison.OrdinalIgnoreCase);
            Assert.Null(account.PasswordHash);
        }

        // Every application is still there, for the staff who need them.
        Assert.Equal(applicationsBefore, (await clerk.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>("/api/v1/applications?pageSize=100", Token))!.TotalCount);
        using HttpClient administrator = await api.SignInAsync(SeededApi.Administrator, Token);
        PagedResponse<AuditEntryResponse> audit = (await administrator.GetFromJsonAsync<PagedResponse<AuditEntryResponse>>("/api/v1/audit", Token))!;
        AuditEntryResponse entry = Assert.Single(audit.Items);
        Assert.Equal("account.closed", entry.Action);
        Assert.Equal(SampleIdentities.ApplicantOne, entry.SubjectId);
        Assert.Equal("(closed account)", entry.ActorName);
    }

    [Fact]
    public async Task Another_person_cannot_close_an_account_and_staff_cannot_use_the_route()
    {
        using SeededApi api = new();
        (HttpClient two, _) = await SignInWithTokenAsync(api, SeededApi.ApplicantTwo);
        using HttpClient applicantTwo = two;
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        using HttpClient anonymous = api.Anonymous();

        // The route acts on the signed-in account only: B has no way to name A's. B's own closing needs B's password.
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PostAsJsonAsync("/api/v1/auth/close-account", new CloseAccountRequest(api.Password), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/auth/close-account", new CloseAccountRequest(api.Password), Token)).StatusCode);

        using HttpClient stillOne = api.Anonymous();
        Assert.Equal(HttpStatusCode.OK, (await stillOne.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(SeededApi.ApplicantOne, api.Password), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await applicantTwo.GetAsync("/api/v1/auth/me", Token)).StatusCode);
    }

    [Fact]
    public async Task Signing_in_notes_when_it_happened_for_the_retention_period()
    {
        using SeededApi api = new();
        using HttpClient client = api.Anonymous();
        DateTimeOffset before = DateTimeOffset.UtcNow.AddSeconds(-5);

        await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(SeededApi.ApplicantThree, api.Password), Token);

        await using AsyncServiceScope scope = api.Factory.Services.CreateAsyncScope();
        CivicPermitsDbContext database = scope.ServiceProvider.GetRequiredService<CivicPermitsDbContext>();
        ApplicationUser person = await database.Users.SingleAsync(user => user.Id == SampleIdentities.ApplicantThree, Token);
        ApplicationUser untouched = await database.Users.SingleAsync(user => user.Id == SampleIdentities.ApplicantOne, Token);

        Assert.NotNull(person.LastSignInOn);
        Assert.InRange(person.LastSignInOn.Value, before, DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.NotNull(person.CreatedOn); // sample accounts are stamped when they are made
        Assert.Null(untouched.LastSignInOn);
    }

    [Fact]
    public async Task Registering_notes_when_the_account_was_made()
    {
        using SeededApi api = new();
        using HttpClient client = api.Anonymous();

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("made.today@bramblewick.test", "vkxjqpzmwnbtyhd", "Made Today"), Token);

        await using AsyncServiceScope scope = api.Factory.Services.CreateAsyncScope();
        ApplicationUser person = await scope.ServiceProvider.GetRequiredService<CivicPermitsDbContext>().Users.SingleAsync(user => user.Email == "made.today@bramblewick.test", Token);

        Assert.NotNull(person.CreatedOn);
        Assert.Null(person.LastSignInOn);
    }
}

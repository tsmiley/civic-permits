using System.Net;
using CivicPermits.Infrastructure.Persistence;
using CivicPermits.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace CivicPermits.Web.IntegrationTests;

/// <summary>Closing an account on the site: who may, what it asks, what it does and what it leaves behind.</summary>
public class AccountClosingPageTests
{
    private const string Applicant = "applicant.two@bramblewick.test";
    private const string Clerk = "clerk@bramblewick.test";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_a_signed_in_applicant_is_offered_the_page_and_it_is_never_kept()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient applicant = factory.Client();
        using HttpClient clerk = factory.Client();
        using HttpClient anonymous = factory.Client();
        await Forms.SignInAsync(applicant, Applicant, factory.Password, Token);
        await Forms.SignInAsync(clerk, Clerk, factory.Password, Token);

        HttpResponseMessage page = await applicant.GetAsync("/account/close", Token);
        HttpResponseMessage staff = await clerk.GetAsync("/account/close", Token);
        HttpResponseMessage signedOut = await anonymous.GetAsync("/account/close", Token);
        string body = await page.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Close my account", body, StringComparison.Ordinal);
        Assert.Contains("cannot be undone", body, StringComparison.Ordinal);
        Assert.Equal("no-store", page.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.Redirect, staff.StatusCode);
        Assert.StartsWith("/account/denied", staff.Headers.Location!.PathAndQuery, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.StartsWith("/account/login", signedOut.Headers.Location!.PathAndQuery, StringComparison.Ordinal);

        // Only an applicant is shown the link.
        Assert.Contains("href=\"/account/close\"", await (await applicant.GetAsync("/applications", Token)).Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/account/close\"", await (await clerk.GetAsync("/applications", Token)).Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_password_closes_nothing_and_says_so_next_to_the_field()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient client = factory.Client();
        await Forms.SignInAsync(client, Applicant, factory.Password, Token);
        string html = await Forms.GetHtmlAsync(client, "/account/close", Token);

        HttpResponseMessage response = await Forms.PostAsync(client, "/account/close", html, "close-account", new() { ["password"] = "not-the-password-1A" }, Token);
        string body = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The password is not right.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("not-the-password-1A", body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/applications", Token)).StatusCode); // still signed in
        using HttpClient again = factory.Client();
        Assert.Equal(HttpStatusCode.Redirect, (await Forms.SignInAsync(again, Applicant, factory.Password, Token)).StatusCode);
    }

    [Fact]
    public async Task Closing_signs_the_person_out_clears_what_the_browser_holds_and_the_account_can_never_sign_in_again()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient client = factory.Client();
        await Forms.SignInAsync(client, Applicant, factory.Password, Token);
        string html = await Forms.GetHtmlAsync(client, "/account/close", Token);

        HttpResponseMessage response = await Forms.PostAsync(client, "/account/close", html, "close-account", new() { ["password"] = factory.Password }, Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/closed", response.Headers.Location!.PathAndQuery, StringComparison.Ordinal);
        Assert.Equal("\"cache\", \"cookies\", \"storage\"", response.Headers.GetValues("Clear-Site-Data").Single());

        // The session ended: a signed-in page now sends them to sign in.
        HttpResponseMessage after = await client.GetAsync("/applications", Token);
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.StartsWith("/account/login", after.Headers.Location!.PathAndQuery, StringComparison.Ordinal);

        // The confirmation page is public and says what happened.
        using HttpClient anonymous = factory.Client();
        string closed = await (await anonymous.GetAsync("/account/closed", Token)).Content.ReadAsStringAsync(Token);
        Assert.Contains("Your account is closed", closed, StringComparison.Ordinal);

        // The old credentials no longer work, and nothing of the person is left on the account.
        using HttpClient later = factory.Client();
        HttpResponseMessage again = await Forms.SignInAsync(later, Applicant, factory.Password, Token);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode); // the sign-in page again, with its one message
        Assert.Contains("The email address or password is not right.", await again.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationUser account = await scope.ServiceProvider.GetRequiredService<CivicPermitsDbContext>().Users.SingleAsync(user => user.Id == SampleIdentities.ApplicantTwo, Token);
        Assert.Equal(string.Empty, account.DisplayName);
        Assert.DoesNotContain("applicant.two", account.Email!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Closing_needs_the_anti_forgery_token_so_another_site_cannot_do_it()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient client = factory.Client();
        await Forms.SignInAsync(client, Applicant, factory.Password, Token);

        using FormUrlEncodedContent forged = new(new Dictionary<string, string> { ["_handler"] = "close-account", ["password"] = factory.Password });
        HttpResponseMessage response = await client.PostAsync("/account/close", forged, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/applications", Token)).StatusCode);
    }

    [Fact]
    public async Task Signing_in_on_the_site_notes_when_it_happened()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient client = factory.Client();
        DateTimeOffset before = DateTimeOffset.UtcNow.AddSeconds(-5);

        await Forms.SignInAsync(client, Applicant, factory.Password, Token);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationUser person = await scope.ServiceProvider.GetRequiredService<CivicPermitsDbContext>().Users.SingleAsync(user => user.Id == SampleIdentities.ApplicantTwo, Token);

        Assert.NotNull(person.LastSignInOn);
        Assert.InRange(person.LastSignInOn.Value, before, DateTimeOffset.UtcNow.AddSeconds(5));
    }
}

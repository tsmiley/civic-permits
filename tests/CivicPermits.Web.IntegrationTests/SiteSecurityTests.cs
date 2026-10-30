using System.Net;
using CivicPermits.Infrastructure.Hosting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CivicPermits.Web.IntegrationTests;

/// <summary>The web site's cookies, sign-in form, page gates, headers and caching, checked in memory against the real pipeline.</summary>
public class SiteSecurityTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Where a redirect goes, as a path and query: the framework sends it as an absolute address.</summary>
    private static string PathOf(HttpResponseMessage response) =>
        response.Headers.Location is { IsAbsoluteUri: true } absolute ? absolute.PathAndQuery : response.Headers.Location?.OriginalString ?? string.Empty;

    private const string SampleClerk = "clerk@bramblewick.test";
    private const string SampleApplicant = "applicant.two@bramblewick.test";

    // ---- Cookies ----

    [Fact]
    public void The_session_and_anti_forgery_cookies_are_configured_as_documented()
    {
        using WebFactory factory = new();
        CookieAuthenticationOptions session = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        AntiforgeryOptions antiforgery = factory.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;

        Assert.Equal("cp.session", session.Cookie.Name);
        Assert.True(session.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, session.Cookie.SameSite);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, session.Cookie.SecurePolicy);
        Assert.Equal(TimeSpan.FromHours(8), session.ExpireTimeSpan);
        Assert.True(session.SlidingExpiration);
        Assert.Equal("/account/login", session.LoginPath.Value);

        Assert.Equal("cp.antiforgery", antiforgery.Cookie.Name);
        Assert.True(antiforgery.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, antiforgery.Cookie.SameSite);
    }

    [Fact]
    public async Task In_production_the_cookies_are_only_ever_sent_over_https()
    {
        using WebFactory factory = new WebFactory().InEnvironment("Production");
        string password = await factory.RegisterApplicantAsync("someone@bramblewick.test");
        using HttpClient client = factory.Client(new Uri("https://permits.example"));

        HttpResponseMessage response = await Forms.SignInAsync(client, "someone@bramblewick.test", password, Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        string session = response.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith("cp.session=", StringComparison.Ordinal));
        Assert.Contains("secure", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", session, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("max-age=63072000; includeSubDomains", response.Headers.GetValues("Strict-Transport-Security").Single());
    }

    [Fact]
    public async Task Signing_in_sets_an_http_only_same_site_session_cookie_and_goes_where_it_was_asked_on_the_site()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient client = factory.Client();

        HttpResponseMessage response = await Forms.SignInAsync(client, SampleApplicant, factory.Password, Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/applications", PathOf(response));
        string session = response.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith("cp.session=", StringComparison.Ordinal));
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", session, StringComparison.OrdinalIgnoreCase);
    }

    // ---- The sign-in form ----

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_against_the_right_password()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient client = factory.Client();

        for (int attempt = 0; attempt < 5; attempt++)
        {
            HttpResponseMessage wrong = await Forms.SignInAsync(client, SampleApplicant, "not-the-password-1A", Token);

            Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
            Assert.Contains("The email address or password is not right.", await wrong.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        }

        HttpResponseMessage rightButLocked = await Forms.SignInAsync(client, SampleApplicant, factory.Password, Token);

        Assert.Equal(HttpStatusCode.OK, rightButLocked.StatusCode);
        Assert.Contains("The email address or password is not right.", await rightButLocked.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.False(rightButLocked.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies) && cookies.Any(cookie => cookie.StartsWith("cp.session=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_address_get_the_same_page()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient client = factory.Client();

        string wrongPassword = await (await Forms.SignInAsync(client, SampleApplicant, "not-the-password-1A", Token)).Content.ReadAsStringAsync(Token);
        string unknownAddress = await (await Forms.SignInAsync(client, "nobody@bramblewick.test", "not-the-password-1A", Token)).Content.ReadAsStringAsync(Token);

        static string Message(string html) => System.Text.RegularExpressions.Regex.Match(html, "<ul>\\s*<li>.*?</li>", System.Text.RegularExpressions.RegexOptions.Singleline, TimeSpan.FromSeconds(1)).Value;

        Assert.Contains("The email address or password is not right.", Message(wrongPassword), StringComparison.Ordinal);
        Assert.Equal(Message(wrongPassword).Replace(SampleApplicant, "X", StringComparison.Ordinal), Message(unknownAddress).Replace("nobody@bramblewick.test", "X", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_sign_in_form_is_rate_limited_per_address()
    {
        using WebFactory factory = new WebFactory().WithSampleData().WithSetting("RateLimiting:AuthPerWindow", "4");
        using HttpClient client = factory.Client();
        string html = await Forms.GetHtmlAsync(client, "/account/login", Token);

        HttpStatusCode last = HttpStatusCode.OK;

        for (int attempt = 0; attempt < 4; attempt++)
        {
            HttpResponseMessage response = await Forms.PostAsync(client, "/account/login", html, "login", new() { ["email"] = SampleApplicant, ["password"] = "not-the-password-1A" }, Token);
            last = response.StatusCode;

            if (last == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task The_register_form_is_rate_limited_per_address_like_the_sign_in_form()
    {
        using WebFactory factory = new WebFactory().WithSetting("RateLimiting:AuthPerWindow", "4");
        using HttpClient client = factory.Client();
        string html = await Forms.GetHtmlAsync(client, "/account/register", Token);

        HttpStatusCode last = HttpStatusCode.OK;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            // An empty form is refused for its content (a 200 with the errors) until the limiter steps in.
            HttpResponseMessage response = await Forms.PostAsync(client, "/account/register", html, "register", new() { ["name"] = string.Empty, ["email"] = string.Empty, ["password"] = string.Empty }, Token);
            last = response.StatusCode;

            if (last == HttpStatusCode.TooManyRequests)
            {
                break;
            }

            Assert.Equal(HttpStatusCode.OK, last);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Theory]
    [InlineData("password123456789")]
    [InlineData("Correct Horse Battery Staple")]
    public async Task The_register_form_refuses_a_listed_password_and_says_why_without_echoing_it(string password)
    {
        using WebFactory factory = new();
        using HttpClient client = factory.Client();
        string html = await Forms.GetHtmlAsync(client, "/account/register", Token);

        HttpResponseMessage response = await Forms.PostAsync(client, "/account/register", html, "register", new() { ["name"] = "New Person", ["email"] = "new.person@bramblewick.test", ["password"] = password }, Token);
        string body = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("first that people guess", body, StringComparison.Ordinal);
        Assert.DoesNotContain(password, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_register_form_accepts_fifteen_characters_of_one_kind_and_the_new_account_can_sign_in()
    {
        using WebFactory factory = new();
        using HttpClient client = factory.Client();
        string html = await Forms.GetHtmlAsync(client, "/account/register", Token);

        HttpResponseMessage refused = await Forms.PostAsync(client, "/account/register", html, "register", new() { ["name"] = "New Person", ["email"] = "new.person@bramblewick.test", ["password"] = "vkxjqpzmwnbthy" }, Token);
        HttpResponseMessage accepted = await Forms.PostAsync(client, "/account/register", html, "register", new() { ["name"] = "New Person", ["email"] = "new.person@bramblewick.test", ["password"] = "vkxjqpzmwnbtyhd" }, Token);

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("15 characters", await refused.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.StartsWith("/account/login", PathOf(accepted), StringComparison.Ordinal);

        HttpResponseMessage signedIn = await Forms.SignInAsync(client, "new.person@bramblewick.test", "vkxjqpzmwnbtyhd", Token);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
    }

    [Fact]
    public async Task Signing_in_cannot_be_used_to_send_someone_to_another_site()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient client = factory.Client();
        string html = await Forms.GetHtmlAsync(client, "/account/login?returnUrl=" + Uri.EscapeDataString("//evil.example/"), Token);

        HttpResponseMessage response = await Forms.PostAsync(client, "/account/login", html, "login",
            new() { ["email"] = SampleApplicant, ["password"] = factory.Password }, Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/applications", PathOf(response));
    }

    // ---- Page gates ----

    [Fact]
    public async Task A_signed_out_person_asking_for_an_application_is_sent_to_sign_in_with_the_way_back()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient client = factory.Client();
        Guid id = Guid.NewGuid();

        HttpResponseMessage response = await client.GetAsync($"/applications/{id}", Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/login?ReturnUrl=", PathOf(response), StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString($"/applications/{id}"), PathOf(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_clerk_who_asks_for_the_permit_type_editor_gets_a_real_403_and_a_signed_out_person_is_sent_to_sign_in()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient clerk = factory.Client();
        using HttpClient signedOut = factory.Client();
        await Forms.SignInAsync(clerk, SampleClerk, factory.Password, Token);
        string path = $"/admin/permit-types/{Guid.Parse("b1000000-0000-4000-8000-000000000001")}";

        HttpResponseMessage redirected = await signedOut.GetAsync(path, Token);

        // A signed-in person without the role is sent to the access-denied page, which answers 403 and shows nothing of the page asked for.
        foreach (string asked in new[] { path, "/admin/permit-types/new", "/admin/permit-types" })
        {
            HttpResponseMessage first = await clerk.GetAsync(asked, Token);
            Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
            Assert.StartsWith("/account/denied", PathOf(first), StringComparison.Ordinal);

            HttpResponseMessage denied = await clerk.GetAsync(PathOf(first), Token);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.DoesNotContain("Residential building permit", await denied.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        }

        Assert.Equal(HttpStatusCode.Redirect, redirected.StatusCode);
        Assert.StartsWith("/account/login", PathOf(redirected), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_applicant_cannot_open_someone_elses_application_and_the_answer_is_a_plain_404()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient applicant = factory.Client();
        await Forms.SignInAsync(applicant, SampleApplicant, factory.Password, Token);

        // Sample application 1 (a draft) belongs to applicant one, not applicant two.
        string list = await Forms.GetHtmlAsync(applicant, "/applications", Token);
        Guid mine = Guid.Parse(System.Text.RegularExpressions.Regex.Match(list, "/applications/([0-9a-f-]{36})", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value);
        Guid someoneElses = Guid.Parse("f1000000-0000-4000-8000-000000000001");

        Assert.Equal(HttpStatusCode.OK, (await applicant.GetAsync($"/applications/{mine}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await applicant.GetAsync($"/applications/{someoneElses}", Token)).StatusCode);
    }

    // ---- Headers and caching ----

    private static readonly string[] RulebookHeaders =
    [
        "Content-Security-Policy", "X-Content-Type-Options", "X-Frame-Options", "Referrer-Policy", "Permissions-Policy",
        "Cross-Origin-Opener-Policy", "Cross-Origin-Resource-Policy", "X-Permitted-Cross-Domain-Policies", "X-Correlation-Id",
    ];

    private static void AssertHeaders(HttpResponseMessage response, string where)
    {
        foreach (string header in RulebookHeaders)
        {
            Assert.True(response.Headers.Contains(header), $"{where}: the {header} header is missing.");
        }

        Assert.Equal(SecurityHeadersPolicy.ForWebSite.ContentSecurityPolicy, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("same-site", response.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.Equal("none", response.Headers.GetValues("X-Permitted-Cross-Domain-Policies").Single());
        Assert.False(response.Headers.Contains("Server"), $"{where}: Server should not be sent.");
        Assert.False(response.Headers.Contains("X-Powered-By"), $"{where}: X-Powered-By should not be sent.");
    }

    [Fact]
    public void The_policy_the_site_sends_is_the_one_documented_and_forbids_inline_script_and_style()
    {
        Assert.Equal(
            "default-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'",
            SecurityHeadersPolicy.ForWebSite.ContentSecurityPolicy);
        Assert.DoesNotContain("unsafe-inline", SecurityHeadersPolicy.ForWebSite.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", SecurityHeadersPolicy.ForWebSite.ContentSecurityPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_rulebook_header_is_on_a_page_a_redirect_a_refusal_a_miss_and_a_static_file()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient anonymous = factory.Client();
        using HttpClient clerk = factory.Client();
        await Forms.SignInAsync(clerk, SampleClerk, factory.Password, Token);

        HttpResponseMessage home = await anonymous.GetAsync("/", Token);
        AssertHeaders(home, "home page");
        AssertHeaders(await anonymous.GetAsync("/applications", Token), "redirect to sign in");
        AssertHeaders(await clerk.GetAsync("/admin/permit-types", Token), "403 refusal");
        AssertHeaders(await anonymous.GetAsync("/no-such-page", Token), "404 page");

        string html = await home.Content.ReadAsStringAsync(Token);
        string stylesheet = System.Text.RegularExpressions.Regex.Match(html, "href=\"([^\"]*\\.css)\"", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;
        HttpResponseMessage css = await anonymous.GetAsync(stylesheet, Token);

        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        AssertHeaders(css, "static stylesheet");
    }

    [Fact]
    public async Task Pages_a_signed_in_person_receives_and_the_account_pages_are_never_kept_but_public_pages_and_files_may_be()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient anonymous = factory.Client();
        using HttpClient applicant = factory.Client();
        await Forms.SignInAsync(applicant, SampleApplicant, factory.Password, Token);

        Assert.Equal("no-store", (await applicant.GetAsync("/applications", Token)).Headers.CacheControl?.ToString());
        Assert.Equal("no-store", (await applicant.GetAsync("/permits", Token)).Headers.CacheControl?.ToString());
        Assert.Equal("no-store", (await anonymous.GetAsync("/account/login", Token)).Headers.CacheControl?.ToString());
        Assert.Equal("no-store", (await anonymous.GetAsync("/account/register", Token)).Headers.CacheControl?.ToString());
        Assert.NotEqual("no-store", (await anonymous.GetAsync("/permits", Token)).Headers.CacheControl?.ToString());

        string html = await Forms.GetHtmlAsync(anonymous, "/", Token);
        string stylesheet = System.Text.RegularExpressions.Regex.Match(html, "href=\"([^\"]*\\.css)\"", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;

        Assert.NotEqual("no-store", (await applicant.GetAsync(stylesheet, Token)).Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Signing_out_tells_the_browser_to_forget_the_site_and_ends_the_session()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient client = factory.Client();
        await Forms.SignInAsync(client, SampleApplicant, factory.Password, Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/applications", Token)).StatusCode);

        string html = await Forms.GetHtmlAsync(client, "/account/logout", Token);
        HttpResponseMessage response = await Forms.PostAsync(client, "/account/logout", html, "logout", [], Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("\"cache\", \"cookies\", \"storage\"", response.Headers.GetValues("Clear-Site-Data").Single());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/applications", Token)).StatusCode);
    }

    [Fact]
    public async Task Signing_out_needs_the_anti_forgery_token_so_another_site_cannot_do_it()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient client = factory.Client();
        await Forms.SignInAsync(client, SampleApplicant, factory.Password, Token);

        using FormUrlEncodedContent withoutToken = new(new Dictionary<string, string> { ["_handler"] = "logout" });
        HttpResponseMessage response = await client.PostAsync("/account/logout", withoutToken, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/applications", Token)).StatusCode);
    }
}

using Microsoft.Playwright;

namespace CivicPermits.Web.A11yTests;

/// <summary>
/// The axe-core sweep: the 53 page states listed in docs/ACCESSIBILITY_REPORT.md (the pages of the site in the states
/// and as the kinds of person named there). A test collects the violations from all its states and reports them together.
/// </summary>
/// <remarks>
/// Passing means axe found nothing it can find. It does not mean the site is accessible: automated rules reach a
/// minority of the success criteria. See docs/ACCESSIBILITY_REPORT.md for what else was done and what remains.
/// </remarks>
public class AxeSweepTests(SiteFixture site) : IClassFixture<SiteFixture>
{
    private static async Task SubmitAsync(IPage page, string buttonName) =>
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = buttonName }).ClickAsync();

    /// <summary>
    /// A sweep that can never fail proves nothing. This puts known defects into a real page and requires axe to find
    /// every one, so a clean result elsewhere means something.
    /// </summary>
    [Fact]
    public async Task The_sweep_does_find_defects_when_there_are_some()
    {
        await using SitePage visitor = await SitePage.OpenAsync(site);
        await visitor.GoAsync("/account/login");

        // Two of the defects are chosen so that only one tag finds each: contrast is a WCAG 2.0 AA rule and an invalid
        // autocomplete token is a WCAG 2.1 AA rule, so dropping either tag from the sweep makes this test fail.
        // The faint colours are set through the object model because the page's policy forbids inline style attributes.
        await visitor.Page.EvaluateAsync(
            """
            () => {
              document.querySelector('main').insertAdjacentHTML('beforeend',
                '<img src="data:image/gif;base64,R0lGODlhAQABAAAAACw=">'
                + '<button type="button"></button>'
                + '<input type="text" id="unlabelled-canary">'
                + '<a href="/" tabindex="0"></a>'
                + '<p id="contrast-canary">Faint text</p>'
                + '<input type="text" id="autocomplete-canary" aria-label="Canary" autocomplete="not-a-real-token">');
              const faint = document.getElementById('contrast-canary');
              faint.style.color = '#cccccc';
              faint.style.backgroundColor = '#ffffff';
            }
            """);

        AxeSweep sweep = new();
        await sweep.ScanAsync(visitor.Page, "canary");
        string found = string.Join("\n", sweep.Violations);

        // The assertion every other test relies on must itself fail when there are violations.
        Xunit.Sdk.XunitException refusal = Assert.ThrowsAny<Xunit.Sdk.XunitException>(sweep.AssertClean);
        Assert.Contains("accessibility violation(s)", refusal.Message, StringComparison.Ordinal);

        Assert.Matches(@"^4\.\d+\.\d+$", AxeSweep.EngineVersion ?? "none");
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "axe-engine-version.txt"), AxeSweep.EngineVersion, TestContext.Current.CancellationToken);
        Assert.Contains("image-alt", found, StringComparison.Ordinal);
        Assert.Contains("button-name", found, StringComparison.Ordinal);
        Assert.Contains("label", found, StringComparison.Ordinal);
        Assert.Contains("link-name", found, StringComparison.Ordinal);
        Assert.Contains("color-contrast", found, StringComparison.Ordinal);
        Assert.Contains("autocomplete-valid", found, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_public_pages_and_their_error_states_have_no_violations()
    {
        await using SitePage visitor = await SitePage.OpenAsync(site);
        await using SitePage clerk = await SitePage.OpenAsync(site);
        AxeSweep sweep = new();

        foreach ((string state, string path) in new[]
                 {
                     ("home", "/"),
                     ("permits", "/permits"),
                     ("lookup, empty", "/lookup"),
                     ("lookup, not found", "/lookup?number=CP-2026-999999"),
                     ("sign in", "/account/login"),
                     ("sign in, after registering", "/account/login?registered=true"),
                     ("register", "/account/register"),
                     ("page not found", "/no-such-page"),
                     ("status 429 page", "/status/429"),
                 })
        {
            await visitor.GoAsync(path);
            await sweep.ScanAsync(visitor.Page, state);
        }

        await clerk.SignInAsync(SitePage.Clerk);
        string number = await clerk.FirstIssuedNumberAsync();
        await visitor.GoAsync("/lookup?number=" + number);
        await sweep.ScanAsync(visitor.Page, "lookup, found");

        await visitor.GoAsync("/account/login");
        await visitor.Page.GetByLabel("Email address").FillAsync(SitePage.Clerk);
        await visitor.Page.GetByLabel("Password").FillAsync("wrong-password-1A");
        await SubmitAsync(visitor.Page, "Sign in");
        await visitor.Page.Locator("[data-error-summary]").WaitForAsync();
        await sweep.ScanAsync(visitor.Page, "sign in, wrong password");

        await visitor.GoAsync("/account/register");
        await SubmitAsync(visitor.Page, "Make account");
        await visitor.Page.Locator("[data-error-summary]").WaitForAsync();
        await sweep.ScanAsync(visitor.Page, "register, nothing entered");

        await visitor.GoAsync("/account/register");
        await visitor.Page.GetByLabel("Your name").FillAsync("Test Person");
        await visitor.Page.GetByLabel("Email address").FillAsync("someone@bramblewick.test");
        await visitor.Page.GetByLabel("Password").FillAsync("short");
        await SubmitAsync(visitor.Page, "Make account");
        await visitor.Page.Locator("[data-error-summary]").WaitForAsync();
        await sweep.ScanAsync(visitor.Page, "register, weak password");

        Assert.Equal(13, sweep.StatesScanned);
        sweep.AssertClean();
        Assert.Empty(visitor.Problems);
    }

    [Fact]
    public async Task An_applicants_pages_in_every_application_status_have_no_violations()
    {
        await using SitePage applicant = await SitePage.OpenAsync(site);
        await using SitePage second = await SitePage.OpenAsync(site);
        await using SitePage third = await SitePage.OpenAsync(site);
        AxeSweep sweep = new();
        await applicant.SignInAsync(SitePage.ApplicantOne);

        await applicant.GoAsync("/applications");
        await sweep.ScanAsync(applicant.Page, "applicant: my applications");

        await applicant.GoAsync("/applications/new");
        await sweep.ScanAsync(applicant.Page, "applicant: choose a permit");
        await SubmitAsync(applicant.Page, "Continue");
        await applicant.Page.Locator("[data-error-summary]").WaitForAsync();
        await sweep.ScanAsync(applicant.Page, "applicant: choose a permit, nothing chosen");

        await applicant.GoAsync("/applications/new");
        await applicant.Page.GetByRole(AriaRole.Radio, new PageGetByRoleOptions { Name = "Residential building permit" }).CheckAsync();
        await SubmitAsync(applicant.Page, "Continue");
        await applicant.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Project details", Level = 1 }).WaitForAsync();
        await sweep.ScanAsync(applicant.Page, "applicant: project details");
        await SubmitAsync(applicant.Page, "Save and continue");
        await applicant.Page.Locator("[data-error-summary]").WaitForAsync();
        await sweep.ScanAsync(applicant.Page, "applicant: project details, everything missing");

        await applicant.OpenApplicationAsync("Build a detached garden shed");
        await sweep.ScanAsync(applicant.Page, "applicant: draft");
        await applicant.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Change the details" }).ClickAsync();
        await applicant.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Change your application", Level = 1 }).WaitForAsync();
        await sweep.ScanAsync(applicant.Page, "applicant: change a draft");
        await applicant.Page.GetByLabel("What do you plan to do?").FillAsync("short");
        await SubmitAsync(applicant.Page, "Save changes");
        await applicant.Page.Locator("[data-error-summary]").WaitForAsync();
        await sweep.ScanAsync(applicant.Page, "applicant: change a draft, a problem");

        await applicant.OpenApplicationAsync("Remodel the kitchen");
        await sweep.ScanAsync(applicant.Page, "applicant: approved, inspection booked");
        await applicant.OpenApplicationAsync("Close the end of Lantern Row");
        await sweep.ScanAsync(applicant.Page, "applicant: issued");
        await applicant.OpenApplicationAsync("A produce stall");
        await sweep.ScanAsync(applicant.Page, "applicant: approved and paid");

        await second.SignInAsync(SitePage.ApplicantTwo);
        await second.OpenApplicationAsync("Replace the rear boundary fence");
        await sweep.ScanAsync(second.Page, "applicant: submitted");
        await second.OpenApplicationAsync("Close Main Street");
        await sweep.ScanAsync(second.Page, "applicant: denied");

        await third.SignInAsync(SitePage.ApplicantThree);
        await third.OpenApplicationAsync("Install a hanging blade sign");
        await sweep.ScanAsync(third.Page, "applicant: in review");
        await third.OpenApplicationAsync("Convert the ground floor");
        await sweep.ScanAsync(third.Page, "applicant: corrections requested");
        await third.OpenApplicationAsync("Hang a banner");
        await sweep.ScanAsync(third.Page, "applicant: withdrawn");

        await third.GoAsync("/admin/permit-types");
        await sweep.ScanAsync(third.Page, "applicant: not allowed (403)");

        Assert.Equal(17, sweep.StatesScanned);
        sweep.AssertClean();
        Assert.Empty(applicant.Problems);
        Assert.Empty(second.Problems);
        Assert.Empty(third.Problems);
    }

    [Fact]
    public async Task The_staff_and_inspector_workspaces_in_every_state_have_no_violations()
    {
        await using SitePage clerk = await SitePage.OpenAsync(site);
        await using SitePage inspector = await SitePage.OpenAsync(site);
        AxeSweep sweep = new();
        await clerk.SignInAsync(SitePage.Clerk);

        await clerk.GoAsync("/applications");
        await sweep.ScanAsync(clerk.Page, "clerk: all applications");
        await clerk.GoAsync("/applications?status=Issued");
        await sweep.ScanAsync(clerk.Page, "clerk: filtered by status");

        await clerk.OpenApplicationAsync("Replace the rear boundary fence");
        await sweep.ScanAsync(clerk.Page, "clerk: submitted (start review)");

        await clerk.OpenApplicationAsync("Install a hanging blade sign");
        await sweep.ScanAsync(clerk.Page, "clerk: in review (approve, deny, ask for changes)");
        await SubmitAsync(clerk.Page, "Deny application");
        await clerk.Page.Locator("[data-error-summary]").WaitForAsync();
        await sweep.ScanAsync(clerk.Page, "clerk: in review, deny without a reason");

        await clerk.OpenApplicationAsync("Remodel the kitchen");
        await sweep.ScanAsync(clerk.Page, "clerk: approved, unpaid, inspection booked");

        await clerk.OpenApplicationAsync("Build a raised timber deck");
        await sweep.ScanAsync(clerk.Page, "clerk: approved, inspection failed (book another)");
        await clerk.Page.GetByLabel("Date and time").FillAsync("2020-01-01T10:00");
        await clerk.Page.GetByLabel("Inspector").SelectOptionAsync(new SelectOptionValue { Label = "Sample Inspector One" });
        await SubmitAsync(clerk.Page, "Book inspection");
        await clerk.Page.Locator("[data-error-summary]").WaitForAsync();
        await sweep.ScanAsync(clerk.Page, "clerk: booking an inspection in the past");

        await clerk.OpenApplicationAsync("A produce stall");
        await sweep.ScanAsync(clerk.Page, "clerk: approved and paid (issue)");
        await clerk.OpenApplicationAsync("Close the end of Lantern Row");
        await sweep.ScanAsync(clerk.Page, "clerk: issued");

        await inspector.SignInAsync(SitePage.InspectorOne);
        await inspector.GoAsync("/applications");
        await sweep.ScanAsync(inspector.Page, "inspector: my inspections");
        await inspector.OpenApplicationAsync("Remodel the kitchen");
        await sweep.ScanAsync(inspector.Page, "inspector: record a result");
        await SubmitAsync(inspector.Page, "Save result");
        await inspector.Page.Locator("[data-error-summary]").WaitForAsync();
        await sweep.ScanAsync(inspector.Page, "inspector: result without a choice");

        Assert.Equal(13, sweep.StatesScanned);
        sweep.AssertClean();
        Assert.Empty(clerk.Problems);
        Assert.Empty(inspector.Problems);
    }

    [Fact]
    public async Task The_administrator_pages_in_every_state_have_no_violations()
    {
        await using SitePage admin = await SitePage.OpenAsync(site);
        AxeSweep sweep = new();
        await admin.SignInAsync(SitePage.Administrator);

        await admin.GoAsync("/admin/permit-types");
        await sweep.ScanAsync(admin.Page, "admin: permit types");
        await admin.GoAsync("/admin/permit-types/new");
        await sweep.ScanAsync(admin.Page, "admin: add a permit type");
        await SubmitAsync(admin.Page, "Add permit type");
        await admin.Page.Locator("[data-error-summary]").WaitForAsync();
        await sweep.ScanAsync(admin.Page, "admin: add a permit type, everything missing");

        await admin.GoAsync("/admin/permit-types");
        await admin.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Block party permit" }).ClickAsync();
        await admin.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Change Block party permit", Level = 1 }).WaitForAsync();
        await sweep.ScanAsync(admin.Page, "admin: change a permit type");

        Assert.Equal(4, sweep.StatesScanned);
        sweep.AssertClean();
        Assert.Empty(admin.Problems);
    }

    [Fact]
    public async Task The_main_pages_have_no_violations_on_a_320_pixel_screen_too()
    {
        await using SitePage clerk = await SitePage.OpenAsync(site, width: 320, height: 640);
        AxeSweep sweep = new();

        foreach ((string state, string path) in new[] { ("home", "/"), ("permits", "/permits"), ("lookup", "/lookup"), ("sign in", "/account/login") })
        {
            await clerk.GoAsync(path);
            await sweep.ScanAsync(clerk.Page, "320px " + state);
        }

        await clerk.SignInAsync(SitePage.Clerk);
        await clerk.GoAsync("/applications");
        await sweep.ScanAsync(clerk.Page, "320px clerk: all applications");
        await clerk.OpenApplicationAsync("Remodel the kitchen");
        await sweep.ScanAsync(clerk.Page, "320px clerk: an application");

        Assert.Equal(6, sweep.StatesScanned);
        sweep.AssertClean();
        Assert.Empty(clerk.Problems);
    }
}

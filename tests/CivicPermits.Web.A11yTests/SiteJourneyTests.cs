using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace CivicPermits.Web.A11yTests;

/// <summary>
/// Whole journeys through the real site in a real browser, done the way a person would: by label, by role, with the
/// keyboard. Each page also gets the structural checks every page must pass. The axe-core sweep over every state
/// arrives with the accessibility slice; these hand-written assertions stay beside it.
/// </summary>
public class SiteJourneyTests(SiteFixture site) : IClassFixture<SiteFixture>
{
    private static Regex Label(string start) => new("^" + Regex.Escape(start), RegexOptions.None, TimeSpan.FromSeconds(1));

    private static async Task FillProjectAsync(IPage page, string description, string valuation)
    {
        await page.GetByLabel(Label("What do you plan to do?")).FillAsync(description);
        await page.GetByLabel(Label("Street address")).FillAsync("21 Quarry Lane");
        await page.GetByLabel(Label("City or town")).FillAsync("Bramblewick");
        await page.GetByLabel(Label("State")).FillAsync("TG");
        await page.GetByLabel(Label("ZIP code")).FillAsync("70002");

        if (valuation.Length > 0)
        {
            await page.GetByLabel(Label("Value of the project")).FillAsync(valuation);
        }
    }

    private static async Task<string> StartApplicationAsync(SitePage person, string permitName, string valuation)
    {
        await person.GoAsync("/applications/new");
        await person.Page.GetByRole(AriaRole.Radio, new PageGetByRoleOptions { Name = permitName }).CheckAsync();
        await person.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue" }).ClickAsync();
        await Expect(person.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Project details", Level = 1 })).ToBeVisibleAsync();

        await FillProjectAsync(person.Page, "Build a timber pergola over the back patio.", valuation);
        await person.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save and continue" }).ClickAsync();
        await Expect(person.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Draft application", Level = 1 })).ToBeVisibleAsync();

        return person.Page.Url;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/permits")]
    [InlineData("/lookup")]
    [InlineData("/account/login")]
    [InlineData("/account/register")]
    public async Task Every_public_page_has_the_structure_every_page_needs(string path)
    {
        await using SitePage person = await SitePage.OpenAsync(site);

        IResponse? response = await person.GoAsync(path);

        Assert.Equal(200, response?.Status);
        await person.AssertPageStructureAsync();
        Assert.Empty(person.Problems);
    }

    [Fact]
    public async Task Every_page_has_a_title_of_its_own()
    {
        await using SitePage person = await SitePage.OpenAsync(site);
        List<string> titles = [];

        foreach (string path in new[] { "/", "/permits", "/lookup", "/account/login", "/account/register" })
        {
            await person.GoAsync(path);
            titles.Add(await person.Page.TitleAsync());
        }

        Assert.Equal(titles.Count, titles.Distinct().Count());
    }

    [Fact]
    public async Task The_permit_list_describes_each_permit_and_every_apply_link_says_which_permit()
    {
        await using SitePage person = await SitePage.OpenAsync(site);
        await person.GoAsync("/permits");

        Assert.Equal(9, await person.Page.Locator("li h3").CountAsync());
        ILocator apply = person.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Apply for Residential building permit" });
        Assert.Equal(1, await apply.CountAsync());
        Assert.Contains("$150.00 plus 1.25% of the project value", await person.Page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_permit_can_be_looked_up_by_number_and_a_wrong_number_is_explained_with_focus_on_the_problem()
    {
        await using SitePage clerk = await SitePage.OpenAsync(site);
        await clerk.SignInAsync(SitePage.Clerk);
        string number = await clerk.FirstIssuedNumberAsync();

        await using SitePage visitor = await SitePage.OpenAsync(site);
        await visitor.GoAsync("/lookup");
        await visitor.Page.GetByLabel(Label("Permit number")).FillAsync(number);
        await visitor.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Check permit" }).ClickAsync();

        await Expect(visitor.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = $"Permit {number}" })).ToBeVisibleAsync();
        await Expect(visitor.Page.Locator(".result")).ToContainTextAsync("Issued");
        Assert.DoesNotContain("Quarry", await visitor.Page.Locator("main").InnerTextAsync(), StringComparison.Ordinal);

        await visitor.GoAsync("/lookup?number=CP-2026-999999");
        await Expect(visitor.Page.Locator("[data-error-summary]")).ToBeFocusedAsync();
        await Expect(visitor.Page).ToHaveTitleAsync(new Regex("^Error: "));
        await visitor.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "No permit was found" }).ClickAsync();
        await Expect(visitor.Page.GetByLabel(Label("Permit number"))).ToBeFocusedAsync();
        await visitor.AssertPageStructureAsync();
        Assert.Empty(visitor.Problems);
    }

    [Fact]
    public async Task A_wrong_password_puts_focus_on_an_error_summary_whose_link_goes_to_the_field()
    {
        await using SitePage person = await SitePage.OpenAsync(site);
        await person.GoAsync("/account/login");

        await person.Page.GetByLabel(Label("Email address")).FillAsync(SitePage.Clerk);
        await person.Page.GetByLabel(Label("Password")).FillAsync("not-the-password-1A");
        await person.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();

        ILocator summary = person.Page.Locator("[data-error-summary]");
        await Expect(summary).ToBeFocusedAsync();
        await Expect(summary).ToContainTextAsync("The email address or password is not right.");
        await Expect(person.Page.GetByLabel(Label("Email address"))).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(person.Page).ToHaveTitleAsync(new Regex("^Error: "));

        await summary.GetByRole(AriaRole.Link).ClickAsync();
        await Expect(person.Page.GetByLabel(Label("Email address"))).ToBeFocusedAsync();
        await person.AssertPageStructureAsync();
        Assert.Empty(person.Problems);
    }

    [Fact]
    public async Task Someone_who_is_not_signed_in_is_sent_to_sign_in_and_brought_back_to_the_page_they_wanted()
    {
        await using SitePage person = await SitePage.OpenAsync(site);

        await person.GoAsync("/applications");

        Assert.Contains("/account/login?returnUrl=%2Fapplications", person.Page.Url, StringComparison.OrdinalIgnoreCase);

        await person.Page.GetByLabel(Label("Email address")).FillAsync(SitePage.ApplicantTwo);
        await person.Page.GetByLabel(Label("Password")).FillAsync(site.Password);
        await person.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();

        await Expect(person.Page).ToHaveURLAsync(new Regex("/applications$"));
        await Expect(person.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "My applications", Level = 1 })).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example")]
    public async Task A_sign_in_link_that_points_off_the_site_never_sends_anyone_there(string returnUrl)
    {
        await using SitePage person = await SitePage.OpenAsync(site);

        await person.GoAsync("/account/login?returnUrl=" + Uri.EscapeDataString(returnUrl));
        await person.Page.GetByLabel(Label("Email address")).FillAsync(SitePage.ApplicantTwo);
        await person.Page.GetByLabel(Label("Password")).FillAsync(site.Password);
        await person.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();

        await Expect(person.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "My applications", Level = 1 })).ToBeVisibleAsync();
        Assert.StartsWith(site.BaseAddress.ToString(), person.Page.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Signing_in_can_be_done_with_the_keyboard_alone_in_a_sensible_order()
    {
        await using SitePage person = await SitePage.OpenAsync(site);
        await person.GoAsync("/account/login");

        List<string> stops = [];

        for (int i = 0; i < 12; i++)
        {
            await person.Page.Keyboard.PressAsync("Tab");
            stops.Add(await person.Page.EvaluateAsync<string>("() => (document.activeElement.getAttribute('aria-label') || document.activeElement.textContent || document.activeElement.id || document.activeElement.tagName).trim()"));

            if (stops[^1] == "Sign in" && await person.Page.EvaluateAsync<bool>("() => document.activeElement.tagName === 'BUTTON'"))
            {
                break;
            }
        }

        Assert.Equal("Skip to main content", stops[0]);
        int email = stops.IndexOf("email");
        int password = stops.IndexOf("password");
        Assert.True(email > 0 && password == email + 1, $"Unexpected tab order: {string.Join(" > ", stops)}");
        Assert.Equal("Sign in", stops[^1]);

        // Back to the email field and finish with the keyboard.
        await person.Page.GetByLabel(Label("Email address")).FocusAsync();
        await person.Page.Keyboard.TypeAsync(SitePage.ApplicantTwo);
        await person.Page.Keyboard.PressAsync("Tab");
        await person.Page.Keyboard.TypeAsync(site.Password);
        await person.Page.Keyboard.PressAsync("Enter");

        await Expect(person.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "My applications", Level = 1 })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_person_signs_out_with_a_confirmation_and_then_sees_the_signed_out_navigation()
    {
        await using SitePage person = await SitePage.OpenAsync(site);
        await person.SignInAsync(SitePage.ApplicantTwo);
        Assert.Equal(1, await person.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "My applications" }).CountAsync());

        await person.SignOutAsync();

        await Expect(person.Page.GetByRole(AriaRole.Navigation).GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Sign in" })).ToBeVisibleAsync();
        await person.GoAsync("/applications");
        Assert.Contains("/account/login", person.Page.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Starting_an_application_asks_for_every_missing_thing_at_once_with_links_to_each_field()
    {
        await using SitePage person = await SitePage.OpenAsync(site);
        await person.SignInAsync(SitePage.ApplicantThree);
        await person.GoAsync("/applications/new");
        await person.Page.GetByRole(AriaRole.Radio, new PageGetByRoleOptions { Name = "Residential building permit" }).CheckAsync();
        await person.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue" }).ClickAsync();

        await person.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save and continue" }).ClickAsync();

        ILocator summary = person.Page.Locator("[data-error-summary]");
        await Expect(summary).ToBeFocusedAsync();
        ILocator links = summary.GetByRole(AriaRole.Link);
        Assert.Equal(6, await links.CountAsync());   // description, street, city, state, ZIP, value
        string[] targets = await links.EvaluateAllAsync<string[]>("els => els.map(e => e.hash.slice(1))");

        foreach (string id in targets)
        {
            Assert.Equal(1, await person.Page.Locator($"[id='{id}']").CountAsync());
            Assert.Equal("true", await person.Page.Locator($"[id='{id}']").GetAttributeAsync("aria-invalid"));
        }

        await person.AssertPageStructureAsync();
        Assert.Empty(person.Problems);
    }

    [Fact]
    public async Task Choosing_no_permit_says_so_and_goes_back_to_the_choice()
    {
        await using SitePage person = await SitePage.OpenAsync(site);
        await person.SignInAsync(SitePage.ApplicantThree);
        await person.GoAsync("/applications/new");

        await person.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue" }).ClickAsync();

        await Expect(person.Page.Locator("[data-error-summary]")).ToBeFocusedAsync();
        await Expect(person.Page.Locator("[data-error-summary]")).ToContainTextAsync("Choose a permit to continue.");
        await person.AssertPageStructureAsync();
    }

    [Fact]
    public async Task An_applicant_makes_a_draft_sees_the_three_steps_sends_it_and_a_clerk_decides_it()
    {
        await using SitePage applicant = await SitePage.OpenAsync(site);
        await using SitePage clerk = await SitePage.OpenAsync(site);
        await applicant.SignInAsync(SitePage.ApplicantTwo);

        string address = await StartApplicationAsync(applicant, "Fence and small structure permit", "");
        await Expect(applicant.Page.Locator("[aria-current='step']")).ToContainTextAsync("Step 3 of 3");
        await Expect(applicant.Page.Locator("[role=status]")).ToContainTextAsync("Your draft is saved");
        await applicant.AssertPageStructureAsync();

        await applicant.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send application" }).ClickAsync();
        await Expect(applicant.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { NameRegex = new Regex(@"^Application CP-\d{4}-\d{6}$"), Level = 1 })).ToBeVisibleAsync();
        await Expect(applicant.Page.Locator(".status-line")).ToContainTextAsync("Submitted");
        await Expect(applicant.Page.GetByText("The fee is worked out", new PageGetByTextOptions { Exact = false })).ToHaveCountAsync(0);

        // The clerk reviews it. Asking for a change without saying why is refused, with the focus on the problem.
        await clerk.SignInAsync(SitePage.Clerk);
        await clerk.GoAsync(address);
        await clerk.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Start review" }).ClickAsync();
        await Expect(clerk.Page.Locator(".status-line")).ToContainTextAsync("In review");

        await clerk.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Deny application" }).ClickAsync();
        await Expect(clerk.Page.Locator("[data-error-summary]")).ToBeFocusedAsync();
        await clerk.Page.Locator("[data-error-summary]").GetByRole(AriaRole.Link).ClickAsync();
        await Expect(clerk.Page.GetByLabel(Label("Reason for the decision"))).ToBeFocusedAsync();

        await clerk.Page.GetByLabel(Label("Reason for the decision")).FillAsync("The fence would block the shared access path.");
        await clerk.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Deny application" }).ClickAsync();
        await Expect(clerk.Page.Locator(".status-line")).ToContainTextAsync("Denied");

        // The applicant reads the reason exactly as written.
        await applicant.GoAsync(address);
        await Expect(applicant.Page.Locator("dl.facts")).ToContainTextAsync("The fence would block the shared access path.");
        await clerk.AssertPageStructureAsync();
        Assert.Empty(applicant.Problems);
        Assert.Empty(clerk.Problems);
    }

    [Fact]
    public async Task Staff_approve_record_payment_book_an_inspection_and_the_inspector_records_the_result_and_the_permit_is_issued()
    {
        await using SitePage applicant = await SitePage.OpenAsync(site);
        await using SitePage clerk = await SitePage.OpenAsync(site);
        await using SitePage inspector = await SitePage.OpenAsync(site);
        await applicant.SignInAsync(SitePage.ApplicantThree);
        string address = await StartApplicationAsync(applicant, "Residential building permit", "24000");
        await applicant.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send application" }).ClickAsync();
        await Expect(applicant.Page.Locator(".status-line")).ToContainTextAsync("Submitted");
        Assert.Contains("$450.00", await applicant.Page.Locator("dl.facts").InnerTextAsync(), StringComparison.Ordinal);

        await clerk.SignInAsync(SitePage.Clerk);
        await clerk.GoAsync(address);
        await clerk.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Start review" }).ClickAsync();
        await clerk.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Approve application" }).ClickAsync();
        await Expect(clerk.Page.Locator(".status-line")).ToContainTextAsync("Approved");

        await clerk.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Record payment" }).ClickAsync();
        await Expect(clerk.Page.Locator("dl.facts")).ToContainTextAsync("paid on");

        // Booking in the past is refused with the field named; a future time is accepted.
        await clerk.Page.GetByLabel(Label("Inspector")).SelectOptionAsync(new SelectOptionValue { Label = "Sample Inspector One" });
        await clerk.Page.GetByLabel(Label("Date and time")).FillAsync("2020-01-01T10:00");
        await clerk.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Book inspection" }).ClickAsync();
        await Expect(clerk.Page.Locator("[data-error-summary]")).ToContainTextAsync("future");

        await clerk.Page.GetByLabel(Label("Inspector")).SelectOptionAsync(new SelectOptionValue { Label = "Sample Inspector One" });
        await clerk.Page.GetByLabel(Label("Date and time")).FillAsync(DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd'T'10:00", System.Globalization.CultureInfo.InvariantCulture));
        await clerk.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Book inspection" }).ClickAsync();
        await Expect(clerk.Page.Locator("table").First).ToContainTextAsync("Scheduled");

        // The permit cannot be issued yet: the inspection has not passed.
        await clerk.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Issue permit" }).ClickAsync();
        await Expect(clerk.Page.Locator("[data-error-summary]")).ToContainTextAsync("inspection has passed");

        await inspector.SignInAsync(SitePage.InspectorOne);
        await inspector.GoAsync(address);
        await inspector.Page.GetByRole(AriaRole.Radio, new PageGetByRoleOptions { Name = "Passed" }).CheckAsync();
        await inspector.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save result" }).ClickAsync();
        await Expect(inspector.Page.Locator("[role=status]")).ToContainTextAsync("result is saved");

        await clerk.GoAsync(address);
        await clerk.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Issue permit" }).ClickAsync();
        await Expect(clerk.Page.Locator(".status-line")).ToContainTextAsync("Issued");
        await Expect(clerk.Page.Locator("dl.facts")).ToContainTextAsync("Valid until");

        await clerk.AssertPageStructureAsync();
        Assert.Empty(clerk.Problems);
        Assert.Empty(inspector.Problems);
        Assert.Empty(applicant.Problems);
    }

    [Fact]
    public async Task Another_applicants_application_is_a_real_404_and_a_non_admin_page_is_a_real_403()
    {
        await using SitePage owner = await SitePage.OpenAsync(site);
        await using SitePage other = await SitePage.OpenAsync(site);
        await owner.SignInAsync(SitePage.ApplicantTwo);
        string address = await StartApplicationAsync(owner, "Fence and small structure permit", "");

        await other.SignInAsync(SitePage.ApplicantThree);
        IResponse? hidden = await other.GoAsync(address);
        IResponse? forbidden = await other.GoAsync("/admin/permit-types");

        Assert.Equal(404, hidden?.Status);
        Assert.Equal(403, forbidden?.Status);
        await Expect(other.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "You do not have access to this page", Level = 1 })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task An_administrator_changes_a_permit_type_and_the_public_list_shows_the_new_fee()
    {
        await using SitePage admin = await SitePage.OpenAsync(site);
        await admin.SignInAsync(SitePage.Administrator);
        await admin.GoAsync("/admin/permit-types");
        await admin.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Block party permit" }).ClickAsync();

        await admin.Page.GetByLabel(Label("Base fee")).FillAsync("55");
        await admin.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
        await Expect(admin.Page.Locator("[role=status]")).ToContainTextAsync("saved");

        await admin.GoAsync("/permits");
        await Expect(admin.Page.Locator("li", new PageLocatorOptions { HasText = "Block party permit" })).ToContainTextAsync("$55.00");

        // A bad value is refused with the field named, and the person's other input is kept.
        await admin.GoAsync("/admin/permit-types");
        await admin.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Block party permit" }).ClickAsync();
        await admin.Page.GetByLabel(Label("Share of the project value")).FillAsync("40");
        await admin.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
        await Expect(admin.Page.Locator("[data-error-summary]")).ToBeFocusedAsync();
        Assert.Equal("55.00", await admin.Page.GetByLabel(Label("Base fee")).InputValueAsync());
        await admin.AssertPageStructureAsync();
        Assert.Empty(admin.Problems);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/permits")]
    [InlineData("/lookup?number=CP-2026-999999")]
    [InlineData("/account/login")]
    [InlineData("/account/register")]
    public async Task Public_pages_reflow_to_a_320_pixel_screen_without_scrolling_sideways(string path)
    {
        await using SitePage person = await SitePage.OpenAsync(site, width: 320, height: 640);

        await person.GoAsync(path);

        Assert.False(await person.ScrollsSidewaysAsync(), $"{path} scrolls sideways at 320px.");
    }

    [Fact]
    public async Task Signed_in_pages_with_tables_reflow_to_320_pixels_and_the_tables_scroll_inside_their_own_region()
    {
        await using SitePage clerk = await SitePage.OpenAsync(site, width: 320, height: 640);
        await clerk.SignInAsync(SitePage.Clerk);

        await clerk.GoAsync("/applications");
        Assert.False(await clerk.ScrollsSidewaysAsync(), "The application list scrolls sideways at 320px.");
        Assert.Equal("0", await clerk.Page.Locator(".table-wrap").First.GetAttributeAsync("tabindex"));

        string address = await clerk.Page.Locator("tbody th a").First.GetAttributeAsync("href") ?? "/applications";
        await clerk.GoAsync(address);
        Assert.False(await clerk.ScrollsSidewaysAsync(), "The application page scrolls sideways at 320px.");
    }

    [Fact]
    public async Task Text_can_be_enlarged_to_200_percent_without_losing_content()
    {
        await using SitePage person = await SitePage.OpenAsync(site, width: 640, height: 900);
        await person.GoAsync("/account/login");

        // Set through the style object, not as markup: the content security policy forbids inline style text.
        await person.Page.EvaluateAsync("() => { document.documentElement.style.fontSize = '200%'; }");

        Assert.False(await person.ScrollsSidewaysAsync());
        Assert.True(await person.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).IsVisibleAsync());
    }

    [Fact]
    public async Task Interface_parts_are_at_least_44_pixels_tall()
    {
        await using SitePage person = await SitePage.OpenAsync(site);
        await person.SignInAsync(SitePage.ApplicantTwo);
        await person.GoAsync("/applications");

        string[] tooSmall = await person.Page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('button, .button, input:not([type=hidden]), select, textarea, nav a')]
              .filter(e => e.getBoundingClientRect().height > 0 && e.getBoundingClientRect().height < 44)
              .map(e => (e.textContent || e.id || e.name).trim() + ' ' + Math.round(e.getBoundingClientRect().height))
            """);

        Assert.Empty(tooSmall);
    }
}

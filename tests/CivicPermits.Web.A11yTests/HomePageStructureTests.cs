using Microsoft.Playwright;

namespace CivicPermits.Web.A11yTests;

/// <summary>
/// The first real browser checks: the structure every page must have. The axe-core sweep over every page and
/// state arrives with the accessibility slice; these are the hand-written assertions that stay beside it.
/// </summary>
[Collection(SiteBrowserGroup.Name)]
public class HomePageStructureTests(SiteFixture site)
{
    [Fact]
    public async Task The_home_page_has_a_language_a_title_one_h1_and_the_four_landmarks()
    {
        site.SkipWithoutBrowser();
        IPage page = await site.Browser!.NewPageAsync();

        await page.GotoAsync(site.BaseAddress.ToString());

        Assert.Equal("en", await page.Locator("html").GetAttributeAsync("lang"));
        Assert.Equal("Civic Permits", await page.TitleAsync());
        Assert.Equal(1, await page.Locator("h1").CountAsync());
        Assert.Equal(1, await page.GetByRole(AriaRole.Banner).CountAsync());
        Assert.Equal(1, await page.GetByRole(AriaRole.Navigation).CountAsync());
        Assert.Equal(1, await page.GetByRole(AriaRole.Main).CountAsync());
        Assert.Equal(1, await page.GetByRole(AriaRole.Contentinfo).CountAsync());
    }

    [Fact]
    public async Task The_first_tab_stop_is_the_skip_link_and_it_moves_focus_into_the_main_content()
    {
        site.SkipWithoutBrowser();
        IPage page = await site.Browser!.NewPageAsync();
        await page.GotoAsync(site.BaseAddress.ToString());

        await page.Keyboard.PressAsync("Tab");

        ILocator skipLink = page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Skip to main content" });
        Assert.True(await skipLink.EvaluateAsync<bool>("element => element === document.activeElement"));
        Assert.True(await skipLink.IsVisibleAsync());

        await page.Keyboard.PressAsync("Enter");

        Assert.Equal("main", await page.EvaluateAsync<string>("document.activeElement.tagName.toLowerCase()"));
    }

    [Fact]
    public async Task The_home_page_loads_under_its_security_policy_with_no_console_errors_and_no_failed_requests()
    {
        site.SkipWithoutBrowser();
        IPage page = await site.Browser!.NewPageAsync();
        List<string> problems = [];

        // A content security policy that blocks a script or style fails silently for the user, so every
        // console error and failed request is collected and must be empty.
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                problems.Add($"console: {message.Text}");
            }
        };
        page.PageError += (_, error) => problems.Add($"page error: {error}");
        page.RequestFailed += (_, request) => problems.Add($"request failed: {request.Url} {request.Failure}");

        IResponse? response = await page.GotoAsync(site.BaseAddress.ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.NotNull(response);
        Assert.Contains("default-src 'self'", await response.HeaderValueAsync("content-security-policy") ?? string.Empty, StringComparison.Ordinal);
        Assert.Empty(problems);
    }

    [Fact]
    public async Task An_unknown_address_shows_the_not_found_page_with_a_real_404_status()
    {
        site.SkipWithoutBrowser();
        IPage page = await site.Browser!.NewPageAsync();

        IResponse? response = await page.GotoAsync(new Uri(site.BaseAddress, "/no-such-page").ToString());

        Assert.NotNull(response);
        Assert.Equal(404, response.Status);
        Assert.Equal("Page not found", await page.Locator("h1").InnerTextAsync());
    }
}

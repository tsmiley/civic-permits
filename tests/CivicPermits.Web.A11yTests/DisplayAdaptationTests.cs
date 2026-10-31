using Microsoft.Playwright;

namespace CivicPermits.Web.A11yTests;

/// <summary>
/// Things axe cannot judge: that every keyboard stop shows where focus is, that controls stay visible when the
/// person's system forces its own colours, and that text still fits when a person overrides its spacing.
/// </summary>
[Collection(SiteBrowserGroup.Name)]
public class DisplayAdaptationTests(SiteFixture site)
{
    /// <summary>
    /// Tabs through a page and returns the stops that show no focus indicator: no outline at all, or one thinner than
    /// two pixels. A keyboard user who cannot see where focus is cannot use the page.
    /// </summary>
    private static async Task<string[]> StopsWithoutAVisibleIndicatorAsync(IPage page, int maximumStops)
    {
        List<string> unseen = [];
        HashSet<string> seen = [];

        for (int i = 0; i < maximumStops; i++)
        {
            await page.Keyboard.PressAsync("Tab");

            string description = await page.EvaluateAsync<string>(
                """
                () => {
                  const e = document.activeElement;
                  if (!e || e === document.body) return 'body';
                  const s = getComputedStyle(e);
                  const label = (e.getAttribute('aria-label') || e.textContent || e.id || e.name || e.tagName).trim().slice(0, 40);
                  const visible = s.outlineStyle !== 'none' && parseFloat(s.outlineWidth) >= 2;
                  return (visible ? 'ok' : 'NO INDICATOR') + '|' + e.tagName.toLowerCase() + ':' + label;
                }
                """);

            if (description == "body" || !seen.Add(description))
            {
                break;
            }

            if (description.StartsWith("NO INDICATOR", StringComparison.Ordinal))
            {
                unseen.Add(description);
            }
        }

        Assert.True(seen.Count > 5, $"Only {seen.Count} keyboard stops were reached; the page was not really exercised.");

        return [.. unseen];
    }

    [Theory]
    [InlineData("/account/login")]
    [InlineData("/permits")]
    [InlineData("/applications/new")]
    public async Task Every_keyboard_stop_shows_where_focus_is(string path)
    {
        await using SitePage person = await SitePage.OpenAsync(site);
        await person.SignInAsync(SitePage.ApplicantTwo);
        await person.GoAsync(path);

        Assert.Empty(await StopsWithoutAVisibleIndicatorAsync(person.Page, 60));
    }

    [Fact]
    public async Task The_focus_check_does_notice_when_a_focus_indicator_is_removed()
    {
        await using SitePage person = await SitePage.OpenAsync(site);
        await person.GoAsync("/account/login");
        await person.Page.EvaluateAsync("() => document.querySelectorAll('a, button, input').forEach(e => e.style.setProperty('outline', 'none', 'important'))");

        string[] unseen = await StopsWithoutAVisibleIndicatorAsync(person.Page, 60);

        Assert.NotEmpty(unseen);
    }

    [Fact]
    public async Task Every_keyboard_stop_on_a_staff_page_shows_where_focus_is()
    {
        await using SitePage clerk = await SitePage.OpenAsync(site);
        await clerk.SignInAsync(SitePage.Clerk);
        await clerk.OpenApplicationAsync("Remodel the kitchen");

        Assert.Empty(await StopsWithoutAVisibleIndicatorAsync(clerk.Page, 80));
    }

    [Theory]
    [InlineData("/account/login")]
    [InlineData("/permits")]
    public async Task Controls_keep_a_visible_boundary_when_the_system_forces_its_own_colours(string path)
    {
        await using SitePage person = await SitePage.OpenAsync(site, forcedColors: true);
        await person.GoAsync(path);

        Assert.True(await person.Page.EvaluateAsync<bool>("() => matchMedia('(forced-colors: active)').matches"), "Forced colours were not applied.");

        // In forced-colours mode backgrounds are replaced, so a button or field is recognisable only by its border.
        string[] unbounded = await person.Page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('button, .button, input:not([type=hidden]):not([type=radio]):not([type=checkbox]), select, textarea, .badge')]
              .filter(e => parseFloat(getComputedStyle(e).borderTopWidth) < 1 || getComputedStyle(e).borderTopStyle === 'none')
              .map(e => (e.textContent || e.id || e.name).trim())
            """);

        Assert.Empty(unbounded);
    }

    [Fact]
    public async Task Text_still_fits_when_the_person_overrides_its_spacing()
    {
        await using SitePage person = await SitePage.OpenAsync(site, width: 640, height: 900);
        await person.GoAsync("/account/register");

        // The WCAG 1.4.12 values: line height 1.5, letter spacing 0.12em, word spacing 0.16em, paragraph spacing 2em.
        // Set through the style object, not as markup, because the content security policy forbids inline style text.
        await person.Page.EvaluateAsync(
            """
            () => {
              const root = document.documentElement;
              root.style.setProperty('line-height', '1.5', 'important');
              root.style.setProperty('letter-spacing', '0.12em', 'important');
              root.style.setProperty('word-spacing', '0.16em', 'important');
              for (const e of document.querySelectorAll('*')) {
                e.style.setProperty('line-height', '1.5', 'important');
                e.style.setProperty('letter-spacing', '0.12em', 'important');
                e.style.setProperty('word-spacing', '0.16em', 'important');
              }
              for (const p of document.querySelectorAll('p')) p.style.setProperty('margin-bottom', '2em', 'important');
            }
            """);

        Assert.False(await person.ScrollsSidewaysAsync(), "The page scrolls sideways once text spacing is overridden.");

        string[] clipped = await person.Page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('button, label, h1, h2, a, legend, .hint')]
              .filter(e => e.getBoundingClientRect().width > 0 && e.scrollWidth > e.clientWidth + 1)
              .map(e => e.textContent.trim().slice(0, 40))
            """);

        Assert.Empty(clipped);
        Assert.True(await person.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Make account" }).IsVisibleAsync());
    }
}

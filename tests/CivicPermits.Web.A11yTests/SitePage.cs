using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace CivicPermits.Web.A11yTests;

/// <summary>
/// One person's browser session on the site: its own cookies, and a record of every console error and failed
/// request, so a content-security-policy block or a script error cannot pass unnoticed.
/// </summary>
internal sealed partial class SitePage : IAsyncDisposable
{
    public const string ApplicantTwo = "applicant.two@bramblewick.test";
    public const string ApplicantThree = "applicant.three@bramblewick.test";
    public const string Clerk = "clerk@bramblewick.test";
    public const string Administrator = "administrator@bramblewick.test";
    public const string InspectorOne = "inspector.one@bramblewick.test";

    private readonly SiteFixture _site;
    private readonly IBrowserContext _context;

    private SitePage(SiteFixture site, IBrowserContext context, IPage page)
    {
        _site = site;
        _context = context;
        Page = page;
    }

    public IPage Page { get; }

    /// <summary>Console errors, page errors and failed requests seen so far. Must be empty at the end of a test.</summary>
    public List<string> Problems { get; } = [];

    public static async Task<SitePage> OpenAsync(SiteFixture site, int width = 1280, int height = 900)
    {
        site.SkipWithoutBrowser();
        IBrowserContext context = await site.Browser!.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            BaseURL = site.BaseAddress.ToString(),
        });
        IPage page = await context.NewPageAsync();
        SitePage session = new(site, context, page);

        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                session.Problems.Add($"console: {message.Text}");
            }
        };
        page.PageError += (_, error) => session.Problems.Add($"page error: {error}");
        page.RequestFailed += (_, request) => session.Problems.Add($"request failed: {request.Url} {request.Failure}");

        return session;
    }

    public async Task<IResponse?> GoAsync(string path) =>
        await Page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

    /// <summary>Signs in through the real form, with the keyboard-friendly labels a person would use.</summary>
    public async Task SignInAsync(string email)
    {
        await GoAsync("/account/login");
        await Page.GetByLabel("Email address").FillAsync(email);
        await Page.GetByLabel("Password").FillAsync(_site.Password);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await Page.WaitForURLAsync(url => !url.Contains("/account/login", StringComparison.Ordinal));
    }

    public async Task SignOutAsync()
    {
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Sign out" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out" }).ClickAsync();
        await Page.WaitForURLAsync(url => url.TrimEnd('/') == _site.BaseAddress.ToString().TrimEnd('/'));
    }

    /// <summary>The permit number of the first issued sample permit, read from a clerk's list.</summary>
    public async Task<string> FirstIssuedNumberAsync()
    {
        await GoAsync("/applications?status=Issued");
        string text = await Page.Locator("tbody th a").First.InnerTextAsync();

        return PermitNumber().Match(text).Value;
    }

    /// <summary>The basic structure every page must have. The axe-core sweep comes later; these stay beside it.</summary>
    public async Task AssertPageStructureAsync()
    {
        Assert.Equal("en", await Page.Locator("html").GetAttributeAsync("lang"));
        Assert.False(string.IsNullOrWhiteSpace(await Page.TitleAsync()), "The page has no title.");
        Assert.EndsWith("Civic Permits", await Page.TitleAsync(), StringComparison.Ordinal);
        Assert.Equal(1, await Page.Locator("h1").CountAsync());
        Assert.Equal(1, await Page.GetByRole(AriaRole.Banner).CountAsync());
        Assert.Equal(1, await Page.GetByRole(AriaRole.Main).CountAsync());
        Assert.Equal(1, await Page.GetByRole(AriaRole.Contentinfo).CountAsync());

        // Every form control has a name a screen reader can say; ids are unique; headings do not skip a level.
        string[] defects = await Page.EvaluateAsync<string[]>(
            """
            () => {
              const found = [];
              for (const control of document.querySelectorAll('input:not([type=hidden]), select, textarea')) {
                const named = (control.labels && control.labels.length > 0)
                  || control.getAttribute('aria-label') || control.getAttribute('aria-labelledby');
                if (!named) found.push('control without a label: ' + (control.id || control.name));
              }
              const ids = [...document.querySelectorAll('[id]')].map(e => e.id);
              for (const id of new Set(ids.filter((id, i) => ids.indexOf(id) !== i))) found.push('duplicate id: ' + id);
              let last = 0;
              for (const heading of document.querySelectorAll('h1,h2,h3,h4,h5,h6')) {
                const level = Number(heading.tagName[1]);
                if (last !== 0 && level > last + 1) found.push('heading level jumps from ' + last + ' to ' + level + ': ' + heading.textContent.trim());
                last = level;
              }
              for (const link of document.querySelectorAll('a[href]')) {
                if (!(link.textContent || '').trim() && !link.getAttribute('aria-label')) found.push('link without a name: ' + link.href);
              }
              for (const image of document.querySelectorAll('img:not([alt])')) found.push('image without alt: ' + image.src);
              for (const button of document.querySelectorAll('button')) {
                if (!(button.textContent || '').trim() && !button.getAttribute('aria-label')) found.push('button without a name');
              }
              return found;
            }
            """);

        Assert.Empty(defects);
    }

    /// <summary>Whether the page scrolls sideways at the current width. At 320px it must not.</summary>
    public Task<bool> ScrollsSidewaysAsync() =>
        Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth");

    public async ValueTask DisposeAsync() => await _context.CloseAsync();

    [GeneratedRegex(@"CP-\d{4}-\d{6}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex PermitNumber();
}

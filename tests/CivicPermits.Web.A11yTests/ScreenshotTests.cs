using Microsoft.Playwright;

namespace CivicPermits.Web.A11yTests;

/// <summary>
/// Takes the screenshots the documentation shows, from the real running site with the sample data. It does nothing
/// unless asked: <c>CP_SCREENSHOTS=1 dotnet test tests/CivicPermits.Web.A11yTests --filter ScreenshotTests</c>.
/// Screenshots are regenerated, never edited, so the pictures cannot show something the site does not do.
/// </summary>
public class ScreenshotTests(SiteFixture site) : IClassFixture<SiteFixture>
{
    private static string ImagesFolder()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        string folder = Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "docs", "images");
        Directory.CreateDirectory(folder);

        return folder;
    }

    private static async Task ShootAsync(IPage page, string name, bool fullPage = false) =>
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ImagesFolder(), name + ".png"), FullPage = fullPage });

    [Fact]
    public async Task Take_the_screenshots_for_the_documentation()
    {
        if (Environment.GetEnvironmentVariable("CP_SCREENSHOTS") != "1")
        {
            Assert.Skip("Set CP_SCREENSHOTS=1 to regenerate the documentation screenshots.");
        }

        await using SitePage visitor = await SitePage.OpenAsync(site, width: 1100, height: 760);
        await visitor.GoAsync("/");
        await ShootAsync(visitor.Page, "home");

        await visitor.GoAsync("/permits");
        await ShootAsync(visitor.Page, "permits");

        await visitor.GoAsync("/account/login");
        await visitor.Page.GetByLabel("Email address").FillAsync(SitePage.Clerk);
        await visitor.Page.GetByLabel("Password").FillAsync("not-the-password-1A");
        await visitor.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await visitor.Page.Locator("[data-error-summary]").WaitForAsync();
        await ShootAsync(visitor.Page, "sign-in-error");

        await using SitePage applicant = await SitePage.OpenAsync(site, width: 1100, height: 760);
        await applicant.SignInAsync(SitePage.ApplicantTwo);
        await applicant.GoAsync("/applications/new");
        await applicant.Page.GetByRole(AriaRole.Radio, new PageGetByRoleOptions { Name = "Residential building permit" }).CheckAsync();
        await applicant.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue" }).ClickAsync();
        await applicant.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save and continue" }).ClickAsync();
        await applicant.Page.Locator("[data-error-summary]").WaitForAsync();
        await ShootAsync(applicant.Page, "project-details-errors", fullPage: true);

        await applicant.OpenApplicationAsync("Close Main Street");
        await ShootAsync(applicant.Page, "application-denied", fullPage: true);

        await using SitePage clerk = await SitePage.OpenAsync(site, width: 1100, height: 760);
        await clerk.SignInAsync(SitePage.Clerk);
        await clerk.GoAsync("/applications");
        await ShootAsync(clerk.Page, "application-list");
        await clerk.OpenApplicationAsync("Install a hanging blade sign");
        await ShootAsync(clerk.Page, "application-in-review", fullPage: true);

        await using SitePage admin = await SitePage.OpenAsync(site, width: 1100, height: 760);
        await admin.SignInAsync(SitePage.Administrator);
        await admin.GoAsync("/admin/permit-types");
        await ShootAsync(admin.Page, "permit-types-admin");

        await using SitePage phone = await SitePage.OpenAsync(site, width: 390, height: 780);
        await phone.SignInAsync(SitePage.Clerk);
        await phone.OpenApplicationAsync("Remodel the kitchen");
        await ShootAsync(phone.Page, "application-on-a-phone", fullPage: true);
    }
}

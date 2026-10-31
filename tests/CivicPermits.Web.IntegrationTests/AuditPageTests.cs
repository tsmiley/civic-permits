using System.Net;
using CivicPermits.Domain.Common;
using CivicPermits.Infrastructure.Persistence;
using CivicPermits.Infrastructure.Persistence.Seeding;

namespace CivicPermits.Web.IntegrationTests;

/// <summary>The audit page: administrators read it, nobody else does, and nothing on it can change an entry.</summary>
public class AuditPageTests
{
    private const string SampleAdministrator = "administrator@bramblewick.test";
    private const string SampleClerk = "clerk@bramblewick.test";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task AddEntryAsync(WebFactory factory, string summary)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        CivicPermitsDbContext database = scope.ServiceProvider.GetRequiredService<CivicPermitsDbContext>();
        database.AuditEntries.Add(AuditEntry.Record(Guid.CreateVersion7(), DateTimeOffset.UtcNow, SampleIdentities.Administrator, "permit_type.revised", "PermitType", Guid.NewGuid(), summary));
        await database.SaveChangesAsync(Token);
    }

    [Fact]
    public async Task An_administrator_reads_the_record_and_sees_who_did_what()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        await AddEntryAsync(factory, "Changed \"Fence permit\": base fee from $50.00 to $55.00.");
        using HttpClient administrator = factory.Client();
        await Forms.SignInAsync(administrator, SampleAdministrator, factory.Password, Token);

        HttpResponseMessage response = await administrator.GetAsync("/admin/audit", Token);
        string body = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Audit record", body, StringComparison.Ordinal);
        Assert.Contains("base fee from $50.00 to $55.00", WebUtility.HtmlDecode(body), StringComparison.Ordinal);
        Assert.Contains("permit_type.revised", body, StringComparison.Ordinal);
        Assert.Contains("Administrator", body, StringComparison.Ordinal); // the name of the account that acted
    }

    [Fact]
    public async Task An_empty_record_says_so()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient administrator = factory.Client();
        await Forms.SignInAsync(administrator, SampleAdministrator, factory.Password, Token);

        string body = await (await administrator.GetAsync("/admin/audit", Token)).Content.ReadAsStringAsync(Token);

        Assert.Contains("Nothing has been recorded yet.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_page_is_read_only_it_has_no_form_and_no_control_that_changes_anything()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        await AddEntryAsync(factory, "Closed \"Fence permit\" to new applications (it was open).");
        using HttpClient administrator = factory.Client();
        await Forms.SignInAsync(administrator, SampleAdministrator, factory.Password, Token);

        string body = await (await administrator.GetAsync("/admin/audit", Token)).Content.ReadAsStringAsync(Token);

        Assert.DoesNotContain("<form", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<button", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<input", body, StringComparison.OrdinalIgnoreCase);

        // And a post to it is not a way in either: the page has no handler for one.
        HttpResponseMessage post = await administrator.PostAsync("/admin/audit", new FormUrlEncodedContent(new Dictionary<string, string>()), Token);
        Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
    }

    [Fact]
    public async Task A_clerk_is_sent_to_the_access_denied_page_and_a_signed_out_person_to_sign_in()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        await AddEntryAsync(factory, "Created \"Fence permit\".");
        using HttpClient clerk = factory.Client();
        using HttpClient anonymous = factory.Client();
        await Forms.SignInAsync(clerk, SampleClerk, factory.Password, Token);

        HttpResponseMessage denied = await clerk.GetAsync("/admin/audit", Token);
        HttpResponseMessage signedOut = await anonymous.GetAsync("/admin/audit", Token);

        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/account/denied", denied.Headers.Location!.PathAndQuery, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.StartsWith("/account/login", signedOut.Headers.Location!.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_administrators_are_offered_the_link_to_it()
    {
        using WebFactory factory = new WebFactory().WithSampleData();
        using HttpClient administrator = factory.Client();
        using HttpClient clerk = factory.Client();
        await Forms.SignInAsync(administrator, SampleAdministrator, factory.Password, Token);
        await Forms.SignInAsync(clerk, SampleClerk, factory.Password, Token);

        string forAdministrator = await (await administrator.GetAsync("/", Token)).Content.ReadAsStringAsync(Token);
        string forClerk = await (await clerk.GetAsync("/", Token)).Content.ReadAsStringAsync(Token);

        Assert.Contains("href=\"/admin/audit\"", forAdministrator, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/admin/audit\"", forClerk, StringComparison.Ordinal);
    }
}

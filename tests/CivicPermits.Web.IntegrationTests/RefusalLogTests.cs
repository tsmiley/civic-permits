using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Serilog.Core;
using Serilog.Events;

namespace CivicPermits.Web.IntegrationTests;

/// <summary>
/// What the site writes to its log when it refuses someone: one line, naming the account (never an address) and the
/// route that was asked for. The site answers with redirects, not 401 and 403, so each kind is checked here.
/// </summary>
public class RefusalLogTests
{
    private const string SampleClerk = "clerk@bramblewick.test";
    private const string SampleApplicant = "applicant.two@bramblewick.test";
    private const string WrongPassword = "not-the-password-1A";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Text(LogEvent logEvent) => logEvent.RenderMessage(CultureInfo.InvariantCulture);

    private static string? Property(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out LogEventPropertyValue? value) && value is ScalarValue { Value: { } scalar }
            ? Convert.ToString(scalar, CultureInfo.InvariantCulture)
            : null;

    private static IReadOnlyList<LogEvent> Refusals(Sink sink) => [.. sink.Events.Where(entry => Text(entry).StartsWith("Refused:", StringComparison.Ordinal))];

    /// <summary>Nothing a person typed or was called may reach the log.</summary>
    private static void AssertNothingPersonalIsLogged(Sink sink, WebFactory factory)
    {
        string everything = string.Join("\n", sink.Events.Select(entry => Text(entry) + string.Join(",", entry.Properties.Select(p => p.Value.ToString()))));

        Assert.DoesNotContain("bramblewick.test", everything, StringComparison.Ordinal);
        Assert.DoesNotContain(WrongPassword, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Password, everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_signed_out_visit_to_a_signed_in_page_leaves_one_line_naming_nobody_and_the_route_asked_for()
    {
        Sink sink = new();
        using WebFactory factory = new WebFactory().WithSampleData().WithServices(services => services.AddSingleton<ILogEventSink>(sink));
        using HttpClient client = factory.Client();

        HttpResponseMessage response = await client.GetAsync("/applications/b2000000-0000-4000-8000-000000000001", Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(sink.Events.Count > 0, "The test sink received no log events at all: the host did not use it.");
        LogEvent line = Assert.Single(Refusals(sink));
        Assert.Equal("anonymous", Property(line, "Account"));
        Assert.Equal("unauthenticated", Property(line, "Code"));
        Assert.Equal("GET", Property(line, "Method"));
        Assert.DoesNotContain("b2000000", Text(line), StringComparison.Ordinal);
        Assert.Equal("/applications/{Id:guid}", Property(line, "Route")); // the pattern, never the identifier that was asked for
        AssertNothingPersonalIsLogged(sink, factory);
    }

    [Fact]
    public async Task A_clerk_sent_to_the_access_denied_page_leaves_one_line_naming_the_account_and_the_page_asked_for()
    {
        Sink sink = new();
        using WebFactory factory = new WebFactory().WithSampleData().WithServices(services => services.AddSingleton<ILogEventSink>(sink));
        using HttpClient clerk = factory.Client();
        await Forms.SignInAsync(clerk, SampleClerk, factory.Password, Token);

        HttpResponseMessage asked = await clerk.GetAsync("/admin/permit-types", Token);
        HttpResponseMessage denied = await clerk.GetAsync(asked.Headers.Location!.OriginalString, Token);

        Assert.Equal(HttpStatusCode.Redirect, asked.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        LogEvent line = Assert.Single(Refusals(sink));
        Assert.Equal("forbidden", Property(line, "Code"));
        Assert.Equal("/admin/permit-types", Property(line, "Route"));
        Assert.True(Guid.TryParse(Property(line, "Account"), out _), "The account should be an identifier, not an address or a name.");
        AssertNothingPersonalIsLogged(sink, factory);
    }

    [Fact]
    public async Task A_failed_sign_in_leaves_one_line_naming_the_account_and_never_the_address_or_the_password()
    {
        Sink sink = new();
        using WebFactory factory = new WebFactory().WithSampleData().WithServices(services => services.AddSingleton<ILogEventSink>(sink));
        using HttpClient client = factory.Client();

        HttpResponseMessage response = await Forms.SignInAsync(client, SampleApplicant, WrongPassword, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        LogEvent line = Assert.Single(Refusals(sink));
        Assert.Equal("auth.sign_in_failed", Property(line, "Code"));
        Assert.Equal("POST", Property(line, "Method"));
        Assert.Equal("/account/login", Property(line, "Route"));
        Assert.True(Guid.TryParse(Property(line, "Account"), out _), "The account should be an identifier, not an address.");
        AssertNothingPersonalIsLogged(sink, factory);
    }

    [Fact]
    public async Task A_successful_sign_in_and_an_ordinary_page_leave_no_refusal_line()
    {
        Sink sink = new();
        using WebFactory factory = new WebFactory().WithSampleData().WithServices(services => services.AddSingleton<ILogEventSink>(sink));
        using HttpClient client = factory.Client();

        await Forms.SignInAsync(client, SampleApplicant, factory.Password, Token);
        HttpResponseMessage page = await client.GetAsync("/applications", Token);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Empty(Refusals(sink));
    }

    private sealed class Sink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        public IReadOnlyList<LogEvent> Events => [.. _events];

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }
}

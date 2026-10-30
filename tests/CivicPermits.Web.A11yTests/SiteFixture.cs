using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace CivicPermits.Web.A11yTests;

/// <summary>
/// Starts the real web site as a separate process on a free local port, and a real browser to drive it.
/// Playwright needs an actual HTTP server, which an in-memory test host is not.
/// </summary>
/// <remarks>
/// The browser is the Chrome or Edge already installed on the machine (set <c>CP_BROWSER_CHANNEL</c> to
/// choose), so running the tests downloads nothing. When no such browser exists the tests skip with a
/// message that says how to supply one; they never pass silently.
/// </remarks>
public sealed class SiteFixture : IAsyncLifetime
{
    // One client for every probe; creating a client per request exhausts sockets.
    private static readonly HttpClient Probe = new() { Timeout = TimeSpan.FromSeconds(2) };

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"civic-permits-a11y-{Guid.NewGuid():N}.db");
    private Process? _site;
    private IPlaywright? _playwright;

    /// <summary>
    /// The password of the sample sign-ins, made up fresh for every run. It exists only in this process and the site
    /// process it starts, so no credential is written anywhere.
    /// </summary>
    public string Password { get; } = $"Aa1-{Guid.NewGuid():N}";

    /// <summary>The address the site listens on.</summary>
    public Uri BaseAddress { get; private set; } = new("http://127.0.0.1:0");

    /// <summary>The browser, or null when none could be started.</summary>
    public IBrowser? Browser { get; private set; }

    /// <summary>Why there is no browser, for the skip message.</summary>
    public string? NoBrowserReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        BaseAddress = new Uri($"http://127.0.0.1:{FreePort()}");

        string siteAssembly = typeof(Program).Assembly.Location;

        ProcessStartInfo start = new("dotnet", ["exec", siteAssembly, "--urls", BaseAddress.ToString()])
        {
            WorkingDirectory = Path.GetDirectoryName(siteAssembly)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["Database__Provider"] = "Sqlite";
        start.Environment["Database__ConnectionString"] = $"Data Source={_databasePath}";
        start.Environment["Database__MigrateOnStartup"] = "true";
        start.Environment["Database__SeedSampleData"] = "true";
        start.Environment["Database__SampleUserPassword"] = Password;
        start.Environment["RateLimiting__AuthPerWindow"] = "1000";

        _site = Process.Start(start) ?? throw new InvalidOperationException("The web site process did not start.");
        _ = _site.StandardOutput.ReadToEndAsync();
        _ = _site.StandardError.ReadToEndAsync();

        await WaitUntilLiveAsync();

        _playwright = await Playwright.CreateAsync();
        await StartBrowserAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        _playwright?.Dispose();

        if (_site is { HasExited: false })
        {
            _site.Kill(entireProcessTree: true);
            await _site.WaitForExitAsync();
        }

        _site?.Dispose();

        foreach (string suffix in new[] { string.Empty, "-shm", "-wal" })
        {
            File.Delete(_databasePath + suffix);
        }
    }

    /// <summary>Skips the calling test with an explanation when there is no browser.</summary>
    public void SkipWithoutBrowser()
    {
        if (Browser is null)
        {
            Assert.Skip(NoBrowserReason ?? "No browser is available.");
        }
    }

    private async Task StartBrowserAsync()
    {
        string[] channels = Environment.GetEnvironmentVariable("CP_BROWSER_CHANNEL") is { Length: > 0 } chosen
            ? [chosen]
            : ["chrome", "msedge"];

        List<string> failures = [];

        foreach (string channel in channels)
        {
            try
            {
                Browser = await _playwright!.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = channel, Headless = true });
                return;
            }
            catch (PlaywrightException failure)
            {
                failures.Add($"{channel}: {failure.Message.Split('\n')[0]}");
            }
        }

        NoBrowserReason =
            "No installed Chrome or Edge could be started (" + string.Join("; ", failures) + "). " +
            "Install one, or set CP_BROWSER_CHANNEL to a Playwright channel such as 'chrome', 'msedge' or 'chrome-beta'.";
    }

    private async Task WaitUntilLiveAsync()
    {
        DateTime giveUp = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < giveUp)
        {
            if (_site!.HasExited)
            {
                throw new InvalidOperationException($"The web site stopped during start-up (exit code {_site.ExitCode}).");
            }

            try
            {
                using HttpResponseMessage response = await Probe.GetAsync(new Uri(BaseAddress, "/health/live"));

                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            catch (TaskCanceledException)
            {
                // Not answering yet.
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("The web site did not become live within 60 seconds.");
    }

    private static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

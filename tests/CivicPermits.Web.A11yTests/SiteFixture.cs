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
/// <para>
/// The browser is the Chrome or Edge already installed on the machine (set <c>CP_BROWSER_CHANNEL</c> to
/// choose), so running the tests downloads nothing. When no such browser exists the tests skip with a
/// message that says how to supply one; they never pass silently.
/// </para>
/// <para>
/// One browser is started for the whole run (the fixture is shared by every test class through <see cref="SiteBrowserGroup"/>), with
/// one persistent profile folder (<c>CP_BROWSER_PROFILE_DIR</c>, or <c>civic-permits/a11y-profile</c> under the local application
/// data folder), and it is closed with Playwright's own close, awaited, never by killing the process. Why: on a Windows machine
/// Chrome checks at start-up whether the account's password is blank, and Windows counts each check as a failed sign-in; a fresh
/// profile for every launch, or one browser per test class, locked the account out. Chrome keeps the answer in the profile,
/// but writes it only on a clean shutdown, so the profile must persist and the close must be clean. The folder is never deleted by
/// the tests. Only one run at a time may use a profile folder. The browser also listens for debugging on a free port of the loopback address
/// for the length of the run, so each test can still make a context of its own (separate cookies and settings).
/// </para>
/// </remarks>
public sealed class SiteFixture : IAsyncLifetime
{
    // One client for every probe; creating a client per request exhausts sockets.
    private static readonly HttpClient Probe = new() { Timeout = TimeSpan.FromSeconds(2) };

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"civic-permits-a11y-{Guid.NewGuid():N}.db");
    private Process? _site;
    private IPlaywright? _playwright;
    private IBrowserContext? _profileContext;

    /// <summary>
    /// The password of the sample sign-ins, made up fresh for every run. It exists only in this process and the site
    /// process it starts, so no credential is written anywhere.
    /// </summary>
    public string Password { get; } = $"Aa1-{Guid.NewGuid():N}";

    /// <summary>The address the site listens on.</summary>
    public Uri BaseAddress { get; private set; } = new("http://127.0.0.1:0");

    /// <summary>
    /// The browser, or null when none could be started. It is the one browser that owns the persistent profile, reached over its debugging
    /// port, so each test can still make contexts of its own (separate cookies and settings), which a persistent context does not allow.
    /// </summary>
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
        Assertions.SetDefaultExpectTimeout(30_000);
        await StartBrowserAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            // Leaves the debugging connection and every context the tests made; the browser itself is closed below.
            await Browser.CloseAsync();
        }

        if (_profileContext is not null)
        {
            // A clean close, awaited: Chrome writes what it learned into the profile at shutdown. Never a process kill.
            await _profileContext.CloseAsync();
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
            try
            {
                File.Delete(_databasePath + suffix);
            }
            catch (IOException)
            {
                // The site process may not have let go of the file yet; the temp folder is cleaned up later.
            }
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

    private static string ProfileDirectory() =>
        Environment.GetEnvironmentVariable("CP_BROWSER_PROFILE_DIR") is { Length: > 0 } chosen
            ? chosen
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "civic-permits", "a11y-profile");

    private async Task StartBrowserAsync()
    {
        string profile = ProfileDirectory();
        Directory.CreateDirectory(profile);

        string[] channels = Environment.GetEnvironmentVariable("CP_BROWSER_CHANNEL") is { Length: > 0 } chosen
            ? [chosen]
            : ["chrome", "msedge"];

        List<string> failures = [];

        foreach (string channel in channels)
        {
            try
            {
                // Playwright will not take --user-data-dir as an argument; a persistent context is how it is given a profile folder. That context is
                // kept only to own the browser process (and to close it cleanly). The same browser is also opened for debugging on a local port, so the
                // tests can make separate contexts in it, each with cookies and settings of its own.
                int debuggingPort = FreePort();
                _profileContext = await _playwright!.Chromium.LaunchPersistentContextAsync(profile, new BrowserTypeLaunchPersistentContextOptions
                {
                    Channel = channel,
                    Headless = true,
                    Args = [$"--remote-debugging-port={debuggingPort}", "--remote-debugging-address=127.0.0.1"],
                });
                Browser = await _playwright.Chromium.ConnectOverCDPAsync($"http://127.0.0.1:{debuggingPort}");

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
        // Generous: on a busy machine (another build, another test run) the first start can be slow.
        DateTime giveUp = DateTime.UtcNow.AddSeconds(120);

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

        throw new TimeoutException("The web site did not become live within 120 seconds.");
    }

    private static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

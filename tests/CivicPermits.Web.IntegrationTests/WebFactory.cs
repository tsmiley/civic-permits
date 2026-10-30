using System.Text.RegularExpressions;
using CivicPermits.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CivicPermits.Web.IntegrationTests;

/// <summary>
/// The real web site in memory against a throwaway SQLite file, with the sample town and sample sign-ins loaded.
/// The sample password is made up fresh for each instance, so no credential exists in the repository.
/// </summary>
public sealed class WebFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"civic-permits-web-{Guid.NewGuid():N}.db");
    private readonly Dictionary<string, string?> _settings = [];
    private readonly List<Action<IServiceCollection>> _services = [];
    private string _environment = "Development";

    public WebFactory()
    {
        _settings["Database:Provider"] = "Sqlite";
        _settings["Database:ConnectionString"] = $"Data Source={_databasePath}";
        _settings["Database:MigrateOnStartup"] = "true";
        _settings["Hosting:RedirectToHttps"] = "false";
        _settings["RateLimiting:AuthPerWindow"] = "1000";
    }

    /// <summary>The password of the sample sign-ins.</summary>
    public string Password { get; } = $"Aa1-{Guid.NewGuid():N}";

    /// <summary>Loads the sample town and the sample sign-ins (Development only).</summary>
    public WebFactory WithSampleData()
    {
        _settings["Database:SeedSampleData"] = "true";
        _settings["Database:SampleUserPassword"] = Password;

        return this;
    }

    public WebFactory WithSetting(string key, string? value)
    {
        _settings[key] = value;

        return this;
    }

    /// <summary>Adds or replaces a service in the host, such as a log sink to capture what the site writes.</summary>
    public WebFactory WithServices(Action<IServiceCollection> configure)
    {
        _services.Add(configure);

        return this;
    }

    public WebFactory InEnvironment(string environment)
    {
        _environment = environment;

        return this;
    }

    /// <summary>A client that does not follow redirects, so a test can see them, and keeps its cookies.</summary>
    public HttpClient Client(Uri? address = null) => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = address ?? new Uri("http://localhost"),
    });

    /// <summary>Makes an applicant account directly, for a site that has no sample data (Production never has).</summary>
    public async Task<string> RegisterApplicantAsync(string email)
    {
        string password = $"Aa1-{Guid.NewGuid():N}";
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        RegistrationOutcome outcome = await scope.ServiceProvider.GetRequiredService<RegistrationService>()
            .RegisterApplicantAsync(email, password, "Test Applicant");

        Assert.True(outcome.Accepted);

        return password;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_settings));
        builder.ConfigureServices(services => _services.ForEach(configure => configure(services)));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            foreach (string suffix in new[] { string.Empty, "-shm", "-wal" })
            {
                try
                {
                    File.Delete(_databasePath + suffix);
                }
                catch (IOException)
                {
                    // Left for the temp folder to clean up.
                }
            }
        }
    }
}

/// <summary>Reading and posting the site's forms the way a browser does, anti-forgery token included.</summary>
internal static partial class Forms
{
    public static async Task<string> GetHtmlAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(path, cancellationToken);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>The hidden fields of the form with the given name (the framework's <c>_handler</c> value), as a browser would send them back.</summary>
    public static Dictionary<string, string> Hidden(string html, string formName)
    {
        string form = html.Split("<form", StringSplitOptions.RemoveEmptyEntries)
            .First(chunk => chunk.Contains($"name=\"_handler\" value=\"{formName}\"", StringComparison.Ordinal)
                || chunk.Contains($"value=\"{formName}\" name=\"_handler\"", StringComparison.Ordinal));
        Dictionary<string, string> fields = [];

        foreach (Match input in HiddenInput().Matches(form))
        {
            string tag = input.Value;
            string? name = Attribute(tag, "name");

            if (name is not null)
            {
                fields[name] = System.Net.WebUtility.HtmlDecode(Attribute(tag, "value") ?? string.Empty);
            }
        }

        return fields;
    }

    /// <summary>Posts a form: its hidden fields (token, handler) plus the values the person typed.</summary>
    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string path, string html, string formName, Dictionary<string, string> typed, CancellationToken cancellationToken)
    {
        Dictionary<string, string> fields = Hidden(html, formName);

        foreach ((string name, string value) in typed)
        {
            fields[name] = value;
        }

        using FormUrlEncodedContent content = new(fields);

        return await client.PostAsync(path, content, cancellationToken);
    }

    /// <summary>Signs in through the real form and returns the response of the post.</summary>
    public static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string email, string password, CancellationToken cancellationToken)
    {
        string html = await GetHtmlAsync(client, "/account/login", cancellationToken);

        return await PostAsync(client, "/account/login", html, "login", new() { ["email"] = email, ["password"] = password }, cancellationToken);
    }

    private static string? Attribute(string tag, string name)
    {
        Match match = Regex.Match(
            tag, $@"\b{name}=""([^""]*)""", RegexOptions.None, TimeSpan.FromSeconds(1));

        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex("""<input[^>]*type="hidden"[^>]*>""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HiddenInput();
}

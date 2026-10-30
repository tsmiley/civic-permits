using CivicPermits.Application.Messaging;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CivicPermits.Api.IntegrationTests;

/// <summary>
/// Hosts the real API in memory against a throwaway SQLite file, so a test exercises the same pipeline,
/// middleware and configuration as production without a network or a shared database.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"civic-permits-{Guid.NewGuid():N}.db");
    private readonly Dictionary<string, string?> _settings = [];
    private string _environment = "Development";
    private Action<IServiceCollection>? _services;

    /// <summary>Overrides one configuration value before the host starts.</summary>
    public ApiFactory WithSetting(string key, string? value)
    {
        _settings[key] = value;
        return this;
    }

    /// <summary>Runs the host in another environment. Production is where error details must stay hidden.</summary>
    public ApiFactory InEnvironment(string environment)
    {
        _environment = environment;
        return this;
    }

    /// <summary>Replaces a service, for example with a dispatcher that fails.</summary>
    public ApiFactory WithServices(Action<IServiceCollection> configure)
    {
        _services = configure;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            Dictionary<string, string?> values = new()
            {
                ["Database:Provider"] = "Sqlite",
                ["Database:ConnectionString"] = $"Data Source={_databasePath}",
                ["Hosting:RedirectToHttps"] = "false",

                // The Development settings migrate and seed on start. A test chooses that deliberately.
                ["Database:MigrateOnStartup"] = "false",
                ["Database:SeedSampleData"] = "false",
            };

            foreach ((string key, string? value) in _settings)
            {
                values[key] = value;
            }

            configuration.AddInMemoryCollection(values);
        });

        builder.ConfigureServices(services => _services?.Invoke(services));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            TryDelete(_databasePath);
            TryDelete(_databasePath + "-shm");
            TryDelete(_databasePath + "-wal");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A temp file that cannot be removed right now is cleaned up with the temp folder.
        }
    }

    /// <summary>A dispatcher that always fails, to prove failures never leak to the caller.</summary>
    public sealed class ThrowingDispatcher : IDispatcher
    {
        public ValueTask<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SECRET-INTERNAL-DETAIL connection string and stack");
    }
}

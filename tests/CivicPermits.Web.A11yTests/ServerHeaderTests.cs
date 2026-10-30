using System.Net;

namespace CivicPermits.Web.A11yTests;

/// <summary>
/// What the real server, Kestrel, sends over a real socket. The in-memory test server has no Kestrel in it, so the
/// <c>Server</c> header could not be checked there at all.
/// </summary>
public class ServerHeaderTests(SiteFixture site) : IClassFixture<SiteFixture>
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

    [Theory]
    [InlineData("/")]
    [InlineData("/account/login")]
    [InlineData("/no-such-page")]
    [InlineData("/app.css")]
    [InlineData("/health/live")]
    public async Task The_real_server_names_neither_itself_nor_its_version_and_sends_the_cross_origin_headers(string path)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri(site.BaseAddress, path), TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.Contains("Server"), $"{path}: the Server header should not be sent.");
        Assert.False(response.Headers.Contains("X-Powered-By"), $"{path}: X-Powered-By should not be sent.");

        Assert.Equal("same-site", response.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.Equal("none", response.Headers.GetValues("X-Permitted-Cross-Domain-Policies").Single());
    }
}

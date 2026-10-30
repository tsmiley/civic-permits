namespace CivicPermits.Infrastructure.Hosting;

/// <summary>
/// The response headers every host sends. Only the content security policy differs between hosts: the
/// API serves no documents so it allows nothing, while the web site may load its own scripts and styles.
/// </summary>
/// <param name="ContentSecurityPolicy">The value of the <c>Content-Security-Policy</c> header.</param>
public sealed record SecurityHeadersPolicy(string ContentSecurityPolicy)
{
    /// <summary>For a JSON API: nothing may be loaded, framed or submitted to.</summary>
    public static SecurityHeadersPolicy ForApi { get; } =
        new("default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'");

    /// <summary>
    /// For the web site: its own scripts, styles, images and fonts only, no framing, forms submit to itself.
    /// There is no <c>unsafe-inline</c>; pages must not need it.
    /// </summary>
    public static SecurityHeadersPolicy ForWebSite { get; } =
        new("default-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'");
}

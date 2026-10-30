using System.Reflection;
using Microsoft.AspNetCore.Builder;

namespace CivicPermits.Infrastructure.Hosting;

/// <summary>
/// Refuses to start outside Development on an ASP.NET Core runtime older than the first one that fixed a flaw that let
/// authentication cookies and tokens be forged (GHSA-9mv3-2cwr-p262, versions 10.0.0 to 10.0.6 on hosts that are not
/// Windows). Both hosts protect their cookies and bearer tokens with the framework's data protection, so a host that is
/// behind is not safe however well the application is written.
/// </summary>
public static class RuntimeFloor
{
    /// <summary>The oldest ASP.NET Core runtime a host may run on outside Development.</summary>
    public static readonly Version Minimum = new(10, 0, 7);

    /// <summary>The ASP.NET Core version this process is running on, or null if it cannot be read.</summary>
    public static Version? Running()
    {
        string? text = typeof(WebApplication).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // "10.0.14+abcdef1234" or "11.0.0-preview.1.25000.1": the numbers before the first plus or hyphen.
        string? numbers = text?.Split('+', '-')[0];

        return Version.TryParse(numbers, out Version? version) ? version : null;
    }

    /// <summary>Throws when the host is not in Development and its runtime is older than <see cref="Minimum"/>, or cannot be read.</summary>
    /// <exception cref="InvalidOperationException">The runtime is too old to be trusted with signed-in people.</exception>
    public static void Require(bool isDevelopment, Version? running)
    {
        if (isDevelopment)
        {
            return;
        }

        if (running is null || running < Minimum)
        {
            throw new InvalidOperationException(
                $"This host runs ASP.NET Core {running?.ToString() ?? "(unknown version)"}, and outside Development {Minimum} or later is required " +
                "(earlier versions let authentication cookies be forged on hosts that are not Windows). Update the .NET runtime.");
        }
    }
}

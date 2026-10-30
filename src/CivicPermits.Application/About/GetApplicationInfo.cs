using System.Reflection;
using CivicPermits.Application.Messaging;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Application.About;

/// <summary>Asks the application to describe itself.</summary>
public sealed record GetApplicationInfoQuery : IRequest<ApplicationInfoResponse>;

/// <summary>Answers <see cref="GetApplicationInfoQuery"/>. The time comes from the injected provider so tests can fix it.</summary>
internal sealed class GetApplicationInfoHandler(TimeProvider time)
    : IRequestHandler<GetApplicationInfoQuery, ApplicationInfoResponse>
{
    public const string ProductName = "Civic Permits";

    public ValueTask<ApplicationInfoResponse> HandleAsync(GetApplicationInfoQuery request, CancellationToken cancellationToken)
    {
        string version = typeof(GetApplicationInfoHandler).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0";

        // The build appends the commit as "+sha"; the public answer is the version alone.
        int plus = version.IndexOf('+', StringComparison.Ordinal);

        return ValueTask.FromResult(new ApplicationInfoResponse(
            ProductName,
            plus < 0 ? version : version[..plus],
            time.GetUtcNow()));
    }
}

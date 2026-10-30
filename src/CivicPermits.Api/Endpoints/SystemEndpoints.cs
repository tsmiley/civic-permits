using CivicPermits.Application.About;
using CivicPermits.Application.Messaging;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Api.Endpoints;

/// <summary>Endpoints that describe the service itself rather than permits.</summary>
internal static class SystemEndpoints
{
    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/info", GetInfoAsync)
            .WithName("GetApplicationInfo")
            .WithSummary("Describes the running service.")
            .WithDescription("Returns the product name, the version of the running API and the server's UTC time. Needs no sign-in.")
            .Produces<ApplicationInfoResponse>();

        return group;
    }

    private static async Task<IResult> GetInfoAsync(IDispatcher dispatcher, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dispatcher.SendAsync(new GetApplicationInfoQuery(), cancellationToken));
}

namespace CivicPermits.Shared.Contracts;

/// <summary>What the API says about itself: the answer to <c>GET /api/v1/info</c>.</summary>
/// <param name="Name">The product name.</param>
/// <param name="Version">The assembly version of the running API.</param>
/// <param name="ServerTime">The server's current time, in UTC.</param>
public sealed record ApplicationInfoResponse(string Name, string Version, DateTimeOffset ServerTime);

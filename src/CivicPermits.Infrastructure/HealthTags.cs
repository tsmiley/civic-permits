namespace CivicPermits.Infrastructure;

/// <summary>Tags that decide which health checks run on which endpoint.</summary>
public static class HealthTags
{
    /// <summary>Checks that must pass before the host can take traffic (the database, for example).</summary>
    public const string Ready = "ready";
}

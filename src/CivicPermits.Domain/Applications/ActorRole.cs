namespace CivicPermits.Domain.Applications;

/// <summary>The part a person plays when they act on an application.</summary>
public enum ActorRole
{
    /// <summary>A resident or business applying for a permit. Acts only on their own applications.</summary>
    Applicant = 1,

    /// <summary>An office clerk who reviews applications and records payments.</summary>
    Clerk = 2,

    /// <summary>An inspector who records what an inspection found.</summary>
    Inspector = 3,

    /// <summary>An administrator, who can do everything a clerk can.</summary>
    Administrator = 4,
}

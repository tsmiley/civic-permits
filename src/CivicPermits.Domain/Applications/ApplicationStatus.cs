namespace CivicPermits.Domain.Applications;

/// <summary>Where an application is in the office's process.</summary>
public enum ApplicationStatus
{
    /// <summary>Started by the applicant and not yet sent to the office.</summary>
    Draft = 1,

    /// <summary>Sent to the office and waiting for a clerk.</summary>
    Submitted = 2,

    /// <summary>A clerk is reviewing it.</summary>
    InReview = 3,

    /// <summary>The clerk needs the applicant to change something and send it again.</summary>
    CorrectionsRequested = 4,

    /// <summary>The office has approved it; the permit is issued once the fee is paid and any inspection passes.</summary>
    Approved = 5,

    /// <summary>The office has refused it. Final.</summary>
    Denied = 6,

    /// <summary>The applicant has withdrawn it. Final.</summary>
    Withdrawn = 7,

    /// <summary>The permit has been issued. Final.</summary>
    Issued = 8,
}

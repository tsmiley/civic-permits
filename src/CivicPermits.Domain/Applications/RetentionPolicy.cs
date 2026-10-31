namespace CivicPermits.Domain.Applications;

/// <summary>Which retention period an application falls under.</summary>
public enum RetentionClass
{
    /// <summary>Approved, denied or issued: the office's record of a decision.</summary>
    Decided = 1,

    /// <summary>Never sent.</summary>
    Draft = 2,

    /// <summary>Taken back by the applicant.</summary>
    Withdrawn = 3,
}

/// <summary>
/// How long personal data is kept. The periods are a decision made for this project (see SECURITY.md): a decided
/// application is the office's legal record and is kept seven years after its last change; a draft or a withdrawn
/// application, which no decision rests on, one year; an applicant's account with no application, two years after the
/// last time it was used. An application still in progress is never purged by age.
/// </summary>
public static class RetentionPolicy
{
    /// <summary>Years a decided application is kept after its last change.</summary>
    public const int DecidedApplicationYears = 7;

    /// <summary>Years a draft or a withdrawn application is kept after its last change.</summary>
    public const int DraftOrWithdrawnApplicationYears = 1;

    /// <summary>Years an applicant's account with no application is kept after its last sign-in (or its creation, if it never signed in).</summary>
    public const int InactiveAccountYears = 2;

    /// <summary>The period an application falls under, or null when it is in progress and is not purged by age.</summary>
    public static RetentionClass? ClassOf(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Approved or ApplicationStatus.Denied or ApplicationStatus.Issued => RetentionClass.Decided,
        ApplicationStatus.Draft => RetentionClass.Draft,
        ApplicationStatus.Withdrawn => RetentionClass.Withdrawn,
        _ => null,
    };

    /// <summary>The years the class is kept.</summary>
    public static int YearsFor(RetentionClass retentionClass) =>
        retentionClass == RetentionClass.Decided ? DecidedApplicationYears : DraftOrWithdrawnApplicationYears;

    /// <summary>Whether an application's period has run out: the whole period has passed since its last change, to the instant.</summary>
    public static bool IsExpired(ApplicationStatus status, DateTimeOffset lastChangedOn, DateTimeOffset now) =>
        ClassOf(status) is { } retentionClass && lastChangedOn.AddYears(YearsFor(retentionClass)) <= now;

    /// <summary>Whether an account's period has run out, counted from the last time it was used.</summary>
    public static bool AccountIsExpired(DateTimeOffset lastActiveOn, DateTimeOffset now) =>
        lastActiveOn.AddYears(InactiveAccountYears) <= now;
}

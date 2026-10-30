namespace CivicPermits.Infrastructure.Persistence.Seeding;

/// <summary>
/// The fixed identifiers the sample data uses for its people. They are bare ids: the sample data names no one,
/// holds no email address and sets no password. A later slice creates demonstration sign-ins for these ids from
/// configuration, never from a value written in the repository.
/// </summary>
public static class SampleIdentities
{
    /// <summary>The first sample applicant.</summary>
    public static readonly Guid ApplicantOne = Guid.Parse("a1000000-0000-4000-8000-000000000001");

    /// <summary>The second sample applicant.</summary>
    public static readonly Guid ApplicantTwo = Guid.Parse("a1000000-0000-4000-8000-000000000002");

    /// <summary>The third sample applicant.</summary>
    public static readonly Guid ApplicantThree = Guid.Parse("a1000000-0000-4000-8000-000000000003");

    /// <summary>The sample permit clerk.</summary>
    public static readonly Guid Clerk = Guid.Parse("c1000000-0000-4000-8000-000000000001");

    /// <summary>The sample administrator.</summary>
    public static readonly Guid Administrator = Guid.Parse("d1000000-0000-4000-8000-000000000001");

    /// <summary>The first sample inspector.</summary>
    public static readonly Guid InspectorOne = Guid.Parse("e1000000-0000-4000-8000-000000000001");

    /// <summary>The second sample inspector.</summary>
    public static readonly Guid InspectorTwo = Guid.Parse("e1000000-0000-4000-8000-000000000002");
}

using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;
using CivicPermits.Domain.Permits;

namespace CivicPermits.Domain.Tests;

/// <summary>
/// Builders for the fictional people, places and permits the tests use. Everything is deterministic so a
/// failing test fails the same way every time.
/// </summary>
internal static class Sample
{
    /// <summary>A fixed afternoon in November 2026.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);

    public static readonly Actor Applicant = new(Guid.Parse("a0000000-0000-4000-8000-000000000001"), ActorRole.Applicant);
    public static readonly Actor OtherApplicant = new(Guid.Parse("a0000000-0000-4000-8000-000000000002"), ActorRole.Applicant);
    public static readonly Actor Clerk = new(Guid.Parse("c0000000-0000-4000-8000-000000000001"), ActorRole.Clerk);
    public static readonly Actor Administrator = new(Guid.Parse("d0000000-0000-4000-8000-000000000001"), ActorRole.Administrator);
    public static readonly Actor Inspector = new(Guid.Parse("e0000000-0000-4000-8000-000000000001"), ActorRole.Inspector);
    public static readonly Actor OtherInspector = new(Guid.Parse("e0000000-0000-4000-8000-000000000002"), ActorRole.Inspector);

    public static readonly PermitNumber Number = PermitNumber.From(2026, 17);

    public static PostalAddress Address() =>
        PostalAddress.Create("100 Lantern Row", null, "Bramblewick", "TG", "70001");

    /// <summary>A building permit: $150 base plus 1.25% of the declared value, inspected, valid for a year.</summary>
    public static PermitType BuildingPermit(bool requiresInspection = true, int valuationRateBasisPoints = 125) =>
        PermitType.Create(
            Guid.Parse("b0000000-0000-4000-8000-000000000001"),
            PermitCategory.Building,
            "Residential building permit",
            "New construction, additions and structural alterations to a home.",
            Money.FromDollars(150m),
            valuationRateBasisPoints,
            requiresInspection,
            validityDays: 365,
            targetReviewDays: 10);

    public static PermitApplication Draft(PermitType? type = null, Money? valuation = null) =>
        PermitApplication.CreateDraft(
            Guid.Parse("f0000000-0000-4000-8000-000000000001"),
            type ?? BuildingPermit(),
            Applicant.Id,
            "Add a covered porch to the rear of the house.",
            Address(),
            valuation ?? Money.FromDollars(24_000m),
            Now);

    public static PermitApplication Submitted(PermitType? type = null, Money? valuation = null)
    {
        PermitType permitType = type ?? BuildingPermit();
        PermitApplication application = Draft(permitType, valuation);
        application.Submit(Applicant, permitType, Number, Now.AddMinutes(1));

        return application;
    }

    public static PermitApplication InReview(PermitType? type = null)
    {
        PermitApplication application = Submitted(type);
        application.StartReview(Clerk, Now.AddHours(1));

        return application;
    }

    public static PermitApplication Approved(PermitType? type = null)
    {
        PermitApplication application = InReview(type);
        application.Approve(Clerk, "Plans comply with the residential code.", Now.AddHours(2));

        return application;
    }
}

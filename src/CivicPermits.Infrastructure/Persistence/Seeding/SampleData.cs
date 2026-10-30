using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;
using CivicPermits.Domain.Permits;

namespace CivicPermits.Infrastructure.Persistence.Seeding;

/// <summary>
/// The sample town: the fictional Town of Bramblewick, State of Tallgrass. Every street, applicant and project is
/// invented. Applications are built by calling the real domain methods at earlier moments, so the sample history
/// is something the rules would actually allow, not rows forced into the database.
/// </summary>
internal static class SampleData
{
    public static readonly Guid Residential = TypeId(1);
    public static readonly Guid Commercial = TypeId(2);
    public static readonly Guid Fence = TypeId(3);
    public static readonly Guid BusinessSign = TypeId(4);
    public static readonly Guid TemporaryBanner = TypeId(5);
    public static readonly Guid StreetFestival = TypeId(6);
    public static readonly Guid BlockParty = TypeId(7);
    public static readonly Guid FoodVendor = TypeId(8);
    public static readonly Guid MarketStall = TypeId(9);

    public static IReadOnlyList<PermitType> PermitTypes() =>
    [
        PermitType.Create(Residential, PermitCategory.Building, "Residential building permit",
            "New construction, additions and structural alterations to a home.",
            Money.FromDollars(150m), 125, requiresInspection: true, validityDays: 365, targetReviewDays: 10),
        PermitType.Create(Commercial, PermitCategory.Building, "Commercial building permit",
            "New construction and major alterations to shops, offices and workshops.",
            Money.FromDollars(400m), 150, requiresInspection: true, validityDays: 730, targetReviewDays: 20),
        PermitType.Create(Fence, PermitCategory.Building, "Fence and small structure permit",
            "Fences, sheds and similar structures under 200 square feet.",
            Money.FromDollars(60m), 0, requiresInspection: false, validityDays: 180, targetReviewDays: 5),
        PermitType.Create(BusinessSign, PermitCategory.Sign, "Business sign permit",
            "A permanent sign on or in front of a business.",
            Money.FromDollars(85m), 100, requiresInspection: false, validityDays: 365, targetReviewDays: 7),
        PermitType.Create(TemporaryBanner, PermitCategory.Sign, "Temporary banner permit",
            "A banner displayed for up to thirty days.",
            Money.FromDollars(25m), 0, requiresInspection: false, validityDays: 30, targetReviewDays: 3),
        PermitType.Create(StreetFestival, PermitCategory.Event, "Street festival permit",
            "Closing a public street for a community festival.",
            Money.FromDollars(200m), 0, requiresInspection: false, validityDays: 30, targetReviewDays: 14),
        PermitType.Create(BlockParty, PermitCategory.Event, "Block party permit",
            "A neighbourhood gathering on a residential street.",
            Money.FromDollars(40m), 0, requiresInspection: false, validityDays: 30, targetReviewDays: 5),
        PermitType.Create(FoodVendor, PermitCategory.Vendor, "Mobile food vendor permit",
            "Selling prepared food from a truck or cart on public land.",
            Money.FromDollars(325m), 0, requiresInspection: true, validityDays: 365, targetReviewDays: 10),
        PermitType.Create(MarketStall, PermitCategory.Vendor, "Farmers market stall permit",
            "A stall at the Saturday market on Founders Green.",
            Money.FromDollars(90m), 0, requiresInspection: false, validityDays: 180, targetReviewDays: 5),
    ];

    /// <summary>
    /// Builds the sample applications as they would stand now. Times count back from <paramref name="now"/>, so the
    /// data always looks recent. Each is at a different point in the process.
    /// </summary>
    public static IReadOnlyList<PermitApplication> Applications(IReadOnlyDictionary<Guid, PermitType> types, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(types);

        Actor applicantA = new(SampleIdentities.ApplicantOne, ActorRole.Applicant);
        Actor applicantB = new(SampleIdentities.ApplicantTwo, ActorRole.Applicant);
        Actor applicantC = new(SampleIdentities.ApplicantThree, ActorRole.Applicant);
        Actor clerk = new(SampleIdentities.Clerk, ActorRole.Clerk);
        Actor admin = new(SampleIdentities.Administrator, ActorRole.Administrator);
        Actor inspector = new(SampleIdentities.InspectorOne, ActorRole.Inspector);

        Dictionary<int, int> sequences = [];
        PermitNumber NextNumber(DateTimeOffset at)
        {
            int year = at.UtcDateTime.Year;
            sequences[year] = sequences.GetValueOrDefault(year) + 1;

            return PermitNumber.From(year, sequences[year]);
        }

        // Moments count back from now: Ago(10) is ten days ago, Ago(10, 3) is ten days ago plus three hours.
        DateTimeOffset Ago(int days, int hours = 0) => now.AddDays(-days).AddHours(hours);

        PermitApplication Draft(int n, Guid typeId, Actor who, string what, PostalAddress where, decimal dollars, DateTimeOffset at) =>
            PermitApplication.CreateDraft(ApplicationId(n), types[typeId], who.Id, what, where, Money.FromDollars(dollars), at);

        PermitApplication Submit(PermitApplication application, Actor who, Guid typeId, DateTimeOffset at)
        {
            application.Submit(who, types[typeId], NextNumber(at), at);

            return application;
        }

        List<PermitApplication> all = [];

        // 1. A draft the applicant has not sent yet.
        all.Add(Draft(1, Residential, applicantA, "Build a detached garden shed with a concrete slab at the back of the lot.",
            Address("14 Lantern Row", "70001"), 6_500m, Ago(2)));

        // 2. Sent this week, waiting for a clerk.
        PermitApplication fence = Draft(2, Fence, applicantB, "Replace the rear boundary fence with a six-foot cedar fence.",
            Address("220 Mill Lane", "70002"), 0m, Ago(4));
        all.Add(Submit(fence, applicantB, Fence, Ago(3)));

        // 3. A clerk is reviewing it.
        PermitApplication sign = Draft(3, BusinessSign, applicantC, "Install a hanging blade sign above the entrance of the bakery.",
            Address("3 Market Square", "70001"), 1_800m, Ago(8));
        Submit(sign, applicantC, BusinessSign, Ago(7));
        sign.StartReview(clerk, Ago(6));
        all.Add(sign);

        // 4. Sent back for corrections: the applicant has not answered yet.
        PermitApplication shop = Draft(4, Commercial, applicantC, "Convert the ground floor of the old mill office into a retail space.",
            Address("9 Foundry Street", "70003"), 85_000m, Ago(15));
        Submit(shop, applicantC, Commercial, Ago(14));
        shop.StartReview(clerk, Ago(13));
        shop.RequestCorrections(clerk, "The plans do not show the second fire exit. Please add it and send them again.", Ago(12));
        all.Add(shop);

        // 5. Approved, an inspection is booked for next week, the fee is still due.
        PermitApplication kitchen = Draft(5, Residential, applicantA, "Remodel the kitchen and move the load-bearing wall between kitchen and dining room.",
            Address("14 Lantern Row", "70001"), 32_000m, Ago(12));
        Submit(kitchen, applicantA, Residential, Ago(11));
        kitchen.StartReview(clerk, Ago(10));
        kitchen.Approve(clerk, "Structural plan accepted.", Ago(9));
        kitchen.ScheduleInspection(clerk, SampleIdentities.InspectorOne, now.AddDays(5), Ago(3));
        all.Add(kitchen);

        // 6. Approved and paid, but the inspection failed and a re-inspection has not been booked.
        PermitApplication deck = Draft(6, Residential, applicantB, "Build a raised timber deck with stairs on the south side of the house.",
            Address("220 Mill Lane", "70002"), 14_500m, Ago(40));
        Submit(deck, applicantB, Residential, Ago(39));
        deck.StartReview(clerk, Ago(37));
        deck.Approve(admin, null, Ago(35));
        deck.RecordFeePayment(clerk, Ago(34));
        Inspection failed = deck.ScheduleInspection(clerk, SampleIdentities.InspectorOne, Ago(20), Ago(30));
        deck.RecordInspectionResult(inspector, failed.Id, passed: false, "The stair rail is 30 inches high; the code needs 36.", Ago(20));
        all.Add(deck);

        // 7. Issued: a block party, which needs no inspection.
        PermitApplication party = Draft(7, BlockParty, applicantA, "Close the end of Lantern Row for a summer block party from noon to eight.",
            Address("14 Lantern Row", "70001"), 0m, Ago(30));
        Submit(party, applicantA, BlockParty, Ago(29));
        party.StartReview(clerk, Ago(28));
        party.Approve(clerk, null, Ago(27));
        party.RecordFeePayment(clerk, Ago(26));
        party.Issue(clerk, types[BlockParty], Ago(26, 1));
        all.Add(party);

        // 8. Issued: a food truck, after a passed inspection.
        PermitApplication truck = Draft(8, FoodVendor, applicantC, "Sell wood-fired flatbreads from a trailer in the Founders Green car park.",
            Address("3 Market Square", "70001"), 0m, Ago(60));
        Submit(truck, applicantC, FoodVendor, Ago(59));
        truck.StartReview(clerk, Ago(58));
        truck.Approve(clerk, null, Ago(56));
        truck.RecordFeePayment(clerk, Ago(55));
        Inspection passed = truck.ScheduleInspection(clerk, SampleIdentities.InspectorTwo, Ago(48), Ago(54));
        truck.RecordInspectionResult(new Actor(SampleIdentities.InspectorTwo, ActorRole.Inspector), passed.Id, passed: true, "Food-safe surfaces and fire suppression in place.", Ago(48));
        truck.Issue(admin, types[FoodVendor], Ago(47));
        all.Add(truck);

        // 9. Denied, with the reason the applicant was given.
        PermitApplication festival = Draft(9, StreetFestival, applicantB, "Close Main Street between Mill Lane and the river for a two-day food festival.",
            Address("220 Mill Lane", "70002"), 0m, Ago(25));
        Submit(festival, applicantB, StreetFestival, Ago(24));
        festival.StartReview(clerk, Ago(22));
        festival.Deny(clerk, "Main Street is the fire route to the east side and cannot be closed for two days. A single-day closure of Mill Lane would be considered.", Ago(20));
        all.Add(festival);

        // 10. Withdrawn by the applicant.
        PermitApplication banner = Draft(10, TemporaryBanner, applicantC, "Hang a banner across the bakery window for the autumn opening.",
            Address("3 Market Square", "70001"), 0m, Ago(18));
        Submit(banner, applicantC, TemporaryBanner, Ago(17));
        banner.Withdraw(applicantC, "The opening has moved to spring.", Ago(16));
        all.Add(banner);

        // 11. Approved and paid, ready for a clerk to issue.
        PermitApplication stall = Draft(11, MarketStall, applicantA, "A produce stall at the Saturday market, selling honey and preserves.",
            Address("14 Lantern Row", "70001"), 0m, Ago(9));
        Submit(stall, applicantA, MarketStall, Ago(8));
        stall.StartReview(clerk, Ago(6));
        stall.Approve(clerk, null, Ago(5));
        stall.RecordFeePayment(clerk, Ago(4));
        all.Add(stall);

        // 12. A second food truck application, just submitted.
        PermitApplication cart = Draft(12, FoodVendor, applicantB, "Sell coffee and pastries from a bicycle cart near the library.",
            Address("220 Mill Lane", "70002"), 0m, Ago(2));
        all.Add(Submit(cart, applicantB, FoodVendor, Ago(1)));

        return all;
    }

    private static Guid TypeId(int n) => Guid.Parse($"b1000000-0000-4000-8000-{n:D12}");

    private static Guid ApplicationId(int n) => Guid.Parse($"f1000000-0000-4000-8000-{n:D12}");

    private static PostalAddress Address(string line1, string postalCode) =>
        PostalAddress.Create(line1, null, "Bramblewick", "TG", postalCode);
}

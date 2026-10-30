using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;
using CivicPermits.Domain.Permits;

namespace CivicPermits.Domain.Tests;

public class PermitApplicationTests
{
    [Fact]
    public void A_new_draft_belongs_to_its_applicant_and_has_no_number_or_fee_yet()
    {
        PermitApplication application = Sample.Draft();

        Assert.Equal(ApplicationStatus.Draft, application.Status);
        Assert.Equal(Sample.Applicant.Id, application.ApplicantId);
        Assert.Null(application.Number);
        Assert.Equal(Money.Zero, application.FeeAssessed);
        Assert.Empty(application.History);
        Assert.Equal(Sample.Now, application.CreatedOn);
    }

    [Fact]
    public void A_draft_cannot_be_started_for_a_retired_permit_type()
    {
        PermitType type = Sample.BuildingPermit();
        type.Deactivate();

        DomainRuleException failure = Assert.Throws<DomainRuleException>(() => Sample.Draft(type));

        Assert.Equal("application.permit_type_retired", failure.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too short")]
    public void A_project_description_that_is_too_short_is_refused(string? description)
    {
        DomainRuleException failure = Assert.Throws<DomainRuleException>(() => PermitApplication.CreateDraft(
            Guid.NewGuid(), Sample.BuildingPermit(), Sample.Applicant.Id, description, Sample.Address(), Money.Zero, Sample.Now));

        Assert.Equal("application.description", failure.Code);
    }

    [Fact]
    public void Checking_the_details_lists_the_description_and_every_address_problem_together()
    {
        IReadOnlyList<RuleViolation> found = PermitApplication.CheckDetails("short", "", null, "", "", "123");

        Assert.Equal(
            ["application.description", "address.line1", "address.city", "address.region", "address.postal_code"],
            found.Select(violation => violation.Code));
        Assert.Empty(PermitApplication.CheckDetails("Build a covered porch at the back.", "100 Lantern Row", null, "Bramblewick", "TG", "70001"));
    }

    [Fact]
    public void A_project_description_that_is_too_long_is_refused()
    {
        DomainRuleException failure = Assert.Throws<DomainRuleException>(() => PermitApplication.CreateDraft(
            Guid.NewGuid(), Sample.BuildingPermit(), Sample.Applicant.Id, new string('x', 2_001), Sample.Address(), Money.Zero, Sample.Now));

        Assert.Equal("application.description", failure.Code);
    }

    [Fact]
    public void The_applicant_can_revise_a_draft_but_nobody_else_can()
    {
        PermitApplication application = Sample.Draft();
        PostalAddress newAddress = PostalAddress.Create("12 Mill Lane", null, "Bramblewick", "TG", "70002");

        application.Revise(Sample.Applicant, "Replace the fence along the north edge.", newAddress, Money.FromDollars(3_000m));

        Assert.Equal("Replace the fence along the north edge.", application.ProjectDescription);
        Assert.Equal(newAddress, application.SiteAddress);
        Assert.Equal(Money.FromDollars(3_000m), application.DeclaredValuation);

        Assert.Equal(
            "workflow.not_your_application",
            Assert.Throws<DomainRuleException>(
                () => application.Revise(Sample.OtherApplicant, "Replace the fence along the north edge.", newAddress, Money.Zero)).Code);

        Assert.Equal(
            "workflow.not_your_application",
            Assert.Throws<DomainRuleException>(
                () => application.Revise(Sample.Clerk, "Replace the fence along the north edge.", newAddress, Money.Zero)).Code);
    }

    [Fact]
    public void An_application_in_review_cannot_be_edited()
    {
        PermitApplication application = Sample.InReview();

        DomainRuleException failure = Assert.Throws<DomainRuleException>(
            () => application.Revise(Sample.Applicant, "Replace the fence along the north edge.", Sample.Address(), Money.Zero));

        Assert.Equal("application.not_editable", failure.Code);
    }

    [Fact]
    public void Submitting_assigns_the_number_works_out_the_fee_and_records_the_step()
    {
        PermitType type = Sample.BuildingPermit();
        PermitApplication application = Sample.Draft(type);

        application.Submit(Sample.Applicant, type, Sample.Number, Sample.Now.AddMinutes(5));

        Assert.Equal(ApplicationStatus.Submitted, application.Status);
        Assert.Equal(Sample.Number, application.Number);
        Assert.Equal(Money.FromDollars(450m), application.FeeAssessed);
        Assert.True(application.InspectionRequired);
        Assert.Equal(Sample.Now.AddMinutes(5), application.SubmittedOn);

        StatusChange change = Assert.Single(application.History);
        Assert.Equal(ApplicationStatus.Draft, change.From);
        Assert.Equal(ApplicationStatus.Submitted, change.To);
        Assert.Equal(ApplicationAction.Submit, change.Action);
        Assert.Equal(ActorRole.Applicant, change.ActorRole);
    }

    [Fact]
    public void A_permit_whose_fee_depends_on_value_needs_a_value_before_it_can_be_sent()
    {
        PermitType type = Sample.BuildingPermit();
        PermitApplication application = Sample.Draft(type, Money.Zero);

        DomainRuleException failure = Assert.Throws<DomainRuleException>(
            () => application.Submit(Sample.Applicant, type, Sample.Number, Sample.Now));

        Assert.Equal("application.valuation_required", failure.Code);
        Assert.Equal(ApplicationStatus.Draft, application.Status);
        Assert.Null(application.Number);
    }

    [Fact]
    public void A_flat_fee_permit_can_be_sent_without_a_value()
    {
        PermitType flat = Sample.BuildingPermit(valuationRateBasisPoints: 0);
        PermitApplication application = Sample.Draft(flat, Money.Zero);

        application.Submit(Sample.Applicant, flat, Sample.Number, Sample.Now);

        Assert.Equal(Money.FromDollars(150m), application.FeeAssessed);
    }

    [Fact]
    public void Submitting_with_a_different_permit_type_is_refused()
    {
        PermitApplication application = Sample.Draft();
        PermitType other = PermitType.Create(
            Guid.NewGuid(), PermitCategory.Sign, "Sign permit", "A sign on private property.", Money.Zero, 0, false, 365, 5);

        DomainRuleException failure = Assert.Throws<DomainRuleException>(
            () => application.Submit(Sample.Applicant, other, Sample.Number, Sample.Now));

        Assert.Equal("application.wrong_permit_type", failure.Code);
    }

    [Fact]
    public void Corrections_keep_the_number_and_the_first_submission_date_and_the_fee_is_worked_out_again()
    {
        PermitType type = Sample.BuildingPermit();
        PermitApplication application = Sample.InReview(type);
        DateTimeOffset firstSubmitted = application.SubmittedOn!.Value;

        application.RequestCorrections(Sample.Clerk, "The declared value looks too low for this work.", Sample.Now.AddHours(2));
        application.Revise(Sample.Applicant, "Add a covered porch to the rear of the house.", Sample.Address(), Money.FromDollars(40_000m));
        application.Submit(Sample.Applicant, type, Sample.Number, Sample.Now.AddDays(1));

        Assert.Equal(ApplicationStatus.Submitted, application.Status);
        Assert.Equal(firstSubmitted, application.SubmittedOn);
        Assert.Equal(Sample.Number, application.Number);
        Assert.Equal(Money.FromDollars(650m), application.FeeAssessed);
    }

    [Fact]
    public void A_second_submission_cannot_change_the_number()
    {
        PermitType type = Sample.BuildingPermit();
        PermitApplication application = Sample.InReview(type);
        application.RequestCorrections(Sample.Clerk, "The declared value looks too low for this work.", Sample.Now.AddHours(2));

        DomainRuleException failure = Assert.Throws<DomainRuleException>(
            () => application.Submit(Sample.Applicant, type, PermitNumber.From(2026, 18), Sample.Now.AddDays(1)));

        Assert.Equal("application.number_changed", failure.Code);
        Assert.Equal(ApplicationStatus.CorrectionsRequested, application.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no")]
    public void Asking_for_corrections_needs_a_reason(string reason)
    {
        PermitApplication application = Sample.InReview();

        DomainRuleException failure = Assert.Throws<DomainRuleException>(
            () => application.RequestCorrections(Sample.Clerk, reason, Sample.Now.AddHours(2)));

        Assert.Equal("application.reason_required", failure.Code);
        Assert.Equal(ApplicationStatus.InReview, application.Status);
    }

    [Fact]
    public void Denying_needs_a_reason_and_keeps_it_as_the_decision_note()
    {
        PermitApplication application = Sample.InReview();

        Assert.Equal(
            "application.reason_required",
            Assert.Throws<DomainRuleException>(() => application.Deny(Sample.Clerk, " ", Sample.Now.AddHours(2))).Code);

        application.Deny(Sample.Clerk, "  The lot is in a protected area.  ", Sample.Now.AddHours(2));

        Assert.Equal(ApplicationStatus.Denied, application.Status);
        Assert.Equal("The lot is in a protected area.", application.DecisionNote);
        Assert.Equal(Sample.Now.AddHours(2), application.DecidedOn);
    }

    [Fact]
    public void Approval_needs_no_note_and_a_blank_one_is_not_kept()
    {
        PermitApplication application = Sample.InReview();

        application.Approve(Sample.Administrator, "   ", Sample.Now.AddHours(2));

        Assert.Equal(ApplicationStatus.Approved, application.Status);
        Assert.Null(application.DecisionNote);
        Assert.Equal(Sample.Now.AddHours(2), application.DecidedOn);
    }

    [Fact]
    public void Another_applicant_cannot_withdraw_someone_elses_application()
    {
        PermitApplication application = Sample.Submitted();

        DomainRuleException failure = Assert.Throws<DomainRuleException>(
            () => application.Withdraw(Sample.OtherApplicant, null, Sample.Now.AddHours(1)));

        Assert.Equal("workflow.not_your_application", failure.Code);
        Assert.Equal(ApplicationStatus.Submitted, application.Status);
    }

    [Fact]
    public void An_applicant_cannot_review_their_own_application()
    {
        PermitApplication application = Sample.Submitted();

        DomainRuleException failure = Assert.Throws<DomainRuleException>(
            () => application.StartReview(Sample.Applicant, Sample.Now.AddHours(1)));

        Assert.Equal("workflow.role_not_allowed", failure.Code);
    }

    [Fact]
    public void A_clerk_cannot_withdraw_for_the_applicant()
    {
        PermitApplication application = Sample.Submitted();

        DomainRuleException failure = Assert.Throws<DomainRuleException>(
            () => application.Withdraw(Sample.Clerk, null, Sample.Now.AddHours(1)));

        Assert.Equal("workflow.role_not_allowed", failure.Code);
    }

    [Fact]
    public void Withdrawal_keeps_the_reason_in_the_history_and_ends_the_application()
    {
        PermitApplication application = Sample.Submitted();

        application.Withdraw(Sample.Applicant, "  We are selling the house.  ", Sample.Now.AddHours(1));

        Assert.Equal(ApplicationStatus.Withdrawn, application.Status);
        Assert.Equal("We are selling the house.", application.History[^1].Note);
    }

    [Fact]
    public void Staff_can_record_the_fee_once_and_only_after_approval()
    {
        PermitApplication inReview = Sample.InReview();

        Assert.Equal(
            "payment.not_approved",
            Assert.Throws<DomainRuleException>(() => inReview.RecordFeePayment(Sample.Clerk, Sample.Now.AddHours(3))).Code);

        PermitApplication approved = Sample.Approved();

        Assert.Equal(
            "workflow.role_not_allowed",
            Assert.Throws<DomainRuleException>(() => approved.RecordFeePayment(Sample.Applicant, Sample.Now.AddHours(3))).Code);

        approved.RecordFeePayment(Sample.Clerk, Sample.Now.AddHours(3));

        Assert.Equal(Sample.Now.AddHours(3), approved.FeePaidOn);
        Assert.Equal(
            "payment.already_recorded",
            Assert.Throws<DomainRuleException>(() => approved.RecordFeePayment(Sample.Administrator, Sample.Now.AddHours(4))).Code);
        Assert.Equal(Sample.Now.AddHours(3), approved.FeePaidOn);
    }

    [Fact]
    public void A_permit_cannot_be_issued_before_the_fee_is_paid()
    {
        PermitType type = Sample.BuildingPermit(requiresInspection: false);
        PermitApplication application = Sample.Approved(type);

        DomainRuleException failure = Assert.Throws<DomainRuleException>(
            () => application.Issue(Sample.Clerk, type, Sample.Now.AddDays(1)));

        Assert.Equal("issue.fee_unpaid", failure.Code);
        Assert.Equal(ApplicationStatus.Approved, application.Status);
    }

    [Fact]
    public void A_permit_that_needs_an_inspection_cannot_be_issued_until_one_has_passed()
    {
        PermitType type = Sample.BuildingPermit();
        PermitApplication application = Sample.Approved(type);
        application.RecordFeePayment(Sample.Clerk, Sample.Now.AddHours(3));

        Assert.Equal(
            "issue.inspection_not_passed",
            Assert.Throws<DomainRuleException>(() => application.Issue(Sample.Clerk, type, Sample.Now.AddDays(1))).Code);

        Inspection inspection = application.ScheduleInspection(
            Sample.Clerk, Sample.Inspector.Id, Sample.Now.AddDays(3), Sample.Now.AddHours(4));

        Assert.Equal(
            "issue.inspection_not_passed",
            Assert.Throws<DomainRuleException>(() => application.Issue(Sample.Clerk, type, Sample.Now.AddDays(1))).Code);

        application.RecordInspectionResult(Sample.Inspector, inspection.Id, passed: true, "Framing and footings are sound.", Sample.Now.AddDays(3));
        application.Issue(Sample.Clerk, type, Sample.Now.AddDays(4));

        Assert.Equal(ApplicationStatus.Issued, application.Status);
    }

    [Fact]
    public void A_failed_latest_inspection_blocks_issue_until_a_later_one_passes()
    {
        PermitType type = Sample.BuildingPermit();
        PermitApplication application = Sample.Approved(type);
        application.RecordFeePayment(Sample.Clerk, Sample.Now.AddHours(3));

        Inspection first = application.ScheduleInspection(Sample.Clerk, Sample.Inspector.Id, Sample.Now.AddDays(3), Sample.Now.AddHours(4));
        application.RecordInspectionResult(Sample.Inspector, first.Id, passed: false, "The rail on the stairs is too low.", Sample.Now.AddDays(3));

        Assert.Equal(
            "issue.inspection_not_passed",
            Assert.Throws<DomainRuleException>(() => application.Issue(Sample.Clerk, type, Sample.Now.AddDays(4))).Code);

        Inspection second = application.ScheduleInspection(Sample.Clerk, Sample.Inspector.Id, Sample.Now.AddDays(10), Sample.Now.AddDays(4));
        application.RecordInspectionResult(Sample.Inspector, second.Id, passed: true, null, Sample.Now.AddDays(10));
        application.Issue(Sample.Clerk, type, Sample.Now.AddDays(11));

        Assert.Equal(ApplicationStatus.Issued, application.Status);
    }

    [Fact]
    public void An_earlier_pass_does_not_cover_a_later_failure()
    {
        PermitType type = Sample.BuildingPermit();
        PermitApplication application = Sample.Approved(type);
        application.RecordFeePayment(Sample.Clerk, Sample.Now.AddHours(3));

        Inspection first = application.ScheduleInspection(Sample.Clerk, Sample.Inspector.Id, Sample.Now.AddDays(3), Sample.Now.AddHours(4));
        application.RecordInspectionResult(Sample.Inspector, first.Id, passed: true, null, Sample.Now.AddDays(3));

        Inspection second = application.ScheduleInspection(Sample.Clerk, Sample.Inspector.Id, Sample.Now.AddDays(9), Sample.Now.AddDays(4));
        application.RecordInspectionResult(Sample.Inspector, second.Id, passed: false, "A new crack appeared in the slab.", Sample.Now.AddDays(9));

        Assert.Equal(
            "issue.inspection_not_passed",
            Assert.Throws<DomainRuleException>(() => application.Issue(Sample.Clerk, type, Sample.Now.AddDays(10))).Code);
    }

    [Fact]
    public void An_issued_permit_records_when_it_was_issued_and_when_it_expires()
    {
        PermitType type = Sample.BuildingPermit(requiresInspection: false);
        PermitApplication application = Sample.Approved(type);
        application.RecordFeePayment(Sample.Clerk, Sample.Now.AddHours(3));

        application.Issue(Sample.Administrator, type, Sample.Now.AddDays(1));

        Assert.Equal(Sample.Now.AddDays(1), application.IssuedOn);
        Assert.Equal(new DateOnly(2027, 11, 3), application.ExpiresOn);
        Assert.Equal(ApplicationAction.Issue, application.History[^1].Action);
    }

    [Fact]
    public void Issuing_with_a_different_permit_type_is_refused()
    {
        PermitApplication application = Sample.Approved(Sample.BuildingPermit(requiresInspection: false));
        PermitType other = PermitType.Create(
            Guid.NewGuid(), PermitCategory.Sign, "Sign permit", "A sign on private property.", Money.Zero, 0, false, 365, 5);

        DomainRuleException failure = Assert.Throws<DomainRuleException>(
            () => application.Issue(Sample.Clerk, other, Sample.Now.AddDays(1)));

        Assert.Equal("application.wrong_permit_type", failure.Code);
    }

    [Fact]
    public void The_history_lists_every_step_in_order_with_who_took_it()
    {
        PermitType type = Sample.BuildingPermit(requiresInspection: false);
        PermitApplication application = Sample.Approved(type);
        application.RecordFeePayment(Sample.Clerk, Sample.Now.AddHours(3));
        application.Issue(Sample.Clerk, type, Sample.Now.AddHours(4));

        Assert.Equal(
            [ApplicationAction.Submit, ApplicationAction.StartReview, ApplicationAction.Approve, ApplicationAction.Issue],
            application.History.Select(change => change.Action));
        Assert.Equal(
            [ApplicationStatus.Submitted, ApplicationStatus.InReview, ApplicationStatus.Approved, ApplicationStatus.Issued],
            application.History.Select(change => change.To));
        Assert.Equal(application.History.OrderBy(change => change.At), application.History);
        Assert.Equal(application.History.Count, application.History.Select(change => change.Id).Distinct().Count());
    }

    [Fact]
    public void A_refused_step_leaves_no_trace_in_the_history()
    {
        PermitApplication application = Sample.Submitted();
        int before = application.History.Count;

        Assert.Throws<DomainRuleException>(() => application.Approve(Sample.Clerk, null, Sample.Now.AddHours(1)));

        Assert.Equal(before, application.History.Count);
        Assert.Null(application.DecidedOn);
    }

    // ---- Inspections ----

    [Fact]
    public void An_inspection_can_be_booked_only_for_an_approved_application_that_needs_one()
    {
        Assert.Equal(
            "inspection.not_approved",
            Assert.Throws<DomainRuleException>(() => Sample.InReview().ScheduleInspection(
                Sample.Clerk, Sample.Inspector.Id, Sample.Now.AddDays(3), Sample.Now.AddHours(4))).Code);

        PermitApplication noInspection = Sample.Approved(Sample.BuildingPermit(requiresInspection: false));

        Assert.Equal(
            "inspection.not_required",
            Assert.Throws<DomainRuleException>(() => noInspection.ScheduleInspection(
                Sample.Clerk, Sample.Inspector.Id, Sample.Now.AddDays(3), Sample.Now.AddHours(4))).Code);
    }

    [Fact]
    public void Only_staff_can_book_an_inspection()
    {
        PermitApplication application = Sample.Approved();

        Assert.Equal(
            "workflow.role_not_allowed",
            Assert.Throws<DomainRuleException>(() => application.ScheduleInspection(
                Sample.Applicant, Sample.Inspector.Id, Sample.Now.AddDays(3), Sample.Now.AddHours(4))).Code);
        Assert.Equal(
            "workflow.role_not_allowed",
            Assert.Throws<DomainRuleException>(() => application.ScheduleInspection(
                Sample.Inspector, Sample.Inspector.Id, Sample.Now.AddDays(3), Sample.Now.AddHours(4))).Code);
    }

    [Theory]
    [InlineData(-1, "inspection.in_the_past")]
    [InlineData(0, "inspection.in_the_past")]
    [InlineData(121, "inspection.too_far_ahead")]
    public void An_inspection_must_be_booked_inside_the_allowed_window(int daysFromNow, string expectedCode)
    {
        PermitApplication application = Sample.Approved();
        DateTimeOffset now = Sample.Now.AddHours(4);

        DomainRuleException failure = Assert.Throws<DomainRuleException>(
            () => application.ScheduleInspection(Sample.Clerk, Sample.Inspector.Id, now.AddDays(daysFromNow), now));

        Assert.Equal(expectedCode, failure.Code);
        Assert.Empty(application.Inspections);
    }

    [Fact]
    public void The_farthest_allowed_booking_is_exactly_120_days_ahead()
    {
        PermitApplication application = Sample.Approved();
        DateTimeOffset now = Sample.Now.AddHours(4);

        application.ScheduleInspection(Sample.Clerk, Sample.Inspector.Id, now.AddDays(120), now);

        Assert.Single(application.Inspections);
    }

    [Fact]
    public void Only_one_inspection_can_be_booked_at_a_time()
    {
        PermitApplication application = Sample.Approved();
        Inspection first = application.ScheduleInspection(Sample.Clerk, Sample.Inspector.Id, Sample.Now.AddDays(3), Sample.Now.AddHours(4));

        Assert.Equal(
            "inspection.already_scheduled",
            Assert.Throws<DomainRuleException>(() => application.ScheduleInspection(
                Sample.Clerk, Sample.Inspector.Id, Sample.Now.AddDays(5), Sample.Now.AddHours(5))).Code);

        application.CancelInspection(Sample.Clerk, first.Id, "The inspector is unwell.", Sample.Now.AddHours(6));
        application.ScheduleInspection(Sample.Clerk, Sample.OtherInspector.Id, Sample.Now.AddDays(5), Sample.Now.AddHours(7));

        Assert.Equal(2, application.Inspections.Count);
        Assert.Equal(InspectionStatus.Cancelled, first.Status);
        Assert.Equal("The inspector is unwell.", first.Notes);
    }

    [Fact]
    public void An_inspection_needs_an_inspector()
    {
        PermitApplication application = Sample.Approved();

        DomainRuleException failure = Assert.Throws<DomainRuleException>(() => application.ScheduleInspection(
            Sample.Clerk, Guid.Empty, Sample.Now.AddDays(3), Sample.Now.AddHours(4)));

        Assert.Equal("inspection.inspector", failure.Code);
    }

    [Fact]
    public void Only_the_assigned_inspector_or_an_administrator_can_record_a_result()
    {
        PermitApplication application = Sample.Approved();
        Inspection inspection = application.ScheduleInspection(Sample.Clerk, Sample.Inspector.Id, Sample.Now.AddDays(3), Sample.Now.AddHours(4));

        foreach (Actor outsider in new[] { Sample.OtherInspector, Sample.Clerk, Sample.Applicant })
        {
            Assert.Equal(
                "inspection.not_assigned",
                Assert.Throws<DomainRuleException>(() => application.RecordInspectionResult(
                    outsider, inspection.Id, passed: true, null, Sample.Now.AddDays(3))).Code);
        }

        Assert.Equal(InspectionStatus.Scheduled, inspection.Status);

        application.RecordInspectionResult(Sample.Administrator, inspection.Id, passed: true, null, Sample.Now.AddDays(3));

        Assert.Equal(InspectionStatus.Passed, inspection.Status);
        Assert.Equal(Sample.Now.AddDays(3), inspection.ClosedOn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad")]
    public void A_failed_inspection_says_what_failed(string? notes)
    {
        PermitApplication application = Sample.Approved();
        Inspection inspection = application.ScheduleInspection(Sample.Clerk, Sample.Inspector.Id, Sample.Now.AddDays(3), Sample.Now.AddHours(4));

        DomainRuleException failure = Assert.Throws<DomainRuleException>(() => application.RecordInspectionResult(
            Sample.Inspector, inspection.Id, passed: false, notes, Sample.Now.AddDays(3)));

        Assert.Equal("inspection.failure_needs_notes", failure.Code);
        Assert.Equal(InspectionStatus.Scheduled, inspection.Status);
    }

    [Fact]
    public void A_closed_or_unknown_inspection_cannot_be_closed_again()
    {
        PermitApplication application = Sample.Approved();
        Inspection inspection = application.ScheduleInspection(Sample.Clerk, Sample.Inspector.Id, Sample.Now.AddDays(3), Sample.Now.AddHours(4));
        application.RecordInspectionResult(Sample.Inspector, inspection.Id, passed: true, null, Sample.Now.AddDays(3));

        Assert.Equal(
            "inspection.closed",
            Assert.Throws<DomainRuleException>(() => application.RecordInspectionResult(
                Sample.Inspector, inspection.Id, passed: false, "Changed my mind about it.", Sample.Now.AddDays(4))).Code);
        Assert.Equal(
            "inspection.closed",
            Assert.Throws<DomainRuleException>(() => application.CancelInspection(Sample.Clerk, inspection.Id, null, Sample.Now.AddDays(4))).Code);
        Assert.Equal(
            "inspection.unknown",
            Assert.Throws<DomainRuleException>(() => application.CancelInspection(Sample.Clerk, Guid.NewGuid(), null, Sample.Now.AddDays(4))).Code);
        Assert.Equal(InspectionStatus.Passed, inspection.Status);
    }

    [Fact]
    public void Only_staff_can_cancel_an_inspection()
    {
        PermitApplication application = Sample.Approved();
        Inspection inspection = application.ScheduleInspection(Sample.Clerk, Sample.Inspector.Id, Sample.Now.AddDays(3), Sample.Now.AddHours(4));

        DomainRuleException failure = Assert.Throws<DomainRuleException>(
            () => application.CancelInspection(Sample.Applicant, inspection.Id, null, Sample.Now.AddHours(5)));

        Assert.Equal("workflow.role_not_allowed", failure.Code);
        Assert.Equal(InspectionStatus.Scheduled, inspection.Status);
    }
}

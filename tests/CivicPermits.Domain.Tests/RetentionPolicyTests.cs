using CivicPermits.Domain.Applications;

namespace CivicPermits.Domain.Tests;

/// <summary>How long applications and accounts are kept, to the day.</summary>
public class RetentionPolicyTests
{
    private static readonly DateTimeOffset LastChange = new(2026, 3, 15, 9, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ApplicationStatus.Approved, RetentionClass.Decided)]
    [InlineData(ApplicationStatus.Denied, RetentionClass.Decided)]
    [InlineData(ApplicationStatus.Issued, RetentionClass.Decided)]
    [InlineData(ApplicationStatus.Draft, RetentionClass.Draft)]
    [InlineData(ApplicationStatus.Withdrawn, RetentionClass.Withdrawn)]
    public void Each_finished_status_falls_under_one_period(ApplicationStatus status, RetentionClass expected) =>
        Assert.Equal(expected, RetentionPolicy.ClassOf(status));

    [Theory]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.InReview)]
    [InlineData(ApplicationStatus.CorrectionsRequested)]
    public void An_application_in_progress_is_never_removed_by_age(ApplicationStatus status)
    {
        Assert.Null(RetentionPolicy.ClassOf(status));
        Assert.False(RetentionPolicy.IsExpired(status, LastChange, LastChange.AddYears(100)));
    }

    [Fact]
    public void Every_status_is_either_in_progress_or_under_a_period_so_a_new_status_cannot_be_forgotten()
    {
        ApplicationStatus[] inProgress = [ApplicationStatus.Submitted, ApplicationStatus.InReview, ApplicationStatus.CorrectionsRequested];

        foreach (ApplicationStatus status in Enum.GetValues<ApplicationStatus>())
        {
            Assert.True(
                inProgress.Contains(status) == (RetentionPolicy.ClassOf(status) is null),
                $"{status} has been neither given a retention period nor listed as in progress.");
        }
    }

    [Theory]
    [InlineData(ApplicationStatus.Approved, 7)]
    [InlineData(ApplicationStatus.Denied, 7)]
    [InlineData(ApplicationStatus.Issued, 7)]
    [InlineData(ApplicationStatus.Draft, 1)]
    [InlineData(ApplicationStatus.Withdrawn, 1)]
    public void An_application_is_kept_for_its_whole_period_and_not_a_day_longer(ApplicationStatus status, int years)
    {
        DateTimeOffset end = LastChange.AddYears(years);

        Assert.False(RetentionPolicy.IsExpired(status, LastChange, end.AddDays(-1)), "A day before the period ends it must still be kept.");
        Assert.False(RetentionPolicy.IsExpired(status, LastChange, end.AddTicks(-1)), "Even a moment before, it must still be kept.");
        Assert.True(RetentionPolicy.IsExpired(status, LastChange, end), "At the end of the period it is removed.");
        Assert.True(RetentionPolicy.IsExpired(status, LastChange, end.AddDays(1)));
    }

    [Fact]
    public void An_account_with_no_application_is_kept_two_years_after_it_was_last_used()
    {
        DateTimeOffset end = LastChange.AddYears(2);

        Assert.False(RetentionPolicy.AccountIsExpired(LastChange, end.AddDays(-1)));
        Assert.True(RetentionPolicy.AccountIsExpired(LastChange, end));
        Assert.True(RetentionPolicy.AccountIsExpired(LastChange, end.AddDays(1)));
    }

    [Fact]
    public void The_periods_are_the_ones_written_in_the_security_policy()
    {
        Assert.Equal(7, RetentionPolicy.DecidedApplicationYears);
        Assert.Equal(1, RetentionPolicy.DraftOrWithdrawnApplicationYears);
        Assert.Equal(2, RetentionPolicy.InactiveAccountYears);
    }

    // ---- Where the period is counted from ----

    [Fact]
    public void A_draft_has_not_changed_since_it_was_made()
    {
        PermitApplication draft = Sample.Draft();

        Assert.Equal(Sample.Now, draft.LastChangedOn());
    }

    [Fact]
    public void The_last_change_is_the_latest_step_in_its_history()
    {
        PermitApplication approved = Sample.Approved();

        Assert.Equal(approved.History.Max(change => change.At), approved.LastChangedOn());
        Assert.True(approved.LastChangedOn() > approved.CreatedOn);
    }

    [Fact]
    public void Closing_an_inspection_counts_as_a_change_and_booking_one_does_not()
    {
        PermitApplication approved = Sample.Approved();
        DateTimeOffset before = approved.LastChangedOn();

        Inspection booked = approved.ScheduleInspection(Sample.Clerk, Sample.Inspector.Id, before.AddDays(100), before.AddMinutes(1));
        Assert.Equal(before, approved.LastChangedOn()); // booking is not a step in the history, and its date, months away, is not a change

        DateTimeOffset result = before.AddDays(3);
        approved.RecordInspectionResult(Sample.Inspector, booked.Id, passed: true, notes: null, result);

        Assert.True(approved.LastChangedOn() >= result);
        Assert.True(approved.LastChangedOn() < before.AddDays(100));
    }
}

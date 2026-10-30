using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;
using CivicPermits.Domain.Permits;

namespace CivicPermits.Domain.Tests;

/// <summary>
/// Checks the workflow table against a second, independent statement of the office's rules written out
/// here in plain terms, and then drives the real aggregate through every status, action and role to prove
/// the aggregate obeys the table.
/// </summary>
public class WorkflowTests
{
    private static readonly ApplicationStatus[] Statuses = Enum.GetValues<ApplicationStatus>();
    private static readonly ApplicationAction[] Actions = Enum.GetValues<ApplicationAction>();
    private static readonly ActorRole[] Roles = Enum.GetValues<ActorRole>();

    /// <summary>The rules as the office would state them. Deliberately not derived from the table.</summary>
    private static readonly (ApplicationStatus From, ApplicationAction Action, ApplicationStatus To, ActorRole[] Who)[] Specification =
    [
        (ApplicationStatus.Draft, ApplicationAction.Submit, ApplicationStatus.Submitted, [ActorRole.Applicant]),
        (ApplicationStatus.CorrectionsRequested, ApplicationAction.Submit, ApplicationStatus.Submitted, [ActorRole.Applicant]),
        (ApplicationStatus.Submitted, ApplicationAction.StartReview, ApplicationStatus.InReview, [ActorRole.Clerk, ActorRole.Administrator]),
        (ApplicationStatus.InReview, ApplicationAction.RequestCorrections, ApplicationStatus.CorrectionsRequested, [ActorRole.Clerk, ActorRole.Administrator]),
        (ApplicationStatus.InReview, ApplicationAction.Approve, ApplicationStatus.Approved, [ActorRole.Clerk, ActorRole.Administrator]),
        (ApplicationStatus.InReview, ApplicationAction.Deny, ApplicationStatus.Denied, [ActorRole.Clerk, ActorRole.Administrator]),
        (ApplicationStatus.Approved, ApplicationAction.Issue, ApplicationStatus.Issued, [ActorRole.Clerk, ActorRole.Administrator]),
        (ApplicationStatus.Draft, ApplicationAction.Withdraw, ApplicationStatus.Withdrawn, [ActorRole.Applicant]),
        (ApplicationStatus.Submitted, ApplicationAction.Withdraw, ApplicationStatus.Withdrawn, [ActorRole.Applicant]),
        (ApplicationStatus.InReview, ApplicationAction.Withdraw, ApplicationStatus.Withdrawn, [ActorRole.Applicant]),
        (ApplicationStatus.CorrectionsRequested, ApplicationAction.Withdraw, ApplicationStatus.Withdrawn, [ActorRole.Applicant]),
        (ApplicationStatus.Approved, ApplicationAction.Withdraw, ApplicationStatus.Withdrawn, [ActorRole.Applicant]),
    ];

    public static TheoryData<ApplicationStatus, ApplicationAction, ActorRole> Everything()
    {
        TheoryData<ApplicationStatus, ApplicationAction, ActorRole> data = [];

        foreach (ApplicationStatus status in Statuses)
        {
            foreach (ApplicationAction action in Actions)
            {
                foreach (ActorRole role in Roles)
                {
                    data.Add(status, action, role);
                }
            }
        }

        return data;
    }

    [Fact]
    public void The_table_has_exactly_the_steps_the_specification_lists()
    {
        Assert.Equal(Specification.Length, ApplicationWorkflow.Transitions.Count);

        foreach (var (from, action, to, who) in Specification)
        {
            WorkflowTransition? step = ApplicationWorkflow.Find(from, action);

            Assert.NotNull(step);
            Assert.Equal(to, step.To);
            Assert.Equal(who.Order(), step.Roles.Order());
        }
    }

    [Fact]
    public void No_status_offers_the_same_action_twice()
    {
        var duplicates = ApplicationWorkflow.Transitions
            .GroupBy(step => (step.From, step.Action))
            .Where(group => group.Count() > 1)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Theory]
    [MemberData(nameof(Everything))]
    public void A_role_may_take_an_action_exactly_when_the_specification_says_so(
        ApplicationStatus status, ApplicationAction action, ActorRole role)
    {
        bool expected = Specification.Any(rule => rule.From == status && rule.Action == action && rule.Who.Contains(role));

        Assert.Equal(expected, ApplicationWorkflow.IsAllowed(status, action, role));
    }

    [Fact]
    public void The_final_statuses_are_the_ones_with_no_way_out()
    {
        foreach (ApplicationStatus status in Statuses)
        {
            bool hasWayOut = Specification.Any(rule => rule.From == status);

            Assert.Equal(!hasWayOut, ApplicationWorkflow.IsFinal(status));
        }

        Assert.All(
            new[] { ApplicationStatus.Denied, ApplicationStatus.Withdrawn, ApplicationStatus.Issued },
            status => Assert.True(ApplicationWorkflow.IsFinal(status)));
    }

    [Fact]
    public void Every_status_can_be_reached_from_a_new_draft()
    {
        HashSet<ApplicationStatus> reached = [ApplicationStatus.Draft];
        Queue<ApplicationStatus> frontier = new([ApplicationStatus.Draft]);

        while (frontier.TryDequeue(out ApplicationStatus current))
        {
            foreach (WorkflowTransition step in ApplicationWorkflow.Transitions.Where(step => step.From == current))
            {
                if (reached.Add(step.To))
                {
                    frontier.Enqueue(step.To);
                }
            }
        }

        Assert.Equal(Statuses.Order(), reached.Order());
    }

    [Fact]
    public void Every_status_that_is_not_final_leads_to_a_final_one()
    {
        foreach (ApplicationStatus start in Statuses.Where(status => !ApplicationWorkflow.IsFinal(status)))
        {
            HashSet<ApplicationStatus> seen = [start];
            Queue<ApplicationStatus> frontier = new([start]);
            bool endsSomewhere = false;

            while (frontier.TryDequeue(out ApplicationStatus current))
            {
                endsSomewhere |= ApplicationWorkflow.IsFinal(current);

                foreach (WorkflowTransition step in ApplicationWorkflow.Transitions.Where(step => step.From == current))
                {
                    if (seen.Add(step.To))
                    {
                        frontier.Enqueue(step.To);
                    }
                }
            }

            Assert.True(endsSomewhere, $"An application that is {start} can never finish.");
        }
    }

    [Fact]
    public void Inspectors_can_take_no_workflow_action_at_all()
    {
        Assert.DoesNotContain(ActorRole.Inspector, ApplicationWorkflow.Transitions.SelectMany(step => step.Roles));
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft, ActorRole.Applicant, new[] { ApplicationAction.Submit, ApplicationAction.Withdraw })]
    [InlineData(ApplicationStatus.InReview, ActorRole.Clerk, new[] { ApplicationAction.RequestCorrections, ApplicationAction.Approve, ApplicationAction.Deny })]
    [InlineData(ApplicationStatus.Issued, ActorRole.Administrator, new ApplicationAction[0])]
    public void The_actions_offered_to_a_role_are_the_ones_it_may_take(
        ApplicationStatus status, ActorRole role, ApplicationAction[] expected)
    {
        Assert.Equal(expected.Order(), ApplicationWorkflow.ActionsFor(status, role));
    }

    /// <summary>
    /// Takes a real application to each status, then tries every action as every kind of person, and checks the
    /// aggregate does what the table says: moves to the right status, or refuses with a workflow rule.
    /// </summary>
    [Theory]
    [MemberData(nameof(Everything))]
    public void The_aggregate_obeys_the_table_for_every_status_action_and_role(
        ApplicationStatus status, ApplicationAction action, ActorRole role)
    {
        PermitType type = Sample.BuildingPermit(requiresInspection: false);
        PermitApplication application = InStatus(status, type);
        Actor actor = ActorFor(role);

        WorkflowTransition? step = ApplicationWorkflow.Find(status, action);
        bool allowed = step is not null && step.Roles.Contains(role);

        if (allowed)
        {
            Attempt(application, type, action, actor);

            Assert.Equal(step!.To, application.Status);
            Assert.Equal(step.To, application.History[^1].To);
            Assert.Equal(status, application.History[^1].From);
            Assert.Equal(actor.Id, application.History[^1].ActorId);
        }
        else
        {
            int historyBefore = application.History.Count;
            DomainRuleException failure = Assert.Throws<DomainRuleException>(() => Attempt(application, type, action, actor));

            Assert.StartsWith("workflow.", failure.Code, StringComparison.Ordinal);
            Assert.Equal(status, application.Status);
            Assert.Equal(historyBefore, application.History.Count);
        }
    }

    private static Actor ActorFor(ActorRole role) => role switch
    {
        ActorRole.Applicant => Sample.Applicant,
        ActorRole.Clerk => Sample.Clerk,
        ActorRole.Administrator => Sample.Administrator,
        ActorRole.Inspector => Sample.Inspector,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static void Attempt(PermitApplication application, PermitType type, ApplicationAction action, Actor actor)
    {
        DateTimeOffset now = Sample.Now.AddDays(3);

        switch (action)
        {
            case ApplicationAction.Submit:
                application.Submit(actor, type, Sample.Number, now);
                break;
            case ApplicationAction.StartReview:
                application.StartReview(actor, now);
                break;
            case ApplicationAction.RequestCorrections:
                application.RequestCorrections(actor, "The site plan is missing the property lines.", now);
                break;
            case ApplicationAction.Approve:
                application.Approve(actor, "Meets the code.", now);
                break;
            case ApplicationAction.Deny:
                application.Deny(actor, "The project is outside what this permit covers.", now);
                break;
            case ApplicationAction.Issue:
                application.Issue(actor, type, now);
                break;
            case ApplicationAction.Withdraw:
                application.Withdraw(actor, "No longer going ahead.", now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    /// <summary>Takes a new application to a status by the real route, with the fee paid so it can be issued.</summary>
    private static PermitApplication InStatus(ApplicationStatus status, PermitType type)
    {
        PermitApplication application = Sample.Draft(type);

        if (status == ApplicationStatus.Draft)
        {
            return application;
        }

        if (status == ApplicationStatus.Withdrawn)
        {
            application.Withdraw(Sample.Applicant, null, Sample.Now.AddMinutes(1));

            return application;
        }

        application.Submit(Sample.Applicant, type, Sample.Number, Sample.Now.AddMinutes(1));

        if (status == ApplicationStatus.Submitted)
        {
            return application;
        }

        application.StartReview(Sample.Clerk, Sample.Now.AddHours(1));

        switch (status)
        {
            case ApplicationStatus.CorrectionsRequested:
                application.RequestCorrections(Sample.Clerk, "Please add the lot dimensions.", Sample.Now.AddHours(2));
                break;
            case ApplicationStatus.Denied:
                application.Deny(Sample.Clerk, "The lot is in a protected area.", Sample.Now.AddHours(2));
                break;
            case ApplicationStatus.Approved or ApplicationStatus.Issued:
                application.Approve(Sample.Clerk, null, Sample.Now.AddHours(2));
                application.RecordFeePayment(Sample.Clerk, Sample.Now.AddHours(3));

                if (status == ApplicationStatus.Issued)
                {
                    application.Issue(Sample.Clerk, type, Sample.Now.AddHours(4));
                }

                break;
            case ApplicationStatus.InReview:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        return application;
    }
}

namespace CivicPermits.Domain.Applications;

/// <summary>One allowed step in the process: from a status, by an action, to a status, by certain roles.</summary>
/// <param name="From">The status the application must be in.</param>
/// <param name="Action">The action taken.</param>
/// <param name="To">The status it moves to.</param>
/// <param name="Roles">The roles allowed to take the action.</param>
public sealed record WorkflowTransition(
    ApplicationStatus From,
    ApplicationAction Action,
    ApplicationStatus To,
    IReadOnlyList<ActorRole> Roles);

/// <summary>
/// The whole process as one table. Nothing else in the code decides what may happen next: the application
/// asks this table, the tests check this table against an independent statement of the rules, and the
/// documentation's diagram is drawn from it.
/// </summary>
public static class ApplicationWorkflow
{
    private static readonly ActorRole[] Applicant = [ActorRole.Applicant];
    private static readonly ActorRole[] Staff = [ActorRole.Clerk, ActorRole.Administrator];

    private static readonly WorkflowTransition[] Table =
    [
        // The applicant sends a draft, or a corrected application, to the office.
        new(ApplicationStatus.Draft, ApplicationAction.Submit, ApplicationStatus.Submitted, Applicant),
        new(ApplicationStatus.CorrectionsRequested, ApplicationAction.Submit, ApplicationStatus.Submitted, Applicant),

        // Staff review it.
        new(ApplicationStatus.Submitted, ApplicationAction.StartReview, ApplicationStatus.InReview, Staff),
        new(ApplicationStatus.InReview, ApplicationAction.RequestCorrections, ApplicationStatus.CorrectionsRequested, Staff),
        new(ApplicationStatus.InReview, ApplicationAction.Approve, ApplicationStatus.Approved, Staff),
        new(ApplicationStatus.InReview, ApplicationAction.Deny, ApplicationStatus.Denied, Staff),

        // Staff issue the permit once the fee is paid and any inspection has passed.
        new(ApplicationStatus.Approved, ApplicationAction.Issue, ApplicationStatus.Issued, Staff),

        // The applicant may withdraw at any point before the permit is issued or the application is denied.
        new(ApplicationStatus.Draft, ApplicationAction.Withdraw, ApplicationStatus.Withdrawn, Applicant),
        new(ApplicationStatus.Submitted, ApplicationAction.Withdraw, ApplicationStatus.Withdrawn, Applicant),
        new(ApplicationStatus.InReview, ApplicationAction.Withdraw, ApplicationStatus.Withdrawn, Applicant),
        new(ApplicationStatus.CorrectionsRequested, ApplicationAction.Withdraw, ApplicationStatus.Withdrawn, Applicant),
        new(ApplicationStatus.Approved, ApplicationAction.Withdraw, ApplicationStatus.Withdrawn, Applicant),
    ];

    /// <summary>Every allowed step.</summary>
    public static IReadOnlyList<WorkflowTransition> Transitions => Table;

    /// <summary>The step an action takes from a status, or null when the action is not possible there.</summary>
    public static WorkflowTransition? Find(ApplicationStatus from, ApplicationAction action) =>
        Table.FirstOrDefault(step => step.From == from && step.Action == action);

    /// <summary>Whether a role may take an action on an application in a status.</summary>
    public static bool IsAllowed(ApplicationStatus from, ApplicationAction action, ActorRole role) =>
        Find(from, action) is { } step && step.Roles.Contains(role);

    /// <summary>The actions a role could take on an application in a status, in a stable order.</summary>
    public static IReadOnlyList<ApplicationAction> ActionsFor(ApplicationStatus from, ActorRole role) =>
        [.. Table.Where(step => step.From == from && step.Roles.Contains(role)).Select(step => step.Action).Order()];

    /// <summary>Whether nothing more can happen to an application in this status.</summary>
    public static bool IsFinal(ApplicationStatus status) =>
        status is ApplicationStatus.Denied or ApplicationStatus.Withdrawn or ApplicationStatus.Issued;
}

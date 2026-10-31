using CivicPermits.Domain.Common;
using CivicPermits.Domain.Permits;

namespace CivicPermits.Domain.Applications;

/// <summary>
/// One application for a permit, from the first draft to the issued permit, a denial or a withdrawal.
/// </summary>
/// <remarks>
/// <para>
/// This is the aggregate root. Every change goes through a method here, and every method checks the same
/// three things in the same order: that the workflow allows the step, that the person is in a role allowed
/// to take it (and, for applicants, that the application is theirs), and that the rules specific to the step
/// are met. A step that passes is recorded in <see cref="History"/>.
/// </para>
/// <para>
/// Time is always passed in, never read from the clock, so the rules can be tested at any moment.
/// </para>
/// </remarks>
public sealed class PermitApplication
{
    private const int MinimumDescriptionLength = 10;
    private const int MaximumDescriptionLength = 2_000;
    private const int MinimumReasonLength = 10;
    private const int MaximumReasonLength = 1_000;
    private const int MaximumInspectionLeadDays = 120;

    private readonly List<StatusChange> _history = [];
    private readonly List<Inspection> _inspections = [];

    private PermitApplication()
    {
        ProjectDescription = string.Empty;
        SiteAddress = PostalAddress.Create("000", null, "None", "XX", "00000");
    }

    /// <summary>The identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The kind of permit applied for.</summary>
    public Guid PermitTypeId { get; private set; }

    /// <summary>The applicant who owns the application.</summary>
    public Guid ApplicantId { get; private set; }

    /// <summary>The public number, assigned the first time the application is submitted.</summary>
    public PermitNumber? Number { get; private set; }

    /// <summary>What the applicant intends to do.</summary>
    public string ProjectDescription { get; private set; }

    /// <summary>Where the work or event takes place.</summary>
    public PostalAddress SiteAddress { get; private set; }

    /// <summary>The applicant's stated value of the project.</summary>
    public Money DeclaredValuation { get; private set; }

    /// <summary>Where the application is in the process.</summary>
    public ApplicationStatus Status { get; private set; }

    /// <summary>The fee worked out at submission from the permit type's rules.</summary>
    public Money FeeAssessed { get; private set; }

    /// <summary>Whether the permit type needed an inspection, as it stood at submission.</summary>
    public bool InspectionRequired { get; private set; }

    /// <summary>When the draft was started.</summary>
    public DateTimeOffset CreatedOn { get; private set; }

    /// <summary>When the application was first sent to the office.</summary>
    public DateTimeOffset? SubmittedOn { get; private set; }

    /// <summary>When it was approved or denied.</summary>
    public DateTimeOffset? DecidedOn { get; private set; }

    /// <summary>The reason given for the decision, when one was given.</summary>
    public string? DecisionNote { get; private set; }

    /// <summary>When a clerk recorded the fee as paid.</summary>
    public DateTimeOffset? FeePaidOn { get; private set; }

    /// <summary>When the permit was issued.</summary>
    public DateTimeOffset? IssuedOn { get; private set; }

    /// <summary>The last day the issued permit is valid.</summary>
    public DateOnly? ExpiresOn { get; private set; }

    /// <summary>Every status change, oldest first.</summary>
    public IReadOnlyList<StatusChange> History => _history;

    /// <summary>
    /// The last moment anything was done to the application: the latest of its creation, its history, its dates and the
    /// closing of its inspections. A booked inspection's future date is not a change. This is where the retention
    /// period is counted from.
    /// </summary>
    public DateTimeOffset LastChangedOn()
    {
        DateTimeOffset latest = CreatedOn;

        foreach (DateTimeOffset? moment in new[] { SubmittedOn, DecidedOn, FeePaidOn, IssuedOn }
                     .Concat(_history.Select(change => (DateTimeOffset?)change.At))
                     .Concat(_inspections.Select(inspection => inspection.ClosedOn)))
        {
            if (moment is { } value && value > latest)
            {
                latest = value;
            }
        }

        return latest;
    }

    /// <summary>Every inspection, in the order they were booked.</summary>
    public IReadOnlyList<Inspection> Inspections => _inspections;

    /// <summary>Starts a draft application for an active permit type.</summary>
    /// <exception cref="DomainRuleException">The permit type is retired, or a value fails validation.</exception>
    public static PermitApplication CreateDraft(
        Guid id,
        PermitType permitType,
        Guid applicantId,
        string? projectDescription,
        PostalAddress siteAddress,
        Money declaredValuation,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(permitType);
        ArgumentNullException.ThrowIfNull(siteAddress);

        DomainRuleException.Require(id != Guid.Empty, "application.id", "An application needs an identifier.");
        DomainRuleException.Require(applicantId != Guid.Empty, "application.applicant", "An application needs an applicant.");
        DomainRuleException.Require(permitType.IsActive, "application.permit_type_retired", "This kind of permit is no longer offered.");

        PermitApplication application = new()
        {
            Id = id,
            PermitTypeId = permitType.Id,
            ApplicantId = applicantId,
            Status = ApplicationStatus.Draft,
            CreatedOn = now,
        };

        application.SetDetails(projectDescription, siteAddress, declaredValuation);

        return application;
    }

    /// <summary>Changes what the application says. Allowed while it is a draft or corrections are requested.</summary>
    public void Revise(Actor actor, string? projectDescription, PostalAddress siteAddress, Money declaredValuation)
    {
        ArgumentNullException.ThrowIfNull(siteAddress);

        RequireOwner(actor);
        DomainRuleException.Require(
            Status is ApplicationStatus.Draft or ApplicationStatus.CorrectionsRequested,
            "application.not_editable",
            $"An application that is {Describe(Status)} can no longer be edited.");

        SetDetails(projectDescription, siteAddress, declaredValuation);
    }

    /// <summary>
    /// Sends the application to the office. The fee is worked out now. The number is assigned on the first
    /// submission and kept when corrections are made and the application is sent again.
    /// </summary>
    public void Submit(Actor actor, PermitType permitType, PermitNumber number, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(permitType);
        DomainRuleException.Require(permitType.Id == PermitTypeId, "application.wrong_permit_type", "That is not this application's permit type.");

        WorkflowTransition step = Authorize(ApplicationAction.Submit, actor);

        DomainRuleException.Require(
            permitType.ValuationRateBasisPoints == 0 || DeclaredValuation > Money.Zero,
            "application.valuation_required",
            "Enter the value of the project: the fee for this permit depends on it.");

        if (Number is { } existing)
        {
            DomainRuleException.Require(existing == number, "application.number_changed", "An application keeps the number it was first given.");
        }

        Number = number;
        FeeAssessed = permitType.CalculateFee(DeclaredValuation);
        InspectionRequired = permitType.RequiresInspection;
        SubmittedOn ??= now;

        Apply(step, actor, now, note: null);
    }

    /// <summary>A clerk takes the application into review.</summary>
    public void StartReview(Actor actor, DateTimeOffset now) =>
        Apply(Authorize(ApplicationAction.StartReview, actor), actor, now, note: null);

    /// <summary>A clerk asks the applicant to make changes. The reason is required and is shown to the applicant.</summary>
    public void RequestCorrections(Actor actor, string reason, DateTimeOffset now)
    {
        WorkflowTransition step = Authorize(ApplicationAction.RequestCorrections, actor);
        string text = RequireReason(reason, "application.reason_required", "Say what needs to change");

        Apply(step, actor, now, text);
    }

    /// <summary>A clerk approves the application. A note is optional.</summary>
    public void Approve(Actor actor, string? note, DateTimeOffset now)
    {
        WorkflowTransition step = Authorize(ApplicationAction.Approve, actor);

        DecidedOn = now;
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        Apply(step, actor, now, DecisionNote);
    }

    /// <summary>A clerk refuses the application. The reason is required and is shown to the applicant.</summary>
    public void Deny(Actor actor, string reason, DateTimeOffset now)
    {
        WorkflowTransition step = Authorize(ApplicationAction.Deny, actor);
        string text = RequireReason(reason, "application.reason_required", "Say why the application is refused");

        DecidedOn = now;
        DecisionNote = text;

        Apply(step, actor, now, text);
    }

    /// <summary>The applicant withdraws the application. A reason is optional.</summary>
    public void Withdraw(Actor actor, string? reason, DateTimeOffset now)
    {
        WorkflowTransition step = Authorize(ApplicationAction.Withdraw, actor);

        // Nobody should visit, or record a result for, work the applicant has called off.
        CloseOpenInspections("The application was withdrawn.", now);

        Apply(step, actor, now, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
    }

    /// <summary>A clerk records that the fee has been paid. There is no payment provider: money is taken at the counter.</summary>
    public void RecordFeePayment(Actor actor, DateTimeOffset now)
    {
        RequireStaff(actor);
        DomainRuleException.Require(
            Status == ApplicationStatus.Approved,
            "payment.not_approved",
            $"A fee can be recorded only once an application is approved; this one is {Describe(Status)}.");
        DomainRuleException.Require(FeePaidOn is null, "payment.already_recorded", "The fee has already been recorded as paid.");

        FeePaidOn = now;
    }

    /// <summary>
    /// A clerk issues the permit. The fee must be paid and, when the permit type needs one, the most recent
    /// inspection must have passed.
    /// </summary>
    public void Issue(Actor actor, PermitType permitType, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(permitType);
        DomainRuleException.Require(permitType.Id == PermitTypeId, "application.wrong_permit_type", "That is not this application's permit type.");

        WorkflowTransition step = Authorize(ApplicationAction.Issue, actor);

        DomainRuleException.Require(FeePaidOn is not null, "issue.fee_unpaid", "The permit cannot be issued until the fee is recorded as paid.");
        DomainRuleException.Require(
            !InspectionRequired || LatestInspectionPassed,
            "issue.inspection_not_passed",
            "The permit cannot be issued until an inspection has passed.");

        IssuedOn = now;
        ExpiresOn = DateOnly.FromDateTime(now.UtcDateTime).AddDays(permitType.ValidityDays);

        // An inspection booked after the one that passed has no purpose once the permit is issued.
        CloseOpenInspections("The permit was issued.", now);

        Apply(step, actor, now, note: null);
    }

    /// <summary>Books an inspection with an inspector. Only an approved application that needs one can be inspected.</summary>
    public Inspection ScheduleInspection(Actor actor, Guid inspectorId, DateTimeOffset scheduledFor, DateTimeOffset now)
    {
        RequireStaff(actor);
        DomainRuleException.Require(inspectorId != Guid.Empty, "inspection.inspector", "Choose an inspector.");
        DomainRuleException.Require(
            Status == ApplicationStatus.Approved,
            "inspection.not_approved",
            $"An inspection can be booked only for an approved application; this one is {Describe(Status)}.");
        DomainRuleException.Require(InspectionRequired, "inspection.not_required", "This kind of permit does not need an inspection.");
        DomainRuleException.Require(
            _inspections.TrueForAll(inspection => inspection.Status != InspectionStatus.Scheduled),
            "inspection.already_scheduled",
            "An inspection is already booked. Cancel it first to book another.");
        DomainRuleException.Require(scheduledFor > now, "inspection.in_the_past", "An inspection must be booked for a future time.");
        DomainRuleException.Require(
            scheduledFor <= now.AddDays(MaximumInspectionLeadDays),
            "inspection.too_far_ahead",
            $"An inspection cannot be booked more than {MaximumInspectionLeadDays} days ahead.");

        Inspection inspection = Inspection.Schedule(inspectorId, scheduledFor);
        _inspections.Add(inspection);

        return inspection;
    }

    /// <summary>The assigned inspector (or an administrator) records what the inspection found.</summary>
    public void RecordInspectionResult(Actor actor, Guid inspectionId, bool passed, string? notes, DateTimeOffset now)
    {
        Inspection inspection = FindScheduledInspection(inspectionId);

        DomainRuleException.Require(
            actor.Role == ActorRole.Administrator || (actor.Role == ActorRole.Inspector && actor.Id == inspection.InspectorId),
            "inspection.not_assigned",
            "Only the inspector assigned to this inspection can record its result.");

        string? text = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        if (!passed)
        {
            DomainRuleException.Require(
                text is { Length: >= MinimumReasonLength },
                "inspection.failure_needs_notes",
                "Say what failed, so the applicant knows what to put right.");
        }

        inspection.Close(passed ? InspectionStatus.Passed : InspectionStatus.Failed, text, now);
    }

    /// <summary>Calls off a booked inspection.</summary>
    public void CancelInspection(Actor actor, Guid inspectionId, string? reason, DateTimeOffset now)
    {
        RequireStaff(actor);

        Inspection inspection = FindScheduledInspection(inspectionId);

        inspection.Close(InspectionStatus.Cancelled, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), now);
    }

    private void CloseOpenInspections(string reason, DateTimeOffset now)
    {
        foreach (Inspection inspection in _inspections.Where(candidate => candidate.Status == InspectionStatus.Scheduled))
        {
            inspection.Close(InspectionStatus.Cancelled, reason, now);
        }
    }

    private bool LatestInspectionPassed =>
        _inspections
            .Where(inspection => inspection.Status is InspectionStatus.Passed or InspectionStatus.Failed)
            .OrderBy(inspection => inspection.ClosedOn)
            .LastOrDefault()?.Status == InspectionStatus.Passed;

    private Inspection FindScheduledInspection(Guid inspectionId)
    {
        Inspection? inspection = _inspections.Find(candidate => candidate.Id == inspectionId);

        DomainRuleException.Require(inspection is not null, "inspection.unknown", "That inspection does not belong to this application.");
        DomainRuleException.Require(inspection.Status == InspectionStatus.Scheduled, "inspection.closed", "That inspection has already been closed.");

        return inspection;
    }

    /// <summary>
    /// Finds every rule the entered details break, not just the first, so a form can list them all at once. The
    /// methods that create or change an application apply the same rules.
    /// </summary>
    public static IReadOnlyList<RuleViolation> CheckDetails(string? projectDescription, string? line1, string? line2, string? city, string? region, string? postalCode)
    {
        List<RuleViolation> found = [];

        if (!DescriptionIsValid(projectDescription?.Trim() ?? string.Empty))
        {
            found.Add(DescriptionViolation);
        }

        found.AddRange(PostalAddress.Check(line1, line2, city, region, postalCode));

        return found;
    }

    private static RuleViolation DescriptionViolation { get; } = new(
        "application.description",
        $"Describe the project in {MinimumDescriptionLength} to {MaximumDescriptionLength:N0} characters.");

    private static bool DescriptionIsValid(string description) =>
        description.Length is >= MinimumDescriptionLength and <= MaximumDescriptionLength;

    private void SetDetails(string? projectDescription, PostalAddress siteAddress, Money declaredValuation)
    {
        string description = projectDescription?.Trim() ?? string.Empty;

        DomainRuleException.Require(DescriptionIsValid(description), DescriptionViolation.Code, DescriptionViolation.Message);

        ProjectDescription = description;
        SiteAddress = siteAddress;
        DeclaredValuation = declaredValuation;
    }

    /// <summary>
    /// Checks, in this order: an applicant owns the application (so a stranger learns nothing about it, not even its
    /// status, from the answer); the workflow allows the step from the current status; the role may take it.
    /// </summary>
    private WorkflowTransition Authorize(ApplicationAction action, Actor actor)
    {
        if (actor.Role == ActorRole.Applicant)
        {
            RequireOwner(actor);
        }

        WorkflowTransition step = ApplicationWorkflow.Find(Status, action)
            ?? throw new DomainRuleException(
                "workflow.transition_not_allowed",
                $"An application that is {Describe(Status)} cannot be {Past(action)}.");

        DomainRuleException.Require(
            step.Roles.Contains(actor.Role),
            "workflow.role_not_allowed",
            $"A person acting as {actor.Role.ToString().ToUpperInvariant()} may not do this.");

        return step;
    }

    private void Apply(WorkflowTransition step, Actor actor, DateTimeOffset now, string? note)
    {
        _history.Add(StatusChange.Record(Status, step.To, step.Action, actor, now, note));
        Status = step.To;
    }

    private void RequireOwner(Actor actor) =>
        DomainRuleException.Require(
            actor.Role == ActorRole.Applicant && actor.Id == ApplicantId,
            "workflow.not_your_application",
            "Only the applicant who made this application can do this.");

    private static void RequireStaff(Actor actor) =>
        DomainRuleException.Require(actor.IsStaff, "workflow.role_not_allowed", "Only office staff can do this.");

    private static string RequireReason(string? reason, string code, string prompt)
    {
        string text = reason?.Trim() ?? string.Empty;

        DomainRuleException.Require(
            text.Length is >= MinimumReasonLength and <= MaximumReasonLength,
            code,
            $"{prompt} ({MinimumReasonLength} to {MaximumReasonLength:N0} characters).");

        return text;
    }

    private static string Describe(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Draft => "a draft",
        ApplicationStatus.Submitted => "submitted",
        ApplicationStatus.InReview => "in review",
        ApplicationStatus.CorrectionsRequested => "waiting for corrections",
        ApplicationStatus.Approved => "approved",
        ApplicationStatus.Denied => "denied",
        ApplicationStatus.Withdrawn => "withdrawn",
        ApplicationStatus.Issued => "issued",
        _ => status.ToString(),
    };

    private static string Past(ApplicationAction action) => action switch
    {
        ApplicationAction.Submit => "submitted",
        ApplicationAction.StartReview => "taken into review",
        ApplicationAction.RequestCorrections => "sent back for corrections",
        ApplicationAction.Approve => "approved",
        ApplicationAction.Deny => "denied",
        ApplicationAction.Issue => "issued",
        ApplicationAction.Withdraw => "withdrawn",
        _ => action.ToString(),
    };
}

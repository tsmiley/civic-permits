// These records are the wire format. The API writes every field and clients read them, so inside this solution some
// are never read, which is expected.
// ReSharper disable NotAccessedPositionalProperty.Global
namespace CivicPermits.Shared.Contracts;

/// <summary>A kind of permit the office offers.</summary>
/// <param name="Id">The identifier.</param>
/// <param name="Category">Building, Sign, Event or Vendor.</param>
/// <param name="Name">The name shown to applicants.</param>
/// <param name="Description">What the permit covers.</param>
/// <param name="BaseFeeCents">The fee charged whatever the project costs, in cents.</param>
/// <param name="ValuationRateBasisPoints">The share of the declared project value added to the fee, in hundredths of a percent.</param>
/// <param name="RequiresInspection">Whether an inspection must pass before the permit is issued.</param>
/// <param name="ValidityDays">How many days an issued permit stays valid.</param>
/// <param name="TargetReviewDays">How many days the office aims to take to review an application.</param>
/// <param name="IsActive">Whether new applications can be started for this type.</param>
public sealed record PermitTypeResponse(
    Guid Id,
    string Category,
    string Name,
    string Description,
    long BaseFeeCents,
    int ValuationRateBasisPoints,
    bool RequiresInspection,
    int ValidityDays,
    int TargetReviewDays,
    bool IsActive);

/// <summary>What an administrator sends to define or change a permit type.</summary>
/// <param name="Category">Building, Sign, Event or Vendor.</param>
/// <param name="Name">The name shown to applicants, 3 to 80 characters.</param>
/// <param name="Description">What the permit covers, 10 to 500 characters.</param>
/// <param name="BaseFeeCents">The fee charged whatever the project costs, in cents.</param>
/// <param name="ValuationRateBasisPoints">0 to 1,000 (0% to 10%).</param>
/// <param name="RequiresInspection">Whether an inspection must pass before issue.</param>
/// <param name="ValidityDays">30 to 3,650.</param>
/// <param name="TargetReviewDays">1 to 90.</param>
public sealed record PermitTypeRequest(
    string? Category,
    string? Name,
    string? Description,
    long BaseFeeCents,
    int ValuationRateBasisPoints,
    bool RequiresInspection,
    int ValidityDays,
    int TargetReviewDays);

/// <summary>A street address.</summary>
/// <param name="Line1">The street number and name.</param>
/// <param name="Line2">The unit or suite, when there is one.</param>
/// <param name="City">The city or town.</param>
/// <param name="Region">The two-letter state code.</param>
/// <param name="PostalCode">The five-digit postal code.</param>
public sealed record AddressDto(string? Line1, string? Line2, string? City, string? Region, string? PostalCode);

/// <summary>What an applicant sends to start an application.</summary>
/// <param name="PermitTypeId">The kind of permit.</param>
/// <param name="ProjectDescription">What they intend to do, 10 to 2,000 characters.</param>
/// <param name="SiteAddress">Where.</param>
/// <param name="DeclaredValuationCents">The value of the project, in cents.</param>
public sealed record CreateApplicationRequest(
    Guid PermitTypeId,
    string? ProjectDescription,
    AddressDto? SiteAddress,
    long DeclaredValuationCents);

/// <summary>What an applicant sends to change a draft or an application sent back for corrections.</summary>
/// <param name="ProjectDescription">What they intend to do, 10 to 2,000 characters.</param>
/// <param name="SiteAddress">Where.</param>
/// <param name="DeclaredValuationCents">The value of the project, in cents.</param>
public sealed record ReviseApplicationRequest(
    string? ProjectDescription,
    AddressDto? SiteAddress,
    long DeclaredValuationCents);

/// <summary>A reason or note sent with a step: required to deny or ask for corrections, optional otherwise.</summary>
/// <param name="Reason">The text, up to 1,000 characters.</param>
public sealed record ReasonRequest(string? Reason);

/// <summary>What staff send to book an inspection.</summary>
/// <param name="InspectorId">The inspector who will carry it out.</param>
/// <param name="ScheduledFor">When.</param>
public sealed record ScheduleInspectionRequest(Guid InspectorId, DateTimeOffset ScheduledFor);

/// <summary>What an inspector sends to record a result.</summary>
/// <param name="Passed">Whether the work passed.</param>
/// <param name="Notes">What was found; required when it failed.</param>
public sealed record InspectionResultRequest(bool Passed, string? Notes);

/// <summary>One entry in an application's history.</summary>
/// <param name="From">The status before.</param>
/// <param name="To">The status after.</param>
/// <param name="Action">What was done.</param>
/// <param name="ActorRole">The role of the person who did it.</param>
/// <param name="At">When.</param>
/// <param name="Note">The reason or note given, when there was one.</param>
public sealed record StatusChangeResponse(
    string From,
    string To,
    string Action,
    string ActorRole,
    DateTimeOffset At,
    string? Note);

/// <summary>An inspection.</summary>
/// <param name="Id">The identifier.</param>
/// <param name="InspectorId">The inspector.</param>
/// <param name="ScheduledFor">When it is booked for.</param>
/// <param name="Status">Scheduled, Passed, Failed or Cancelled.</param>
/// <param name="ClosedOn">When the result was recorded or it was cancelled.</param>
/// <param name="Notes">What was found, or why it was cancelled.</param>
public sealed record InspectionResponse(
    Guid Id,
    Guid InspectorId,
    DateTimeOffset ScheduledFor,
    string Status,
    DateTimeOffset? ClosedOn,
    string? Notes);

/// <summary>A whole application, as the signed-in person is allowed to see it.</summary>
/// <param name="Id">The identifier.</param>
/// <param name="Number">The public number, once submitted.</param>
/// <param name="PermitTypeId">The kind of permit.</param>
/// <param name="PermitTypeName">Its name.</param>
/// <param name="Status">Where it is in the process.</param>
/// <param name="ProjectDescription">What the applicant intends to do.</param>
/// <param name="SiteAddress">Where.</param>
/// <param name="DeclaredValuationCents">The stated value, in cents.</param>
/// <param name="FeeAssessedCents">The fee worked out at submission, in cents.</param>
/// <param name="InspectionRequired">Whether an inspection is part of getting the permit.</param>
/// <param name="CreatedOn">When the draft was started.</param>
/// <param name="SubmittedOn">When it was first sent.</param>
/// <param name="DecidedOn">When it was approved or denied.</param>
/// <param name="DecisionNote">The reason given for the decision.</param>
/// <param name="FeePaidOn">When the fee was recorded as paid.</param>
/// <param name="IssuedOn">When the permit was issued.</param>
/// <param name="ExpiresOn">The last day the permit is valid.</param>
/// <param name="AllowedActions">The workflow steps the signed-in person could take next.</param>
/// <param name="History">Every step taken, oldest first.</param>
/// <param name="Inspections">Every inspection, in the order booked.</param>
public sealed record ApplicationResponse(
    Guid Id,
    string? Number,
    Guid PermitTypeId,
    string PermitTypeName,
    string Status,
    string ProjectDescription,
    AddressDto SiteAddress,
    long DeclaredValuationCents,
    long FeeAssessedCents,
    bool InspectionRequired,
    DateTimeOffset CreatedOn,
    DateTimeOffset? SubmittedOn,
    DateTimeOffset? DecidedOn,
    string? DecisionNote,
    DateTimeOffset? FeePaidOn,
    DateTimeOffset? IssuedOn,
    DateOnly? ExpiresOn,
    IReadOnlyList<string> AllowedActions,
    IReadOnlyList<StatusChangeResponse> History,
    IReadOnlyList<InspectionResponse> Inspections);

/// <summary>One line in a list of applications.</summary>
/// <param name="Id">The identifier.</param>
/// <param name="Number">The public number, once submitted.</param>
/// <param name="PermitTypeName">The kind of permit.</param>
/// <param name="Status">Where it is in the process.</param>
/// <param name="ProjectDescription">What the applicant intends to do.</param>
/// <param name="CreatedOn">When the draft was started.</param>
/// <param name="SubmittedOn">When it was first sent.</param>
/// <param name="FeeAssessedCents">The fee, in cents.</param>
public sealed record ApplicationSummaryResponse(
    Guid Id,
    string? Number,
    string PermitTypeName,
    string Status,
    string ProjectDescription,
    DateTimeOffset CreatedOn,
    DateTimeOffset? SubmittedOn,
    long FeeAssessedCents);

/// <summary>One page of results.</summary>
/// <typeparam name="T">What the page holds.</typeparam>
/// <param name="Items">The items on this page.</param>
/// <param name="Page">The page number, from 1.</param>
/// <param name="PageSize">The most items a page holds.</param>
/// <param name="TotalCount">How many items match in all.</param>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>What anyone may learn about a permit from its number: nothing about who or where.</summary>
/// <param name="Number">The permit number.</param>
/// <param name="PermitTypeName">The kind of permit.</param>
/// <param name="Status">Where it is in the process.</param>
/// <param name="IssuedOn">When it was issued, if it was.</param>
/// <param name="ExpiresOn">The last day it is valid, if issued.</param>
public sealed record PublicPermitStatusResponse(
    string Number,
    string PermitTypeName,
    string Status,
    DateTimeOffset? IssuedOn,
    DateOnly? ExpiresOn);

/// <summary>A person who can be given an inspection.</summary>
/// <param name="Id">The person's identifier.</param>
/// <param name="DisplayName">The name shown in lists.</param>
public sealed record InspectorResponse(Guid Id, string DisplayName);

/// <summary>One line of the audit record, as an administrator reads it.</summary>
/// <param name="Id">The identifier.</param>
/// <param name="OccurredOn">When it happened, in UTC.</param>
/// <param name="ActorId">The account that did it.</param>
/// <param name="ActorName">That account's name, or a note that the account has been closed.</param>
/// <param name="Action">A stable code for what was done, such as <c>permit_type.created</c>.</param>
/// <param name="SubjectType">The kind of thing it was done to.</param>
/// <param name="SubjectId">Which one, if it was one.</param>
/// <param name="Summary">What changed, in plain words.</param>
public sealed record AuditEntryResponse(
    Guid Id,
    DateTimeOffset OccurredOn,
    Guid ActorId,
    string ActorName,
    string Action,
    string SubjectType,
    Guid? SubjectId,
    string Summary);

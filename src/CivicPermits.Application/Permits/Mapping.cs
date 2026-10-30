using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;
using CivicPermits.Domain.Permits;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Application.Permits;

/// <summary>Turns domain objects into the contracts the API returns, and back for addresses.</summary>
internal static class Mapping
{
    public static PermitTypeResponse ToResponse(this PermitType type) => new(
        type.Id,
        type.Category.ToString(),
        type.Name,
        type.Description,
        type.BaseFee.Cents,
        type.ValuationRateBasisPoints,
        type.RequiresInspection,
        type.ValidityDays,
        type.TargetReviewDays,
        type.IsActive);

    public static AddressDto ToDto(this PostalAddress address) =>
        new(address.Line1, address.Line2, address.City, address.Region, address.PostalCode);

    public static PostalAddress ToAddress(this AddressDto? dto) =>
        PostalAddress.Create(dto?.Line1, dto?.Line2, dto?.City, dto?.Region, dto?.PostalCode);

    public static ApplicationResponse ToResponse(this PermitApplication application, PermitType type, Actor viewer) => new(
        application.Id,
        application.Number?.ToString(),
        application.PermitTypeId,
        type.Name,
        application.Status.ToString(),
        application.ProjectDescription,
        application.SiteAddress.ToDto(),
        application.DeclaredValuation.Cents,
        application.FeeAssessed.Cents,
        application.InspectionRequired,
        application.CreatedOn,
        application.SubmittedOn,
        application.DecidedOn,
        application.DecisionNote,
        application.FeePaidOn,
        application.IssuedOn,
        application.ExpiresOn,
        AllowedActions(application, viewer),
        [.. application.History.OrderBy(change => change.At).Select(change => new StatusChangeResponse(
            change.From.ToString(),
            change.To.ToString(),
            change.Action.ToString(),
            change.ActorRole.ToString(),
            change.At,
            change.Note))],
        [.. application.Inspections.Select(inspection => new InspectionResponse(
            inspection.Id,
            inspection.InspectorId,
            inspection.ScheduledFor,
            inspection.Status.ToString(),
            inspection.ClosedOn,
            inspection.Notes))]);

    /// <summary>The workflow steps the viewer could take next. An applicant can act only on their own application.</summary>
    private static string[] AllowedActions(PermitApplication application, Actor viewer)
    {
        if (viewer.Role == ActorRole.Applicant && viewer.Id != application.ApplicantId)
        {
            return [];
        }

        return [.. ApplicationWorkflow.ActionsFor(application.Status, viewer.Role).Select(action => action.ToString())];
    }
}

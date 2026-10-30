using CivicPermits.Application.Abstractions;
using CivicPermits.Application.Messaging;
using CivicPermits.Domain.Applications;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Application.Permits;

/// <summary>Staff book an inspection for an approved application.</summary>
/// <param name="ApplicationId">The application.</param>
/// <param name="Request">The inspector and the time.</param>
public sealed record ScheduleInspectionCommand(Guid ApplicationId, ScheduleInspectionRequest Request) : IRequest<ApplicationResponse>;

/// <summary>The assigned inspector, or an administrator, records what an inspection found.</summary>
/// <param name="ApplicationId">The application.</param>
/// <param name="InspectionId">The inspection.</param>
/// <param name="Request">The result.</param>
public sealed record RecordInspectionResultCommand(Guid ApplicationId, Guid InspectionId, InspectionResultRequest Request)
    : IRequest<ApplicationResponse>;

/// <summary>Staff call off a booked inspection.</summary>
/// <param name="ApplicationId">The application.</param>
/// <param name="InspectionId">The inspection.</param>
/// <param name="Reason">Why, when there is a reason to give.</param>
public sealed record CancelInspectionCommand(Guid ApplicationId, Guid InspectionId, string? Reason) : IRequest<ApplicationResponse>;

/// <summary>Staff ask who the inspectors are, to choose one for an inspection.</summary>
public sealed record ListInspectorsQuery : IRequest<IReadOnlyList<InspectorResponse>>;

internal sealed class InspectionHandlers(
    IPermitApplicationStore applications,
    IPermitTypeStore types,
    IUserDirectory users,
    ICurrentUser user,
    IUnitOfWork work,
    TimeProvider time) :
    IRequestHandler<ScheduleInspectionCommand, ApplicationResponse>,
    IRequestHandler<RecordInspectionResultCommand, ApplicationResponse>,
    IRequestHandler<CancelInspectionCommand, ApplicationResponse>,
    IRequestHandler<ListInspectorsQuery, IReadOnlyList<InspectorResponse>>
{
    public async ValueTask<IReadOnlyList<InspectorResponse>> HandleAsync(ListInspectorsQuery request, CancellationToken cancellationToken)
    {
        user.Require(ActorRole.Clerk, ActorRole.Administrator);

        return await users.ListInspectorsAsync(cancellationToken);
    }

    public async ValueTask<ApplicationResponse> HandleAsync(ScheduleInspectionCommand request, CancellationToken cancellationToken)
    {
        Actor actor = user.Require(ActorRole.Clerk, ActorRole.Administrator);
        PermitApplication application = await applications.LoadVisibleAsync(request.ApplicationId, actor, cancellationToken);

        if (!await users.IsInspectorAsync(request.Request.InspectorId, cancellationToken))
        {
            throw Access.Invalid("inspection.inspector", "Choose a person who works as an inspector.");
        }

        application.ScheduleInspection(actor, request.Request.InspectorId, request.Request.ScheduledFor, time.GetUtcNow());
        await work.SaveChangesAsync(cancellationToken);

        return await RespondAsync(application, actor, cancellationToken);
    }

    public async ValueTask<ApplicationResponse> HandleAsync(RecordInspectionResultCommand request, CancellationToken cancellationToken)
    {
        Actor actor = user.Require(ActorRole.Inspector, ActorRole.Administrator);
        PermitApplication application = await applications.LoadVisibleAsync(request.ApplicationId, actor, cancellationToken);

        application.RecordInspectionResult(actor, request.InspectionId, request.Request.Passed, request.Request.Notes, time.GetUtcNow());
        await work.SaveChangesAsync(cancellationToken);

        return await RespondAsync(application, actor, cancellationToken);
    }

    public async ValueTask<ApplicationResponse> HandleAsync(CancelInspectionCommand request, CancellationToken cancellationToken)
    {
        Actor actor = user.Require(ActorRole.Clerk, ActorRole.Administrator);
        PermitApplication application = await applications.LoadVisibleAsync(request.ApplicationId, actor, cancellationToken);

        application.CancelInspection(actor, request.InspectionId, request.Reason, time.GetUtcNow());
        await work.SaveChangesAsync(cancellationToken);

        return await RespondAsync(application, actor, cancellationToken);
    }

    private async Task<ApplicationResponse> RespondAsync(PermitApplication application, Actor actor, CancellationToken cancellationToken) =>
        application.ToResponse(
            await types.FindAsync(application.PermitTypeId, cancellationToken)
                ?? throw new InvalidOperationException("An application refers to a permit type that does not exist."),
            actor);
}

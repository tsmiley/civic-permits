using CivicPermits.Application.Abstractions;
using CivicPermits.Application.Messaging;
using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;
using CivicPermits.Domain.Permits;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Application.Permits;

/// <summary>Lists the kinds of permit. Anyone may ask; only staff are shown the types that are closed to new applications.</summary>
/// <param name="IncludeInactive">Whether to include closed types (ignored for anyone who is not staff).</param>
public sealed record ListPermitTypesQuery(bool IncludeInactive) : IRequest<IReadOnlyList<PermitTypeResponse>>;

/// <summary>Reads one kind of permit. A closed type is not found unless the asker is staff.</summary>
/// <param name="Id">The identifier.</param>
public sealed record GetPermitTypeQuery(Guid Id) : IRequest<PermitTypeResponse>;

/// <summary>Defines a new kind of permit, or changes an existing one. Administrators only.</summary>
/// <param name="Id">The type to change, or null to define a new one.</param>
/// <param name="Request">The values.</param>
public sealed record SavePermitTypeCommand(Guid? Id, PermitTypeRequest Request) : IRequest<PermitTypeResponse>;

/// <summary>Opens or closes a kind of permit to new applications. Administrators only.</summary>
/// <param name="Id">The type.</param>
/// <param name="Active">True to open it, false to close it.</param>
public sealed record SetPermitTypeActiveCommand(Guid Id, bool Active) : IRequest<PermitTypeResponse>;

internal sealed class PermitTypeHandlers(IPermitTypeStore types, IAuditLog audit, ICurrentUser user, IUnitOfWork work, TimeProvider time) :
    IRequestHandler<ListPermitTypesQuery, IReadOnlyList<PermitTypeResponse>>,
    IRequestHandler<GetPermitTypeQuery, PermitTypeResponse>,
    IRequestHandler<SavePermitTypeCommand, PermitTypeResponse>,
    IRequestHandler<SetPermitTypeActiveCommand, PermitTypeResponse>
{
    private const string NotFoundCode = "permit_type.not_found";
    private const string NotFoundMessage = "That kind of permit was not found.";

    public async ValueTask<IReadOnlyList<PermitTypeResponse>> HandleAsync(ListPermitTypesQuery request, CancellationToken cancellationToken)
    {
        bool includeInactive = request.IncludeInactive && user.Actor is { IsStaff: true };

        return [.. (await types.ListAsync(includeInactive, cancellationToken)).Select(type => type.ToResponse())];
    }

    public async ValueTask<PermitTypeResponse> HandleAsync(GetPermitTypeQuery request, CancellationToken cancellationToken)
    {
        PermitType? type = await types.FindAsync(request.Id, cancellationToken);

        return type is not null && (type.IsActive || user.Actor is { IsStaff: true })
            ? type.ToResponse()
            : throw RequestException.NotFound(NotFoundCode, NotFoundMessage);
    }

    public async ValueTask<PermitTypeResponse> HandleAsync(SavePermitTypeCommand request, CancellationToken cancellationToken)
    {
        Actor administrator = user.Require(ActorRole.Administrator);
        PermitTypeRequest values = request.Request;

        PermitCategory category = Enum.TryParse(values.Category, ignoreCase: true, out PermitCategory parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw Access.Invalid("permit_type.category", "Choose a permit category: Building, Sign, Event or Vendor.");

        PermitType type;
        AuditSummaries.Values? before = null;

        if (request.Id is { } id)
        {
            type = await types.FindAsync(id, cancellationToken)
                ?? throw RequestException.NotFound(NotFoundCode, NotFoundMessage);
            before = AuditSummaries.Values.Of(type);
            type.Revise(category, values.Name, values.Description, new Money(values.BaseFeeCents), values.ValuationRateBasisPoints, values.RequiresInspection, values.ValidityDays, values.TargetReviewDays);
        }
        else
        {
            type = PermitType.Create(Guid.CreateVersion7(), category, values.Name, values.Description, new Money(values.BaseFeeCents), values.ValuationRateBasisPoints, values.RequiresInspection, values.ValidityDays, values.TargetReviewDays);
        }

        if (await types.NameInUseAsync(type.Name, request.Id, cancellationToken))
        {
            throw RequestException.Conflict("permit_type.name_in_use", "Another kind of permit already has that name.");
        }

        if (request.Id is null)
        {
            types.Add(type);
        }

        // Written with the change and in the same save, so the record and the change stand or fall together, and a
        // request that was refused above leaves no entry.
        audit.Add(AuditEntry.Record(
            Guid.CreateVersion7(),
            time.GetUtcNow(),
            administrator.Id,
            before is null ? "permit_type.created" : "permit_type.revised",
            nameof(PermitType),
            type.Id,
            before is { } old ? AuditSummaries.Revised(old, type) : AuditSummaries.Created(type)));

        await work.SaveChangesAsync(cancellationToken);

        return type.ToResponse();
    }

    public async ValueTask<PermitTypeResponse> HandleAsync(SetPermitTypeActiveCommand request, CancellationToken cancellationToken)
    {
        Actor administrator = user.Require(ActorRole.Administrator);

        PermitType type = await types.FindAsync(request.Id, cancellationToken)
            ?? throw RequestException.NotFound(NotFoundCode, NotFoundMessage);
        bool wasOpen = type.IsActive;

        if (request.Active)
        {
            type.Reactivate();
        }
        else
        {
            type.Deactivate();
        }

        audit.Add(AuditEntry.Record(
            Guid.CreateVersion7(),
            time.GetUtcNow(),
            administrator.Id,
            request.Active ? "permit_type.opened" : "permit_type.closed",
            nameof(PermitType),
            type.Id,
            request.Active ? AuditSummaries.Opened(type, wasOpen) : AuditSummaries.Closed(type, wasOpen)));

        await work.SaveChangesAsync(cancellationToken);

        return type.ToResponse();
    }
}

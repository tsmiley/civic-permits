using CivicPermits.Application.Abstractions;
using CivicPermits.Application.Messaging;
using CivicPermits.Domain.Applications;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Application.Permits;

/// <summary>Reads the audit record, newest first. Administrators only, and read only: nothing here changes an entry.</summary>
/// <param name="Page">The page, from 1.</param>
/// <param name="PageSize">The most entries on a page, 1 to 100.</param>
public sealed record ListAuditQuery(int Page, int PageSize) : IRequest<PagedResponse<AuditEntryResponse>>;

internal sealed class AuditHandlers(IAuditLog audit, ICurrentUser user) :
    IRequestHandler<ListAuditQuery, PagedResponse<AuditEntryResponse>>
{
    private const int MaximumPageSize = 100;

    public async ValueTask<PagedResponse<AuditEntryResponse>> HandleAsync(ListAuditQuery request, CancellationToken cancellationToken)
    {
        user.Require(ActorRole.Administrator);

        if (request.Page < 1)
        {
            throw Access.Invalid("paging.page", "The page must be 1 or more.");
        }

        if (request.PageSize is < 1 or > MaximumPageSize)
        {
            throw Access.Invalid("paging.page_size", $"The page size must be 1 to {MaximumPageSize}.");
        }

        return await audit.ListAsync(request.Page, request.PageSize, cancellationToken);
    }
}

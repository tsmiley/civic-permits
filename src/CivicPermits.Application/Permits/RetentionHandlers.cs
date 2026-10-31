using CivicPermits.Application.Abstractions;
using CivicPermits.Application.Messaging;
using CivicPermits.Domain.Applications;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Application.Permits;

/// <summary>Removes what has outlived its retention period, or counts it. Administrators only.</summary>
/// <param name="DryRun">True to remove nothing and only report what a real run would remove.</param>
public sealed record PurgeExpiredRecordsCommand(bool DryRun) : IRequest<RetentionResponse>;

internal sealed class RetentionHandlers(IRetentionService retention, ICurrentUser user, TimeProvider time) :
    IRequestHandler<PurgeExpiredRecordsCommand, RetentionResponse>
{
    public async ValueTask<RetentionResponse> HandleAsync(PurgeExpiredRecordsCommand request, CancellationToken cancellationToken)
    {
        Actor administrator = user.Require(ActorRole.Administrator);

        return await retention.RunAsync(administrator.Id, time.GetUtcNow(), request.DryRun, cancellationToken);
    }
}

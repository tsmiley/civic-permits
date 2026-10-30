namespace CivicPermits.Application.Messaging;

/// <summary>Carries out one kind of request. There is exactly one handler per request type.</summary>
/// <typeparam name="TRequest">The request this handler answers.</typeparam>
/// <typeparam name="TResponse">What it returns.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Handles the request.</summary>
    ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

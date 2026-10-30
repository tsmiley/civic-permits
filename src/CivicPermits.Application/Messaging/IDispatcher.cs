namespace CivicPermits.Application.Messaging;

/// <summary>
/// The only way the outer layers reach a use case. Endpoints and pages know a request type and a
/// dispatcher, never a handler, so a use case can change without its callers noticing.
/// </summary>
public interface IDispatcher
{
    /// <summary>Finds the handler for the request and runs it.</summary>
    /// <exception cref="InvalidOperationException">No handler is registered for the request type.</exception>
    ValueTask<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

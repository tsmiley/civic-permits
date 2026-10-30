using System.Collections.Concurrent;

namespace CivicPermits.Application.Messaging;

/// <summary>
/// Resolves a handler from the container and invokes it. The generic plumbing is built once per
/// request type and cached, so the reflection cost is paid on first use only.
/// </summary>
/// <remarks>
/// This replaces a third-party mediator library on purpose (see docs/DECISIONS): the whole mechanism
/// is about forty lines, it has no licence or upgrade risk, and a reviewer can read all of it.
/// </remarks>
internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, HandlerInvoker> Invokers = new();

    public ValueTask<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Type requestType = request.GetType();
        HandlerInvoker invoker = Invokers.GetOrAdd(requestType, static type => HandlerInvoker.For(type, typeof(TResponse)));

        object handler = services.GetService(invoker.HandlerType)
            ?? throw new InvalidOperationException(
                $"No handler is registered for {requestType.Name}. Add an IRequestHandler<{requestType.Name}, {typeof(TResponse).Name}>.");

        return (ValueTask<TResponse>)invoker.Invoke(handler, request, cancellationToken);
    }

    private sealed record HandlerInvoker(Type HandlerType, System.Reflection.MethodInfo Method)
    {
        public static HandlerInvoker For(Type requestType, Type responseType)
        {
            Type handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, responseType);

            return new HandlerInvoker(handlerType, handlerType.GetMethod(nameof(IRequestHandler<IRequest<object>, object>.HandleAsync))!);
        }

        public object Invoke(object handler, object request, CancellationToken cancellationToken) =>
            Method.Invoke(handler, [request, cancellationToken])!;
    }
}

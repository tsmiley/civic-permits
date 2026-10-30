using CivicPermits.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace CivicPermits.Application;

/// <summary>Registers the application layer.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds the dispatcher and every request handler in this assembly. A new handler needs no
    /// registration: implementing <see cref="IRequestHandler{TRequest, TResponse}"/> is enough.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IDispatcher, Dispatcher>();

        foreach (Type implementation in typeof(DependencyInjection).Assembly.GetTypes())
        {
            if (implementation is not { IsClass: true, IsAbstract: false })
            {
                continue;
            }

            foreach (Type contract in implementation.GetInterfaces())
            {
                if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                {
                    services.AddScoped(contract, implementation);
                }
            }
        }

        return services;
    }
}

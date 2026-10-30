using CivicPermits.Application.About;
using CivicPermits.Application.Messaging;
using CivicPermits.Shared.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace CivicPermits.Application.Tests;

public class DispatcherTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static ServiceProvider BuildProvider(TimeProvider? time = null)
    {
        ServiceCollection services = new();
        services.AddApplication();

        if (time is not null)
        {
            services.AddSingleton(time); // registered last, so it replaces the system clock
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task A_request_reaches_its_handler_and_the_answer_comes_back()
    {
        using ServiceProvider provider = BuildProvider(new FakeTimeProvider(Noon));
        using IServiceScope scope = provider.CreateScope();
        IDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        ApplicationInfoResponse info = await dispatcher.SendAsync(new GetApplicationInfoQuery(), TestContext.Current.CancellationToken);

        Assert.Equal("Civic Permits", info.Name);
        Assert.Equal(Noon, info.ServerTime);
        Assert.False(string.IsNullOrWhiteSpace(info.Version));
        Assert.DoesNotContain('+', info.Version);
    }

    [Fact]
    public async Task A_request_with_no_handler_fails_loudly_and_names_the_request()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        IDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await dispatcher.SendAsync(new UnhandledQuery(), TestContext.Current.CancellationToken));

        Assert.Contains(nameof(UnhandledQuery), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_null_request_is_refused()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        IDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await dispatcher.SendAsync<string>(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_handler_that_throws_before_it_returns_surfaces_its_own_exception()
    {
        ServiceCollection services = new();
        services.AddApplication();
        services.AddSingleton<IRequestHandler<ThrowsAtOnce, string>, ThrowsAtOnceHandler>();
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        using IServiceScope scope = provider.CreateScope();
        IDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        // Not an async method, so the exception is raised by the call itself, which reflection would wrap if asked to.
        await Assert.ThrowsAsync<ArithmeticException>(
            async () => await dispatcher.SendAsync(new ThrowsAtOnce(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_request_that_is_two_kinds_of_request_goes_to_the_handler_of_the_kind_it_was_sent_as()
    {
        ServiceCollection services = new();
        services.AddApplication();
        services.AddSingleton<IRequestHandler<Both, string>, BothAsText>();
        services.AddSingleton<IRequestHandler<Both, int>, BothAsNumber>();
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        using IServiceScope scope = provider.CreateScope();
        IDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        Both request = new();

        // The same object, the same runtime type, two answers: the cache must not hand the second caller the first one's handler.
        string text = await dispatcher.SendAsync<string>(request, TestContext.Current.CancellationToken);
        int number = await dispatcher.SendAsync<int>(request, TestContext.Current.CancellationToken);

        Assert.Equal("text", text);
        Assert.Equal(42, number);
    }

    private sealed record UnhandledQuery : IRequest<string>;

    private sealed record ThrowsAtOnce : IRequest<string>;

    private sealed class ThrowsAtOnceHandler : IRequestHandler<ThrowsAtOnce, string>
    {
        public ValueTask<string> HandleAsync(ThrowsAtOnce request, CancellationToken cancellationToken) =>
            throw new ArithmeticException("raised before any await");
    }

    private sealed record Both : IRequest<string>, IRequest<int>;

    private sealed class BothAsText : IRequestHandler<Both, string>
    {
        public ValueTask<string> HandleAsync(Both request, CancellationToken cancellationToken) => ValueTask.FromResult("text");
    }

    private sealed class BothAsNumber : IRequestHandler<Both, int>
    {
        public ValueTask<int> HandleAsync(Both request, CancellationToken cancellationToken) => ValueTask.FromResult(42);
    }
}

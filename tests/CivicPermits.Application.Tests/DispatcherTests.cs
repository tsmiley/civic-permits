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

    private sealed record UnhandledQuery : IRequest<string>;
}

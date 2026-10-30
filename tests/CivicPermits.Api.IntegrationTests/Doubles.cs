using System.Collections.Concurrent;
using CivicPermits.Application.Messaging;
using CivicPermits.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Serilog.Core;
using Serilog.Events;

namespace CivicPermits.Api.IntegrationTests;

/// <summary>A dispatcher that fails with a chosen exception, to prove how each kind of failure is answered.</summary>
internal sealed class FailingDispatcher(Exception failure) : IDispatcher
{
    public ValueTask<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
        throw failure;
}

/// <summary>Keeps every log event, so a test can read what the host wrote.</summary>
internal sealed class CapturingSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyList<LogEvent> Events => [.. _events];

    /// <summary>The lines the refusal log wrote.</summary>
    public IReadOnlyList<LogEvent> Refusals =>
        [.. _events.Where(entry => entry.RenderMessage(System.Globalization.CultureInfo.InvariantCulture).StartsWith("Refused:", StringComparison.Ordinal))];

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    /// <summary>A property of an event as plain text, or null.</summary>
    public static string? Property(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out LogEventPropertyValue? value) && value is ScalarValue { Value: { } scalar }
            ? Convert.ToString(scalar, System.Globalization.CultureInfo.InvariantCulture)
            : null;
}

/// <summary>A password hasher that counts how many passwords it was asked to check.</summary>
internal sealed class CountingHasher : PasswordHasher<ApplicationUser>
{
    private int _checks;

    public int Checks => _checks;

    public override PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
    {
        Interlocked.Increment(ref _checks);

        return base.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }
}

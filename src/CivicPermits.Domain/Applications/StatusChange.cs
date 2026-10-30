namespace CivicPermits.Domain.Applications;

/// <summary>One entry in an application's history: who moved it from one status to another, when, and why.</summary>
public sealed class StatusChange
{
    private StatusChange()
    {
    }

    /// <summary>The identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The status before.</summary>
    public ApplicationStatus From { get; private set; }

    /// <summary>The status after.</summary>
    public ApplicationStatus To { get; private set; }

    /// <summary>What was done.</summary>
    public ApplicationAction Action { get; private set; }

    /// <summary>The person who did it.</summary>
    public Guid ActorId { get; private set; }

    /// <summary>The role they acted in.</summary>
    public ActorRole ActorRole { get; private set; }

    /// <summary>When it was done.</summary>
    public DateTimeOffset At { get; private set; }

    /// <summary>The reason or note given, when one was needed or offered.</summary>
    public string? Note { get; private set; }

    internal static StatusChange Record(
        ApplicationStatus from,
        ApplicationStatus to,
        ApplicationAction action,
        Actor actor,
        DateTimeOffset at,
        string? note) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            From = from,
            To = to,
            Action = action,
            ActorId = actor.Id,
            ActorRole = actor.Role,
            At = at,
            Note = note,
        };
}

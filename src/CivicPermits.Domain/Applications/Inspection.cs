namespace CivicPermits.Domain.Applications;

/// <summary>Where an inspection stands.</summary>
public enum InspectionStatus
{
    /// <summary>Booked and not yet carried out.</summary>
    Scheduled = 1,

    /// <summary>Carried out and passed.</summary>
    Passed = 2,

    /// <summary>Carried out and failed; the notes say why.</summary>
    Failed = 3,

    /// <summary>Called off before it happened.</summary>
    Cancelled = 4,
}

/// <summary>A visit by an inspector to check the work an application covers.</summary>
public sealed class Inspection
{
    private Inspection()
    {
    }

    /// <summary>The identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The inspector who will carry it out.</summary>
    public Guid InspectorId { get; private set; }

    /// <summary>When it is booked for.</summary>
    public DateTimeOffset ScheduledFor { get; private set; }

    /// <summary>Where it stands.</summary>
    public InspectionStatus Status { get; private set; }

    /// <summary>When the result was recorded, or the inspection was cancelled.</summary>
    public DateTimeOffset? ClosedOn { get; private set; }

    /// <summary>What the inspector found, or why it was cancelled.</summary>
    public string? Notes { get; private set; }

    internal static Inspection Schedule(Guid inspectorId, DateTimeOffset scheduledFor) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            InspectorId = inspectorId,
            ScheduledFor = scheduledFor,
            Status = InspectionStatus.Scheduled,
        };

    internal void Close(InspectionStatus status, string? notes, DateTimeOffset at)
    {
        Status = status;
        Notes = notes;
        ClosedOn = at;
    }
}

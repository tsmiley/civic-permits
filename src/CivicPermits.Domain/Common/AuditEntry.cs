namespace CivicPermits.Domain.Common;

/// <summary>
/// One line of the audit record: who did a privileged thing, what they did, to what, and when. It is only ever added.
/// Nothing in the program changes or removes an entry, and the people who may read the record cannot edit it.
/// </summary>
/// <remarks>
/// An application's own history already records who took each step. This record is for the actions that have no
/// application to carry them: changing the office's fee schedule, running the retention purge, closing an account.
/// A summary says what changed in plain words and never holds a password, token or contact detail.
/// </remarks>
public sealed class AuditEntry
{
    /// <summary>The longest summary kept; a longer one is cut and marked.</summary>
    public const int MaximumSummaryLength = 2_000;

    /// <summary>The longest action or subject type.</summary>
    public const int MaximumCodeLength = 60;

    private AuditEntry()
    {
        Action = string.Empty;
        SubjectType = string.Empty;
        Summary = string.Empty;
    }

    /// <summary>The identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>When it happened, in UTC.</summary>
    public DateTimeOffset OccurredOn { get; private set; }

    /// <summary>Who did it (an account identifier, never an email address).</summary>
    public Guid ActorId { get; private set; }

    /// <summary>A stable code for what was done, such as <c>permit_type.created</c>.</summary>
    public string Action { get; private set; }

    /// <summary>The kind of thing it was done to, such as <c>PermitType</c>.</summary>
    public string SubjectType { get; private set; }

    /// <summary>Which one, or null when the action covers many or none.</summary>
    public Guid? SubjectId { get; private set; }

    /// <summary>What changed, in plain words: the fields and their old and new values, or the counts.</summary>
    public string Summary { get; private set; }

    /// <summary>Makes an entry.</summary>
    /// <exception cref="ArgumentException">The action or subject type is empty or too long.</exception>
    public static AuditEntry Record(Guid id, DateTimeOffset occurredOn, Guid actorId, string action, string subjectType, Guid? subjectId, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        ArgumentNullException.ThrowIfNull(summary);

        if (action.Length > MaximumCodeLength || subjectType.Length > MaximumCodeLength)
        {
            throw new ArgumentException($"An action and a subject type are at most {MaximumCodeLength} characters.");
        }

        string kept = summary.Length <= MaximumSummaryLength ? summary : string.Concat(summary.AsSpan(0, MaximumSummaryLength - 3), "...");

        return new AuditEntry
        {
            Id = id,
            OccurredOn = occurredOn.ToUniversalTime(),
            ActorId = actorId,
            Action = action,
            SubjectType = subjectType,
            SubjectId = subjectId,
            Summary = kept,
        };
    }
}

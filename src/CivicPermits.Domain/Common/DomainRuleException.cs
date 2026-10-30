using System.Diagnostics.CodeAnalysis;

namespace CivicPermits.Domain.Common;

/// <summary>
/// Thrown when an action would break a rule of the permit office: a transition the workflow does not allow,
/// a missing reason, a value out of range. The message is written to be shown to the person who caused it.
/// </summary>
/// <remarks>
/// The <see cref="Code"/> is stable and machine-readable so the API can map a rule to a problem document
/// without parsing text, and so a test can assert which rule fired.
/// </remarks>
public sealed class DomainRuleException : Exception
{
    private const string DefaultCode = "domain.rule_violated";

    /// <summary>Creates the exception.</summary>
    /// <param name="code">A stable identifier for the rule, such as <c>workflow.transition_not_allowed</c>.</param>
    /// <param name="message">What went wrong, in words the user can act on.</param>
    public DomainRuleException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>Creates the exception with no details.</summary>
    public DomainRuleException()
        : this(DefaultCode, "A rule was broken.")
    {
    }

    /// <summary>Creates the exception with a message only.</summary>
    public DomainRuleException(string message)
        : this(DefaultCode, message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    public DomainRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = DefaultCode;
    }

    /// <summary>The stable, machine-readable identifier of the rule that was broken.</summary>
    public string Code { get; }

    /// <summary>Throws a rule violation when the condition is false.</summary>
    internal static void Require([DoesNotReturnIf(false)] bool condition, string code, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(code, message);
        }
    }
}

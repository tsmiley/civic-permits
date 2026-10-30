namespace CivicPermits.Domain.Common;

/// <summary>One broken rule, found while checking a set of values. The code is the same stable one a thrown exception carries.</summary>
/// <param name="Code">The rule, such as <c>address.postal_code</c>.</param>
/// <param name="Message">What is wrong, in words the person can act on.</param>
public sealed record RuleViolation(string Code, string Message);

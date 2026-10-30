using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CivicPermits.Domain.Permits;

/// <summary>
/// The public identifier of a permit application, written on letters and read out over the phone.
/// </summary>
/// <remarks>
/// The format is <c>CP-YYYY-NNNNNN</c>: a fixed prefix, the year the application was filed, and a
/// six-digit sequence that restarts every year. It is deliberately not a database key. Keys are for the
/// database; this is for people, so it is short, has no ambiguous characters, and can be checked for
/// shape before any query runs. Because the public status lookup accepts it from anonymous callers,
/// parsing is strict: anything that does not match exactly is rejected rather than "fixed".
/// </remarks>
public readonly partial record struct PermitNumber
{
    private const string Prefix = "CP";

    private PermitNumber(int year, int sequence)
    {
        Year = year;
        Sequence = sequence;
    }

    /// <summary>The first year the office could have issued a number; earlier years are never valid.</summary>
    public static int EarliestYear => 2000;

    /// <summary>The largest sequence a year can hold.</summary>
    public static int MaximumSequence => 999_999;

    /// <summary>The year the application was filed.</summary>
    public int Year { get; }

    /// <summary>The position within the year, starting at one.</summary>
    public int Sequence { get; }

    /// <summary>Builds a number from its parts, or throws when a part is out of range.</summary>
    public static PermitNumber From(int year, int sequence)
    {
        if (!TryCreate(year, sequence, out PermitNumber number))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence),
                $"A permit number needs a year from {EarliestYear} and a sequence from 1 to {MaximumSequence}.");
        }

        return number;
    }

    /// <summary>
    /// Reads the text form. Returns false, and never throws, for any input that is not exactly
    /// <c>CP-YYYY-NNNNNN</c>: wrong case, spaces, extra characters and out-of-range values are all rejected.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out PermitNumber number)
    {
        number = default;

        if (text is null)
        {
            return false;
        }

        Match match = Shape().Match(text);

        return match.Success
            && TryCreate(
                int.Parse(match.Groups["year"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["seq"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture),
                out number);
    }

    /// <summary>The text form, for example <c>CP-2026-000042</c>.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}-{Year:D4}-{Sequence:D6}");

    private static bool TryCreate(int year, int sequence, out PermitNumber number)
    {
        bool valid = year >= EarliestYear && year <= 9999 && sequence >= 1 && sequence <= MaximumSequence;

        number = valid ? new PermitNumber(year, sequence) : default;

        return valid;
    }

    // \d would also match digits from other scripts, so the explicit class keeps the format ASCII-only.
    // \z, not $: in .NET, $ also matches just before a trailing newline, which would accept a trailing line feed.
    [GeneratedRegex(@"^CP-(?<year>[0-9]{4})-(?<seq>[0-9]{6})\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex Shape();
}

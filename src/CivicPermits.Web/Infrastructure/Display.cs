using System.Globalization;
using CivicPermits.Domain.Common;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Web.Infrastructure;

/// <summary>How amounts, dates and statuses are written for people. English, US conventions, UTC times.</summary>
internal static class Display
{
    public static string Dollars(long cents) => new Money(cents).ToString();

    public static string Date(DateTimeOffset? moment) =>
        moment is { } value ? value.UtcDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : "Not yet";

    public static string Date(DateOnly? day) =>
        day is { } value ? value.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : "Not yet";

    public static string DateTime(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " UTC";

    /// <summary>The machine-readable form for a <c>time</c> element.</summary>
    public static string Iso(DateTimeOffset moment) => moment.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>A status as people say it: words, with spaces.</summary>
    public static string Status(string status) => status switch
    {
        "InReview" => "In review",
        "CorrectionsRequested" => "Corrections requested",
        _ => status,
    };

    /// <summary>The badge style for a status. Colour is never the only signal: the badge always carries the words.</summary>
    public static string StatusClass(string status) => status switch
    {
        "Approved" or "Issued" or "Passed" => "badge-good",
        "Denied" or "Failed" => "badge-bad",
        "CorrectionsRequested" => "badge-warn",
        "Submitted" or "InReview" or "Scheduled" => "badge-info",
        _ => "badge-plain",
    };

    /// <summary>What the fee rule means in words.</summary>
    public static string FeeRule(PermitTypeResponse type)
    {
        string basis = type.ValuationRateBasisPoints == 0
            ? string.Empty
            : $" plus {(type.ValuationRateBasisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture)}% of the project value";

        return Dollars(type.BaseFeeCents) + basis;
    }

    /// <summary>
    /// Reads an amount of dollars the way a person types it: 24000, 24,000, 24000.5 or $24,000.50. Fractions of a
    /// cent are not accepted, because rounding someone's money silently is not a thing a form should do.
    /// </summary>
    public static bool TryParseDollars(string? text, out long cents)
    {
        cents = 0;
        string cleaned = (text ?? string.Empty).Trim().TrimStart('$').Replace(",", string.Empty, StringComparison.Ordinal);

        if (cleaned.Length == 0
            || !decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal dollars)
            || dollars < 0
            || decimal.Round(dollars, 2) != dollars
            || dollars > 1_000_000_000m)
        {
            return false;
        }

        cents = (long)(dollars * 100m);

        return true;
    }

    /// <summary>Reads a percentage such as 1.25 into hundredths of a percent.</summary>
    public static bool TryParsePercentToBasisPoints(string? text, out int basisPoints)
    {
        basisPoints = 0;

        if (!decimal.TryParse((text ?? string.Empty).Trim().TrimEnd('%'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal percent)
            || percent < 0
            || decimal.Round(percent, 2) != percent
            || percent > 100)
        {
            return false;
        }

        basisPoints = (int)(percent * 100m);

        return true;
    }

    /// <summary>The same-site path a person may be sent back to, or null. Anything that could leave the site is refused.</summary>
    public static string? LocalPath(string? target) =>
        !string.IsNullOrEmpty(target)
        && target[0] == '/'
        && (target.Length == 1 || (target[1] != '/' && target[1] != '\\'))
        && !target.Any(char.IsControl)
            ? target
            : null;
}

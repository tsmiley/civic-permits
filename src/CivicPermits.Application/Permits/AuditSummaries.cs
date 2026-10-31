using System.Globalization;
using CivicPermits.Domain.Permits;

namespace CivicPermits.Application.Permits;

/// <summary>
/// The plain-words summaries the audit record keeps for permit-type changes: the fields and their old and new values.
/// Fee amounts are written in dollars; text is quoted and cut at a sensible length. Nothing personal is included.
/// </summary>
internal static class AuditSummaries
{
    private const int LongestQuotedText = 200;

    /// <summary>The values of a type that an administrator can change, taken before a change so they can be compared after.</summary>
    internal readonly record struct Values(
        PermitCategory Category,
        string Name,
        string Description,
        long BaseFeeCents,
        int ValuationRateBasisPoints,
        bool RequiresInspection,
        int ValidityDays,
        int TargetReviewDays)
    {
        public static Values Of(PermitType type) => new(
            type.Category,
            type.Name,
            type.Description,
            type.BaseFee.Cents,
            type.ValuationRateBasisPoints,
            type.RequiresInspection,
            type.ValidityDays,
            type.TargetReviewDays);
    }

    public static string Created(PermitType type) => string.Create(
        CultureInfo.InvariantCulture,
        $"Created {Quote(type.Name)}: category {type.Category}, base fee {Dollars(type.BaseFee.Cents)}, value share {Rate(type.ValuationRateBasisPoints)}, " +
        $"inspection {Required(type.RequiresInspection)}, valid {type.ValidityDays} days, target review {type.TargetReviewDays} days, description {Quote(type.Description)}.");

    public static string Revised(Values before, PermitType after)
    {
        Values now = Values.Of(after);
        List<string> changes = [];

        Add(changes, "category", before.Category.ToString(), now.Category.ToString());
        Add(changes, "name", Quote(before.Name), Quote(now.Name));
        Add(changes, "description", Quote(before.Description), Quote(now.Description));
        Add(changes, "base fee", Dollars(before.BaseFeeCents), Dollars(now.BaseFeeCents));
        Add(changes, "value share", Rate(before.ValuationRateBasisPoints), Rate(now.ValuationRateBasisPoints));
        Add(changes, "inspection", Required(before.RequiresInspection), Required(now.RequiresInspection));
        Add(changes, "validity", Days(before.ValidityDays), Days(now.ValidityDays));
        Add(changes, "target review", Days(before.TargetReviewDays), Days(now.TargetReviewDays));

        string subject = Quote(after.Name);

        return changes.Count == 0
            ? $"Saved {subject} with no change to any value."
            : $"Changed {subject}: {string.Join("; ", changes)}.";
    }

    public static string Opened(PermitType type, bool wasOpen) =>
        $"Opened {Quote(type.Name)} to new applications ({(wasOpen ? "it was already open" : "it was closed")}).";

    public static string Closed(PermitType type, bool wasOpen) =>
        $"Closed {Quote(type.Name)} to new applications ({(wasOpen ? "it was open" : "it was already closed")}).";

    private static void Add(List<string> changes, string field, string before, string after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changes.Add($"{field} from {before} to {after}");
        }
    }

    private static string Quote(string text) =>
        text.Length <= LongestQuotedText ? $"\"{text}\"" : $"\"{text[..LongestQuotedText]}...\"";

    private static string Dollars(long cents) => string.Create(CultureInfo.InvariantCulture, $"${cents / 100m:0.00}");

    private static string Rate(int basisPoints) => string.Create(CultureInfo.InvariantCulture, $"{basisPoints / 100m:0.##}%");

    private static string Required(bool required) => required ? "required" : "not required";

    private static string Days(int days) => string.Create(CultureInfo.InvariantCulture, $"{days} days");
}

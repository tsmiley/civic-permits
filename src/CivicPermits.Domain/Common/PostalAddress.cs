using System.Text.RegularExpressions;

namespace CivicPermits.Domain.Common;

/// <summary>
/// A street address in the fictional state-and-ZIP format the office uses: a street line, an optional second
/// line, a city, a two-letter region and a five-digit postal code.
/// </summary>
/// <remarks>
/// Built only through <see cref="Create"/>, which trims and validates, so an address that exists is a valid one.
/// The region is any two letters rather than a list of real states: the sample town is fictional.
/// </remarks>
public sealed partial record PostalAddress
{
    private PostalAddress(string line1, string? line2, string city, string region, string postalCode)
    {
        Line1 = line1;
        Line2 = line2;
        City = city;
        Region = region;
        PostalCode = postalCode;
    }

    /// <summary>The street number and name.</summary>
    public string Line1 { get; }

    /// <summary>The unit, suite or floor, when there is one.</summary>
    public string? Line2 { get; }

    /// <summary>The city or town.</summary>
    public string City { get; }

    /// <summary>The two-letter region code, upper case.</summary>
    public string Region { get; }

    /// <summary>The five-digit postal code.</summary>
    public string PostalCode { get; }

    /// <summary>Validates and builds an address.</summary>
    /// <exception cref="DomainRuleException">A part is missing, too long or not in the expected shape.</exception>
    public static PostalAddress Create(string? line1, string? line2, string? city, string? region, string? postalCode)
    {
        string street = Trimmed(line1);
        string? unit = string.IsNullOrWhiteSpace(line2) ? null : line2.Trim();
        string town = Trimmed(city);
        string code = Trimmed(region).ToUpperInvariant();
        string zip = Trimmed(postalCode);

        DomainRuleException.Require(street.Length is >= 3 and <= 120, "address.line1", "Enter the street address (3 to 120 characters).");
        DomainRuleException.Require(unit is null || unit.Length <= 60, "address.line2", "The second address line can be at most 60 characters.");
        DomainRuleException.Require(town.Length is >= 2 and <= 80, "address.city", "Enter the city (2 to 80 characters).");
        DomainRuleException.Require(RegionShape().IsMatch(code), "address.region", "Enter the two-letter state code.");
        DomainRuleException.Require(PostalCodeShape().IsMatch(zip), "address.postal_code", "Enter a five-digit postal code.");

        return new PostalAddress(street, unit, town, code, zip);
    }

    /// <summary>The address on one line, for lists and letters.</summary>
    public override string ToString() =>
        $"{Line1}{(Line2 is null ? string.Empty : ", " + Line2)}, {City}, {Region} {PostalCode}";

    private static string Trimmed(string? value) => value?.Trim() ?? string.Empty;

    [GeneratedRegex("^[A-Z]{2}\\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex RegionShape();

    [GeneratedRegex("^[0-9]{5}\\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex PostalCodeShape();
}

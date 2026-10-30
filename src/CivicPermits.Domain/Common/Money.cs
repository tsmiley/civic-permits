using System.Globalization;

namespace CivicPermits.Domain.Common;

/// <summary>
/// An amount of US dollars, held as a whole number of cents.
/// </summary>
/// <remarks>
/// Cents rather than <see cref="decimal"/> so the value is stored identically by every database provider
/// (SQLite cannot aggregate or compare decimals exactly) and so arithmetic cannot drift. Money is never
/// negative: a refund is a different concept and not part of this system.
/// </remarks>
public readonly record struct Money : IComparable<Money>
{
    /// <summary>Creates an amount from a count of cents.</summary>
    /// <exception cref="DomainRuleException">The count is negative.</exception>
    public Money(long cents)
    {
        DomainRuleException.Require(cents >= 0, "money.negative", "An amount of money cannot be negative.");
        Cents = cents;
    }

    /// <summary>No money.</summary>
    public static Money Zero => default;

    /// <summary>The amount as a whole number of cents.</summary>
    public long Cents { get; }

    /// <summary>The amount in dollars, for display and calculation only; never store this.</summary>
    public decimal Dollars => Cents / 100m;

    /// <summary>Creates an amount from dollars, which must not carry a fraction of a cent.</summary>
    /// <exception cref="DomainRuleException">The amount is negative or has more than two decimal places.</exception>
    public static Money FromDollars(decimal dollars)
    {
        DomainRuleException.Require(dollars >= 0, "money.negative", "An amount of money cannot be negative.");
        DomainRuleException.Require(
            decimal.Round(dollars, 2) == dollars,
            "money.fraction_of_a_cent",
            "An amount of money cannot include a fraction of a cent.");

        return new Money(checked((long)(dollars * 100m)));
    }

    /// <summary>Adds two amounts.</summary>
    public static Money operator +(Money left, Money right) => new(checked(left.Cents + right.Cents));

    /// <summary>
    /// Takes a share of this amount expressed in basis points (hundredths of a percent): 125 is 1.25%.
    /// The result is rounded half up to the nearest cent.
    /// </summary>
    public Money ApplyBasisPoints(int basisPoints)
    {
        DomainRuleException.Require(basisPoints >= 0, "money.negative_rate", "A rate cannot be negative.");

        long scaled = checked(Cents * basisPoints);

        return new Money((scaled + 5_000) / 10_000);
    }

    /// <inheritdoc />
    public int CompareTo(Money other) => Cents.CompareTo(other.Cents);

    /// <summary>Whether the left amount is smaller than the right.</summary>
    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    /// <summary>Whether the left amount is larger than the right.</summary>
    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    /// <summary>Whether the left amount is smaller than or equal to the right.</summary>
    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    /// <summary>Whether the left amount is larger than or equal to the right.</summary>
    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    /// <summary>The amount as dollars and cents, for example <c>$1,234.50</c>, in the invariant culture.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"${Dollars:N2}");
}

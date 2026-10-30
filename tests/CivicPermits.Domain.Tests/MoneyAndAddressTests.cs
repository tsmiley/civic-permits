using CivicPermits.Domain.Common;

namespace CivicPermits.Domain.Tests;

public class MoneyAndAddressTests
{
    [Theory]
    [InlineData(0, "$0.00")]
    [InlineData(5, "$0.05")]
    [InlineData(100, "$1.00")]
    [InlineData(123_450, "$1,234.50")]
    public void Money_is_written_as_dollars_and_cents(long cents, string expected)
    {
        Assert.Equal(expected, new Money(cents).ToString());
    }

    [Fact]
    public void Money_built_from_dollars_holds_exact_cents()
    {
        Assert.Equal(1_999, Money.FromDollars(19.99m).Cents);
        Assert.Equal(Money.Zero, Money.FromDollars(0m));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Money_cannot_be_negative(long cents)
    {
        DomainRuleException failure = Assert.Throws<DomainRuleException>(() => new Money(cents));

        Assert.Equal("money.negative", failure.Code);
    }

    [Fact]
    public void Dollars_with_a_fraction_of_a_cent_are_refused_rather_than_rounded()
    {
        DomainRuleException failure = Assert.Throws<DomainRuleException>(() => Money.FromDollars(10.005m));

        Assert.Equal("money.fraction_of_a_cent", failure.Code);
    }

    [Theory]
    [InlineData(10_000, 125, 125)]      // 1.25% of $100.00 is $1.25
    [InlineData(24_000_00, 125, 30_000)] // 1.25% of $24,000.00 is $300.00
    [InlineData(1, 5_000, 1)]            // half of one cent rounds up
    [InlineData(1, 4_999, 0)]            // just under half rounds down
    [InlineData(0, 1_000, 0)]
    [InlineData(12_345, 0, 0)]
    public void A_basis_point_share_is_rounded_half_up_to_the_cent(long cents, int basisPoints, long expected)
    {
        Assert.Equal(expected, new Money(cents).ApplyBasisPoints(basisPoints).Cents);
    }

    [Fact]
    public void Amounts_add_and_compare()
    {
        Money small = Money.FromDollars(5m);
        Money large = Money.FromDollars(7.50m);

        Assert.Equal(Money.FromDollars(12.50m), small + large);
        Assert.True(small < large);
        Assert.True(large >= small);
        Assert.Equal(0, small.CompareTo(Money.FromDollars(5m)));
    }

    [Fact]
    public void Adding_past_the_largest_amount_fails_instead_of_wrapping_around()
    {
        Money huge = new(long.MaxValue);

        Assert.Throws<OverflowException>(() => huge + new Money(1));
    }

    [Fact]
    public void A_valid_address_is_trimmed_and_its_region_upper_cased()
    {
        PostalAddress address = PostalAddress.Create("  100 Lantern Row ", "  Suite 4 ", " Bramblewick ", " tg ", " 70001 ");

        Assert.Equal("100 Lantern Row", address.Line1);
        Assert.Equal("Suite 4", address.Line2);
        Assert.Equal("Bramblewick", address.City);
        Assert.Equal("TG", address.Region);
        Assert.Equal("100 Lantern Row, Suite 4, Bramblewick, TG 70001", address.ToString());
    }

    [Fact]
    public void A_blank_second_line_becomes_no_second_line()
    {
        Assert.Null(PostalAddress.Create("100 Lantern Row", "   ", "Bramblewick", "TG", "70001").Line2);
    }

    [Theory]
    [InlineData(null, "Bramblewick", "TG", "70001", "address.line1")]
    [InlineData("10", "Bramblewick", "TG", "70001", "address.line1")]
    [InlineData("100 Lantern Row", "B", "TG", "70001", "address.city")]
    [InlineData("100 Lantern Row", "Bramblewick", "T", "70001", "address.region")]
    [InlineData("100 Lantern Row", "Bramblewick", "TGX", "70001", "address.region")]
    [InlineData("100 Lantern Row", "Bramblewick", "T1", "70001", "address.region")]
    [InlineData("100 Lantern Row", "Bramblewick", "TG", "7000", "address.postal_code")]
    [InlineData("100 Lantern Row", "Bramblewick", "TG", "700012", "address.postal_code")]
    [InlineData("100 Lantern Row", "Bramblewick", "TG", "7000a", "address.postal_code")]
    [InlineData("100 Lantern Row", "Bramblewick", "TG", "70\n01", "address.postal_code")]
    [InlineData("100 Lantern Row", "Bramblewick", "TG", "7000\n1", "address.postal_code")]
    public void A_malformed_address_is_refused_with_the_rule_that_failed(
        string? line1, string? city, string? region, string? postalCode, string expectedCode)
    {
        DomainRuleException failure = Assert.Throws<DomainRuleException>(
            () => PostalAddress.Create(line1, null, city, region, postalCode));

        Assert.Equal(expectedCode, failure.Code);
    }

    [Fact]
    public void Checking_an_address_finds_every_problem_not_only_the_first()
    {
        IReadOnlyList<RuleViolation> found = PostalAddress.Check("", null, "B", "T", "7");

        Assert.Equal(
            ["address.line1", "address.city", "address.region", "address.postal_code"],
            found.Select(violation => violation.Code));
        Assert.All(found, violation => Assert.False(string.IsNullOrWhiteSpace(violation.Message)));
    }

    [Fact]
    public void Checking_a_good_address_finds_nothing_and_creating_a_bad_one_reports_the_first_problem_found()
    {
        Assert.Empty(PostalAddress.Check("100 Lantern Row", "Suite 4", "Bramblewick", "TG", "70001"));

        DomainRuleException failure = Assert.Throws<DomainRuleException>(() => PostalAddress.Create("", null, "B", "T", "7"));

        Assert.Equal(PostalAddress.Check("", null, "B", "T", "7")[0].Code, failure.Code);
    }

    [Fact]
    public void Two_addresses_with_the_same_parts_are_equal()
    {
        Assert.Equal(
            PostalAddress.Create("100 Lantern Row", null, "Bramblewick", "TG", "70001"),
            PostalAddress.Create("100 Lantern Row ", null, "Bramblewick", "tg", "70001"));
    }
}

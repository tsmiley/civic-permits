using CivicPermits.Domain.Common;
using CivicPermits.Domain.Permits;

namespace CivicPermits.Domain.Tests;

public class PermitTypeTests
{
    [Theory]
    [InlineData(0, 15_000)]                 // no value-based share: just the base fee
    [InlineData(24_000_00, 45_000)]         // $150 + 1.25% of $24,000 = $450
    [InlineData(1_000_00, 16_250)]          // $150 + $12.50
    public void The_fee_is_the_base_fee_plus_the_share_of_the_declared_value(long valuationCents, long expectedFeeCents)
    {
        PermitType type = Sample.BuildingPermit();

        Assert.Equal(expectedFeeCents, type.CalculateFee(new Money(valuationCents)).Cents);
    }

    [Fact]
    public void A_type_with_no_value_based_rate_charges_its_base_fee_whatever_the_project_costs()
    {
        PermitType flat = Sample.BuildingPermit(valuationRateBasisPoints: 0);

        Assert.Equal(Money.FromDollars(150m), flat.CalculateFee(Money.FromDollars(1_000_000m)));
    }

    [Fact]
    public void Revising_a_type_replaces_what_it_says_and_charges_and_keeps_its_identity_and_state()
    {
        PermitType type = Sample.BuildingPermit();
        type.Deactivate();

        type.Revise(PermitCategory.Sign, "  Large sign permit ", "A sign bigger than thirty square feet.", Money.FromDollars(90m), 50, false, 180, 6);

        Assert.Equal(Sample.BuildingPermit().Id, type.Id);
        Assert.False(type.IsActive);
        Assert.Equal(PermitCategory.Sign, type.Category);
        Assert.Equal("Large sign permit", type.Name);
        Assert.Equal(Money.FromDollars(90m), type.BaseFee);
        Assert.Equal(50, type.ValuationRateBasisPoints);
        Assert.False(type.RequiresInspection);
        Assert.Equal(180, type.ValidityDays);
        Assert.Equal(6, type.TargetReviewDays);
    }

    [Fact]
    public void A_revision_that_breaks_a_rule_changes_nothing()
    {
        PermitType type = Sample.BuildingPermit();

        DomainRuleException failure = Assert.Throws<DomainRuleException>(
            () => type.Revise(PermitCategory.Sign, "Large sign permit", "A sign bigger than thirty square feet.", Money.Zero, 5_000, false, 180, 6));

        Assert.Equal("permit_type.valuation_rate", failure.Code);
        Assert.Equal("Residential building permit", type.Name);
        Assert.Equal(PermitCategory.Building, type.Category);
        Assert.Equal(125, type.ValuationRateBasisPoints);
    }

    [Fact]
    public void A_new_type_is_active_and_can_be_retired_and_restored()
    {
        PermitType type = Sample.BuildingPermit();

        Assert.True(type.IsActive);

        type.Deactivate();
        Assert.False(type.IsActive);

        type.Reactivate();
        Assert.True(type.IsActive);
    }

    [Fact]
    public void The_name_and_description_are_trimmed()
    {
        PermitType type = PermitType.Create(
            Guid.NewGuid(), PermitCategory.Sign, "  Sign permit  ", "  A sign on private property.  ", Money.Zero, 0, false, 365, 5);

        Assert.Equal("Sign permit", type.Name);
        Assert.Equal("A sign on private property.", type.Description);
    }

    [Theory]
    [InlineData(null, "A sign on private property.", 0, 365, 5, "permit_type.name")]
    [InlineData("Sign permit", null, 0, 365, 5, "permit_type.description")]
    [InlineData("", "A sign on private property.", 0, 365, 5, "permit_type.name")]
    [InlineData("Si", "A sign on private property.", 0, 365, 5, "permit_type.name")]
    [InlineData("Sign permit", "short", 0, 365, 5, "permit_type.description")]
    [InlineData("Sign permit", "A sign on private property.", -1, 365, 5, "permit_type.valuation_rate")]
    [InlineData("Sign permit", "A sign on private property.", 1_001, 365, 5, "permit_type.valuation_rate")]
    [InlineData("Sign permit", "A sign on private property.", 0, 29, 5, "permit_type.validity")]
    [InlineData("Sign permit", "A sign on private property.", 0, 3_651, 5, "permit_type.validity")]
    [InlineData("Sign permit", "A sign on private property.", 0, 365, 0, "permit_type.review_days")]
    [InlineData("Sign permit", "A sign on private property.", 0, 365, 91, "permit_type.review_days")]
    public void A_permit_type_outside_its_limits_is_refused_with_the_rule_that_failed(
        string? name, string? description, int basisPoints, int validityDays, int reviewDays, string expectedCode)
    {
        DomainRuleException failure = Assert.Throws<DomainRuleException>(() => PermitType.Create(
            Guid.NewGuid(), PermitCategory.Sign, name, description, Money.Zero, basisPoints, false, validityDays, reviewDays));

        Assert.Equal(expectedCode, failure.Code);
    }

    [Fact]
    public void An_undefined_category_and_an_empty_identifier_are_refused()
    {
        Assert.Equal(
            "permit_type.category",
            Assert.Throws<DomainRuleException>(() => PermitType.Create(
                Guid.NewGuid(), (PermitCategory)99, "Sign permit", "A sign on private property.", Money.Zero, 0, false, 365, 5)).Code);

        Assert.Equal(
            "permit_type.id",
            Assert.Throws<DomainRuleException>(() => PermitType.Create(
                Guid.Empty, PermitCategory.Sign, "Sign permit", "A sign on private property.", Money.Zero, 0, false, 365, 5)).Code);
    }
}

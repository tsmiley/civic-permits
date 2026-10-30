using CivicPermits.Domain.Common;

namespace CivicPermits.Domain.Permits;

/// <summary>
/// A kind of permit the office offers, with the rules that belong to it: what it costs, how long it lasts
/// and whether an inspection is part of getting it.
/// </summary>
/// <remarks>
/// The fee is a base amount plus a share of the declared project value, because a small repair and a new
/// building should not cost the same. The share is held in basis points (hundredths of a percent) so it is
/// an exact integer, not a floating-point rate.
/// </remarks>
public sealed class PermitType
{
    /// <summary>Largest value-based share allowed: 10.00% of the declared value.</summary>
    public const int MaximumValuationRateBasisPoints = 1_000;

    private PermitType()
    {
        Name = string.Empty;
        Description = string.Empty;
    }

    /// <summary>The identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The broad kind of permit.</summary>
    public PermitCategory Category { get; private set; }

    /// <summary>The name shown to applicants, unique across the office.</summary>
    public string Name { get; private set; }

    /// <summary>What the permit covers, in plain words.</summary>
    public string Description { get; private set; }

    /// <summary>The fee charged whatever the project costs.</summary>
    public Money BaseFee { get; private set; }

    /// <summary>The share of the declared project value added to the base fee, in basis points.</summary>
    public int ValuationRateBasisPoints { get; private set; }

    /// <summary>Whether an inspection must pass before the permit can be issued.</summary>
    public bool RequiresInspection { get; private set; }

    /// <summary>How many days an issued permit stays valid.</summary>
    public int ValidityDays { get; private set; }

    /// <summary>How many days the office aims to take to review an application.</summary>
    public int TargetReviewDays { get; private set; }

    /// <summary>Whether new applications can be started for this type.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Defines a permit type, checking every rule that belongs to it.</summary>
    /// <exception cref="DomainRuleException">A value is out of range or missing.</exception>
    public static PermitType Create(
        Guid id,
        PermitCategory category,
        string? name,
        string? description,
        Money baseFee,
        int valuationRateBasisPoints,
        bool requiresInspection,
        int validityDays,
        int targetReviewDays)
    {
        DomainRuleException.Require(id != Guid.Empty, "permit_type.id", "A permit type needs an identifier.");

        PermitType type = new() { Id = id, IsActive = true };
        type.Revise(category, name, description, baseFee, valuationRateBasisPoints, requiresInspection, validityDays, targetReviewDays);

        return type;
    }

    /// <summary>
    /// Changes what the type says and charges. Applications already submitted keep the fee they were assessed;
    /// only later submissions use the new rules.
    /// </summary>
    /// <exception cref="DomainRuleException">A value is out of range or missing.</exception>
    public void Revise(
        PermitCategory category,
        string? name,
        string? description,
        Money baseFee,
        int valuationRateBasisPoints,
        bool requiresInspection,
        int validityDays,
        int targetReviewDays)
    {
        string trimmedName = name?.Trim() ?? string.Empty;
        string trimmedDescription = description?.Trim() ?? string.Empty;

        DomainRuleException.Require(Enum.IsDefined(category), "permit_type.category", "Choose a permit category.");
        DomainRuleException.Require(trimmedName.Length is >= 3 and <= 80, "permit_type.name", "The name must be 3 to 80 characters.");
        DomainRuleException.Require(trimmedDescription.Length is >= 10 and <= 500, "permit_type.description", "The description must be 10 to 500 characters.");
        DomainRuleException.Require(
            valuationRateBasisPoints is >= 0 and <= MaximumValuationRateBasisPoints,
            "permit_type.valuation_rate",
            "The value-based rate must be between 0% and 10%.");
        DomainRuleException.Require(validityDays is >= 30 and <= 3_650, "permit_type.validity", "A permit must stay valid for 30 to 3,650 days.");
        DomainRuleException.Require(targetReviewDays is >= 1 and <= 90, "permit_type.review_days", "The review target must be 1 to 90 days.");

        Category = category;
        Name = trimmedName;
        Description = trimmedDescription;
        BaseFee = baseFee;
        ValuationRateBasisPoints = valuationRateBasisPoints;
        RequiresInspection = requiresInspection;
        ValidityDays = validityDays;
        TargetReviewDays = targetReviewDays;
    }

    /// <summary>The fee for a project of the declared value: the base fee plus the value-based share.</summary>
    public Money CalculateFee(Money declaredValuation) =>
        BaseFee + declaredValuation.ApplyBasisPoints(ValuationRateBasisPoints);

    /// <summary>Stops new applications for this type. Applications already made are unaffected.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Allows new applications for this type again.</summary>
    public void Reactivate() => IsActive = true;
}

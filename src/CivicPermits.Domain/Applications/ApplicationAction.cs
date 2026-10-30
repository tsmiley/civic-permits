namespace CivicPermits.Domain.Applications;

/// <summary>The things a person can do that move an application from one status to another.</summary>
public enum ApplicationAction
{
    /// <summary>Send a draft, or a corrected application, to the office.</summary>
    Submit = 1,

    /// <summary>A clerk takes a submitted application into review.</summary>
    StartReview = 2,

    /// <summary>A clerk asks the applicant to change something.</summary>
    RequestCorrections = 3,

    /// <summary>A clerk approves the application.</summary>
    Approve = 4,

    /// <summary>A clerk refuses the application, giving a reason.</summary>
    Deny = 5,

    /// <summary>A clerk issues the permit.</summary>
    Issue = 6,

    /// <summary>The applicant withdraws the application.</summary>
    Withdraw = 7,
}

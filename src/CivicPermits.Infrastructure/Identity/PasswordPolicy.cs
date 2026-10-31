namespace CivicPermits.Infrastructure.Identity;

/// <summary>The numbers of the password policy in one place, so the rule, the sample-password check and the tests agree.</summary>
public static class PasswordPolicy
{
    /// <summary>The fewest characters a new password may have (NIST SP 800-63B-4 asks for fifteen when a password is the only factor).</summary>
    public const int MinimumLength = 15;

    /// <summary>
    /// PBKDF2-HMAC-SHA512 iterations for a new password hash. The security baseline's floor (docs/THREAT_MODEL.md) is 220,000 and Identity's default is
    /// 100,000. Each hash records the count it was made with, so raising this later upgrades hashes at the next sign-in.
    /// </summary>
    public const int IterationCount = 250_000;
}

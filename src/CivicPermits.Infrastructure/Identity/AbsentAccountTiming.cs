using CivicPermits.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace CivicPermits.Infrastructure.Identity;

/// <summary>
/// Makes signing in with an address nobody has registered cost the same as signing in with one that exists. Checking a
/// password is deliberately slow; if the unknown-address path skipped it, the answer would come back measurably faster
/// and the speed alone would say which addresses have accounts.
/// </summary>
public sealed class AbsentAccountTiming(IPasswordHasher<ApplicationUser> hasher)
{
    // Not a credential of anyone's: a fixed string, hashed once for the whole process (not once per request, which
    // would make the unknown-address path cost two hashes against the real path's one), only so there is a real hash to check.
    private static readonly ApplicationUser Nobody = new();
    private static string? _hash;

    /// <summary>Does the work of a password check and throws the answer away.</summary>
    public void SpendTheTimeOfACheck(string password)
    {
        string hash = LazyInitializer.EnsureInitialized(ref _hash, () => hasher.HashPassword(Nobody, "a password that no account has"));

        hasher.VerifyHashedPassword(Nobody, hash, password);
    }
}

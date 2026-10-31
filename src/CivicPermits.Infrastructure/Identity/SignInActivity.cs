using CivicPermits.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace CivicPermits.Infrastructure.Identity;

/// <summary>Notes when a person last signed in, which is where the retention period for an unused account is counted from.</summary>
public sealed class SignInActivity(UserManager<ApplicationUser> users, TimeProvider time)
{
    /// <summary>Records now as the person's last sign-in.</summary>
    /// <exception cref="InvalidOperationException">The account could not be updated.</exception>
    public async Task RecordAsync(ApplicationUser person)
    {
        ArgumentNullException.ThrowIfNull(person);

        person.LastSignInOn = time.GetUtcNow();
        IdentityResult saved = await users.UpdateAsync(person);

        if (!saved.Succeeded)
        {
            throw new InvalidOperationException("Could not record the sign-in: " + string.Join("; ", saved.Errors.Select(error => error.Code)));
        }
    }
}

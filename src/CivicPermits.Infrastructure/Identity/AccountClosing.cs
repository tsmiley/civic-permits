using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;
using CivicPermits.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace CivicPermits.Infrastructure.Identity;

/// <summary>What closing an account came to.</summary>
/// <param name="Closed">Whether the account was closed.</param>
/// <param name="Problem">When it was not: what to tell the person, in words that reveal nothing else.</param>
public sealed record ClosingOutcome(bool Closed, string? Problem = null);

/// <summary>
/// Lets an applicant close their own account. The name and email address are removed from the account, it can never sign
/// in again, and every session and refresh token it had ends. The applications stay, under the retention periods, because
/// they are the office's record; they are no longer tied to a name or an address anyone can read.
/// </summary>
/// <remarks>
/// The account row itself is kept so the applications still point at something, with an opaque placeholder in place of the
/// address (<c>closed-{id}@closed.invalid</c>, a reserved domain that reaches no mailbox) because Identity requires a unique
/// one. The password is asked again, and a wrong one counts toward the lockout, so a borrowed session cannot close it.
/// </remarks>
public sealed class AccountClosing(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    CivicPermitsDbContext database,
    TimeProvider time)
{
    private const string WrongPassword = "The password is not right.";

    /// <summary>Closes the applicant's account after checking their password.</summary>
    /// <exception cref="InvalidOperationException">The account could not be updated, so nothing was closed.</exception>
    public async Task<ClosingOutcome> CloseApplicantAsync(Guid userId, string? password)
    {
        ApplicationUser? person = await users.FindByIdAsync(userId.ToString());

        if (person is null || !await users.IsInRoleAsync(person, nameof(ActorRole.Applicant)))
        {
            return new ClosingOutcome(false, "Only an applicant can close their own account here.");
        }

        if (string.IsNullOrEmpty(password) || !(await signIn.CheckPasswordSignInAsync(person, password, lockoutOnFailure: true)).Succeeded)
        {
            return new ClosingOutcome(false, WrongPassword);
        }

        string placeholder = $"closed-{person.Id:N}";
        person.DisplayName = string.Empty;
        person.UserName = placeholder;
        person.NormalizedUserName = placeholder.ToUpperInvariant();
        person.Email = placeholder + "@closed.invalid";
        person.NormalizedEmail = person.Email.ToUpperInvariant();
        person.EmailConfirmed = false;
        person.PhoneNumber = null;
        person.PasswordHash = null;
        person.LockoutEnabled = true;
        person.LockoutEnd = DateTimeOffset.MaxValue;
        person.SecurityStamp = Guid.NewGuid().ToString("N");

        // The entry is added to the same context the user update saves through, so the closing and its record are one save.
        database.AuditEntries.Add(AuditEntry.Record(
            Guid.CreateVersion7(),
            time.GetUtcNow(),
            person.Id,
            "account.closed",
            "Account",
            person.Id,
            "An applicant closed their account. Their name and email address were removed; their applications were kept under the retention periods."));

        IdentityResult saved = await users.UpdateAsync(person);

        if (!saved.Succeeded)
        {
            throw new InvalidOperationException("Could not close the account: " + string.Join("; ", saved.Errors.Select(error => error.Code)));
        }

        return new ClosingOutcome(true);
    }
}

using System.ComponentModel.DataAnnotations;
using CivicPermits.Domain.Applications;
using CivicPermits.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace CivicPermits.Infrastructure.Identity;

/// <summary>What registering an applicant account came to.</summary>
/// <param name="Errors">What the person must fix, by field. Empty when the request was accepted.</param>
public sealed record RegistrationOutcome(IReadOnlyDictionary<string, string[]> Errors)
{
    /// <summary>Whether the request was accepted. It is accepted whether or not the address was already registered.</summary>
    public bool Accepted => Errors.Count == 0;
}

/// <summary>
/// Makes applicant accounts. Both hosts register people through this one method, so the rules cannot drift apart.
/// </summary>
/// <remarks>
/// A taken address is answered exactly like a new one, so the response cannot be used to find out who has an
/// account. There is no email confirmation in this demonstration; a production service would send one and
/// would be the place to tell the owner of a taken address.
/// </remarks>
public sealed class RegistrationService(UserManager<ApplicationUser> users)
{
    /// <summary>Validates the details and creates an applicant account.</summary>
    public async Task<RegistrationOutcome> RegisterApplicantAsync(string? email, string? password, string? displayName)
    {
        Dictionary<string, string[]> errors = [];

        string address = email?.Trim() ?? string.Empty;
        string name = displayName?.Trim() ?? string.Empty;

        if (address.Length is 0 or > 256 || !new EmailAddressAttribute().IsValid(address))
        {
            errors["email"] = ["Enter a valid email address."];
        }

        if (name.Length is < 2 or > 80)
        {
            errors["displayName"] = ["Enter your name (2 to 80 characters)."];
        }

        if (string.IsNullOrEmpty(password))
        {
            errors["password"] = ["Enter a password."];
        }

        if (errors.Count > 0)
        {
            return new RegistrationOutcome(errors);
        }

        ApplicationUser person = new() { Id = Guid.CreateVersion7(), UserName = address, Email = address, DisplayName = name };
        IdentityResult created = await users.CreateAsync(person, password!);

        if (!created.Succeeded)
        {
            string[] fixable = [.. created.Errors
                .Where(error => !error.Code.StartsWith("Duplicate", StringComparison.Ordinal))
                .Select(error => error.Description)];

            return fixable.Length == 0
                ? new RegistrationOutcome(new Dictionary<string, string[]>())
                : new RegistrationOutcome(new Dictionary<string, string[]> { ["password"] = fixable });
        }

        // An account with no role could sign in and do nothing, which is a worse failure than none. If the role cannot
        // be given, undo the account and say so loudly. The usual cause is that the roles were never created (a
        // deployment that skipped them), and Identity reports that by throwing, not by returning a failure.
        string failure;

        try
        {
            IdentityResult assigned = await users.AddToRoleAsync(person, nameof(ActorRole.Applicant));

            if (assigned.Succeeded)
            {
                return new RegistrationOutcome(new Dictionary<string, string[]>());
            }

            failure = string.Join("; ", assigned.Errors.Select(error => error.Code));
        }
        catch (InvalidOperationException problem)
        {
            failure = problem.Message;
        }

        await users.DeleteAsync(person);

        throw new InvalidOperationException($"Could not give a new account the {nameof(ActorRole.Applicant)} role, so it was removed: {failure}");
    }
}

using CivicPermits.Domain.Applications;
using CivicPermits.Infrastructure.Persistence;
using CivicPermits.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;

namespace CivicPermits.Infrastructure.Identity;

/// <summary>
/// Makes sure the four roles exist and, when asked, creates the sample sign-ins. The roles are the same four the
/// domain uses, spelled the same way, so a role claim maps straight onto an <see cref="ActorRole"/>.
/// </summary>
public sealed class IdentityBootstrapper(UserManager<ApplicationUser> users, RoleManager<IdentityRole<Guid>> roles, TimeProvider time)
{
    private static readonly (Guid Id, string Email, string Name, ActorRole Role)[] SampleUsers =
    [
        (SampleIdentities.ApplicantOne, "applicant.one@bramblewick.test", "Sample Applicant One", ActorRole.Applicant),
        (SampleIdentities.ApplicantTwo, "applicant.two@bramblewick.test", "Sample Applicant Two", ActorRole.Applicant),
        (SampleIdentities.ApplicantThree, "applicant.three@bramblewick.test", "Sample Applicant Three", ActorRole.Applicant),
        (SampleIdentities.Clerk, "clerk@bramblewick.test", "Sample Clerk", ActorRole.Clerk),
        (SampleIdentities.Administrator, "administrator@bramblewick.test", "Sample Administrator", ActorRole.Administrator),
        (SampleIdentities.InspectorOne, "inspector.one@bramblewick.test", "Sample Inspector One", ActorRole.Inspector),
        (SampleIdentities.InspectorTwo, "inspector.two@bramblewick.test", "Sample Inspector Two", ActorRole.Inspector),
    ];

    /// <summary>Creates any of the four roles that is missing.</summary>
    public async Task EnsureRolesAsync()
    {
        foreach (ActorRole role in Enum.GetValues<ActorRole>())
        {
            if (!await roles.RoleExistsAsync(role.ToString()))
            {
                IdentityResult result = await roles.CreateAsync(new IdentityRole<Guid>(role.ToString()) { Id = Guid.CreateVersion7() });
                Require(result, $"create role {role}");
            }
        }
    }

    /// <summary>
    /// Creates the sample people with the given password, skipping any that exist. The password comes from
    /// configuration at run time, never from the repository, and the sample addresses use the reserved
    /// <c>.test</c> domain so they can never reach a real mailbox.
    /// </summary>
    /// <returns>How many sign-ins were created.</returns>
    public async Task<int> EnsureSampleUsersAsync(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        int created = 0;

        foreach ((Guid id, string email, string name, ActorRole role) in SampleUsers)
        {
            if (await users.FindByIdAsync(id.ToString()) is not null)
            {
                continue;
            }

            ApplicationUser person = new()
            {
                Id = id,
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = name,
                CreatedOn = time.GetUtcNow(),
            };

            Require(await users.CreateAsync(person, password), $"create the sample user {role}");
            Require(await users.AddToRoleAsync(person, role.ToString()), $"give the sample user the {role} role");
            created++;
        }

        return created;
    }

    private static void Require(IdentityResult result, string what)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Could not {what}: {string.Join("; ", result.Errors.Select(error => error.Code))}.");
        }
    }
}

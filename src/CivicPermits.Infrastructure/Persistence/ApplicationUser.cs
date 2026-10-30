using Microsoft.AspNetCore.Identity;

namespace CivicPermits.Infrastructure.Persistence;

/// <summary>
/// A person who signs in: an applicant, a clerk, an inspector or an administrator.
/// Roles are assigned through ASP.NET Core Identity; this type adds only what the office needs to show.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>The name shown in the interface and on correspondence.</summary>
    public string DisplayName { get; set; } = string.Empty;
}

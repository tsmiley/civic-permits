using System.Security.Claims;
using CivicPermits.Application.Abstractions;
using CivicPermits.Domain.Applications;
using Microsoft.AspNetCore.Http;

namespace CivicPermits.Infrastructure.Identity;

/// <summary>
/// Reads who is signed in from the request's claims. A person with several roles acts in the most senior one,
/// so an administrator who also applies for a permit is treated as an administrator.
/// </summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    // Most senior first.
    private static readonly ActorRole[] Seniority =
        [ActorRole.Administrator, ActorRole.Clerk, ActorRole.Inspector, ActorRole.Applicant];

    public Actor? Actor
    {
        get
        {
            ClaimsPrincipal? principal = accessor.HttpContext?.User;

            if (principal?.Identity is not { IsAuthenticated: true })
            {
                return null;
            }

            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id))
            {
                return null;
            }

            foreach (ActorRole role in Seniority)
            {
                if (principal.IsInRole(role.ToString()))
                {
                    return new Actor(id, role);
                }
            }

            return null;
        }
    }
}

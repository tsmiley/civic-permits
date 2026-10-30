using CivicPermits.Application.Abstractions;
using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;

namespace CivicPermits.Application.Permits;

/// <summary>The checks every handler makes before it touches anything: who is asking, and may they see it.</summary>
internal static class Access
{
    public const string ApplicationNotFound = "application.not_found";

    /// <summary>The signed-in person, or an Unauthorized failure.</summary>
    public static Actor Require(this ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.Actor
            ?? throw new RequestException(RequestFailure.Unauthorized, "request.unauthorized", "Sign in to do this.");
    }

    /// <summary>The signed-in person, who must act in one of the given roles.</summary>
    public static Actor Require(this ICurrentUser user, params ActorRole[] roles)
    {
        Actor actor = user.Require();

        return roles.Contains(actor.Role)
            ? actor
            : throw RequestException.Forbidden("You do not have permission to do this.");
    }

    /// <summary>
    /// Whether the person may know the application exists. Applicants see their own, staff see all, and an
    /// inspector sees those with an inspection booked for them or carried out by them: an inspection that was called
    /// off before it happened no longer counts, so a cancelled booking does not leave the applicant's name, address and
    /// project readable. To anyone else it does not exist.
    /// </summary>
    public static bool CanSee(this Actor actor, PermitApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return actor.Role switch
        {
            ActorRole.Applicant => application.ApplicantId == actor.Id,
            ActorRole.Clerk or ActorRole.Administrator => true,
            ActorRole.Inspector => application.Inspections.Any(inspection =>
                inspection.InspectorId == actor.Id && inspection.Status != InspectionStatus.Cancelled),
            _ => false,
        };
    }

    /// <summary>Loads an application the person may see, ready to change; otherwise reports it as not found.</summary>
    public static async Task<PermitApplication> LoadVisibleAsync(
        this IPermitApplicationStore store, Guid id, Actor actor, CancellationToken cancellationToken)
    {
        PermitApplication? application = await store.FindAsync(id, cancellationToken);

        return application is not null && actor.CanSee(application)
            ? application
            : throw RequestException.NotFound(ApplicationNotFound, "That application was not found.");
    }

    /// <summary>A rule violation raised by the request's own shape rather than by the domain.</summary>
    public static DomainRuleException Invalid(string code, string message) => new(code, message);
}

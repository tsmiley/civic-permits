namespace CivicPermits.Domain.Applications;

/// <summary>Who is doing something to an application: a person and the role they are acting in.</summary>
/// <param name="Id">The person's identifier.</param>
/// <param name="Role">The role they act in.</param>
public readonly record struct Actor(Guid Id, ActorRole Role)
{
    /// <summary>Whether the role is office staff who can run an application through review.</summary>
    public bool IsStaff => Role is ActorRole.Clerk or ActorRole.Administrator;
}

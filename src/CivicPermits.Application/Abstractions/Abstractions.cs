using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;
using CivicPermits.Domain.Permits;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Application.Abstractions;

/// <summary>The person making the current request, as the host has authenticated them.</summary>
public interface ICurrentUser
{
    /// <summary>Who they are and the role they act in, or null when nobody is signed in.</summary>
    Actor? Actor { get; }
}

/// <summary>Saves everything the handler changed as one unit, or none of it.</summary>
public interface IUnitOfWork
{
    /// <summary>Saves the changes.</summary>
    /// <exception cref="RequestException">Someone else changed the same record first, or a uniqueness rule was hit.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Where permit types are kept.</summary>
public interface IPermitTypeStore
{
    /// <summary>Every type, or only those open to new applications, ordered by category and name.</summary>
    Task<IReadOnlyList<PermitType>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    /// <summary>One type, ready to change, or null.</summary>
    Task<PermitType?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether another type already has this name, ignoring case.</summary>
    Task<bool> NameInUseAsync(string name, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Starts keeping a new type.</summary>
    void Add(PermitType type);
}

/// <summary>The audit record: what privileged people did, only ever added to.</summary>
public interface IAuditLog
{
    /// <summary>Starts keeping an entry. It is saved with the handler's own changes, so a refused or failed action leaves none.</summary>
    void Add(AuditEntry entry);

    /// <summary>One page of entries, newest first, with the names of the accounts that acted.</summary>
    Task<PagedResponse<AuditEntryResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
}
/// <summary>Which applications a search should return.</summary>
/// <param name="Status">Only this status, or any.</param>
/// <param name="ApplicantId">Only this applicant's, or anyone's.</param>
/// <param name="InspectorId">Only those with an inspection booked for this inspector, or all.</param>
/// <param name="Page">The page, from 1.</param>
/// <param name="PageSize">The most results on a page.</param>
public sealed record ApplicationSearch(ApplicationStatus? Status, Guid? ApplicantId, Guid? InspectorId, int Page, int PageSize);

/// <summary>Where applications are kept.</summary>
public interface IPermitApplicationStore
{
    /// <summary>One application with its history and inspections, ready to change, or null.</summary>
    Task<PermitApplication?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The application with this public number, read only, or null.</summary>
    Task<PermitApplication?> FindByNumberAsync(PermitNumber number, CancellationToken cancellationToken);

    /// <summary>Starts keeping a new application.</summary>
    void Add(PermitApplication application);

    /// <summary>One page of matching applications, newest first.</summary>
    Task<PagedResponse<ApplicationSummaryResponse>> SearchAsync(ApplicationSearch search, CancellationToken cancellationToken);
}

/// <summary>Hands out public permit numbers, one sequence per year.</summary>
public interface IPermitNumberAllocator
{
    /// <summary>
    /// The next unused number for the year. The claim is saved with the handler's own changes, so a request that
    /// fails never uses up a number, and two requests can never be given the same one.
    /// </summary>
    Task<PermitNumber> NextAsync(int year, CancellationToken cancellationToken);
}

/// <summary>Answers questions about people that only the identity store knows.</summary>
public interface IUserDirectory
{
    /// <summary>Whether the person exists and acts as an inspector.</summary>
    Task<bool> IsInspectorAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Every inspector, by name.</summary>
    Task<IReadOnlyList<InspectorResponse>> ListInspectorsAsync(CancellationToken cancellationToken);
}

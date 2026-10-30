using CivicPermits.Application.Abstractions;
using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Permits;
using CivicPermits.Shared.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CivicPermits.Infrastructure.Persistence;

internal sealed class PermitTypeStore(CivicPermitsDbContext database) : IPermitTypeStore
{
    public async Task<IReadOnlyList<PermitType>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        IQueryable<PermitType> query = database.PermitTypes.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(type => type.IsActive);
        }

        return await query.OrderBy(type => type.Category).ThenBy(type => type.Name).ToListAsync(cancellationToken);
    }

    public Task<PermitType?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        database.PermitTypes.FirstOrDefaultAsync(type => type.Id == id, cancellationToken);

    public async Task<bool> NameInUseAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        // Compared in memory so that "Sign permit" and "SIGN PERMIT" clash on SQLite as well as on SQL Server,
        // whose collations differ. There are a few dozen types at most.
        var existing = await database.PermitTypes.AsNoTracking().Select(type => new { type.Id, type.Name }).ToListAsync(cancellationToken);

        return existing.Exists(type => type.Id != exceptId && string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public void Add(PermitType type) => database.PermitTypes.Add(type);
}

internal sealed class PermitApplicationStore(CivicPermitsDbContext database) : IPermitApplicationStore
{
    public Task<PermitApplication?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        database.PermitApplications
            .Include(application => application.History)
            .Include(application => application.Inspections)
            .AsSplitQuery()
            .FirstOrDefaultAsync(application => application.Id == id, cancellationToken);

    public Task<PermitApplication?> FindByNumberAsync(PermitNumber number, CancellationToken cancellationToken) =>
        database.PermitApplications.AsNoTracking().FirstOrDefaultAsync(application => application.Number == number, cancellationToken);

    public void Add(PermitApplication application) => database.PermitApplications.Add(application);

    public async Task<PagedResponse<ApplicationSummaryResponse>> SearchAsync(ApplicationSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        IQueryable<PermitApplication> query = database.PermitApplications.AsNoTracking();

        if (search.Status is { } status)
        {
            query = query.Where(application => application.Status == status);
        }

        if (search.ApplicantId is { } applicantId)
        {
            query = query.Where(application => application.ApplicantId == applicantId);
        }

        if (search.InspectorId is { } inspectorId)
        {
            query = query.Where(application => application.Inspections.Any(inspection => inspection.InspectorId == inspectorId));
        }

        int total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(application => application.CreatedOn)
            .ThenBy(application => application.Id)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Join(
                database.PermitTypes,
                application => application.PermitTypeId,
                type => type.Id,
                (application, type) => new
                {
                    application.Id,
                    application.Number,
                    TypeName = type.Name,
                    application.Status,
                    application.ProjectDescription,
                    application.CreatedOn,
                    application.SubmittedOn,
                    application.FeeAssessed,
                })
            .ToListAsync(cancellationToken);

        // A join does not promise to keep the order of its outer side, so the page is sorted again.
        return new PagedResponse<ApplicationSummaryResponse>(
            [.. rows
                .OrderByDescending(row => row.CreatedOn).ThenBy(row => row.Id)
                .Select(row => new ApplicationSummaryResponse(
                    row.Id,
                    row.Number?.ToString(),
                    row.TypeName,
                    row.Status.ToString(),
                    row.ProjectDescription,
                    row.CreatedOn,
                    row.SubmittedOn,
                    row.FeeAssessed.Cents))],
            search.Page,
            search.PageSize,
            total);
    }
}

internal sealed class PermitNumberAllocator(CivicPermitsDbContext database) : IPermitNumberAllocator
{
    public async Task<PermitNumber> NextAsync(int year, CancellationToken cancellationToken)
    {
        PermitNumberCounter? counter = await database.Set<PermitNumberCounter>()
            .FirstOrDefaultAsync(candidate => candidate.Year == year, cancellationToken);

        if (counter is null)
        {
            counter = new PermitNumberCounter { Year = year };
            database.Set<PermitNumberCounter>().Add(counter);
        }

        counter.LastSequence++;

        return PermitNumber.From(year, counter.LastSequence);
    }
}

internal sealed class UnitOfWork(CivicPermitsDbContext database) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new RequestException(
                RequestFailure.Conflict,
                "request.concurrent_change",
                "Someone else changed this at the same moment. Reload it and try again.",
                exception);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new RequestException(
                RequestFailure.Conflict,
                "request.duplicate",
                "That clashes with something that already exists. Reload and try again.",
                exception);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException switch
    {
        SqliteException sqlite => sqlite.SqliteErrorCode == 19 && sqlite.SqliteExtendedErrorCode is 1555 or 2067,
        SqlException sql => sql.Number is 2601 or 2627,
        _ => false,
    };
}

internal sealed class UserDirectory(UserManager<ApplicationUser> users) : IUserDirectory
{
    public async Task<bool> IsInspectorAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplicationUser? person = await users.FindByIdAsync(userId.ToString());

        return person is not null && await users.IsInRoleAsync(person, nameof(ActorRole.Inspector));
    }

    public async Task<IReadOnlyList<InspectorResponse>> ListInspectorsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IList<ApplicationUser> inspectors = await users.GetUsersInRoleAsync(nameof(ActorRole.Inspector));

        return [.. inspectors
            .OrderBy(person => person.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(person => new InspectorResponse(person.Id, person.DisplayName))];
    }
}

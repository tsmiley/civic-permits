using CivicPermits.Application.Abstractions;
using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;
using CivicPermits.Infrastructure.Persistence;
using CivicPermits.Shared.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CivicPermits.Infrastructure.Identity;

/// <summary>
/// Removes what has outlived its retention period (<see cref="RetentionPolicy"/>): decided applications seven years after
/// their last change, drafts and withdrawn applications one year after, and applicant accounts with no application two
/// years after they were last used. An application still in progress is never removed by age.
/// </summary>
/// <remarks>
/// The removal and its audit entry are one transaction, so a purge either happened and is recorded or did not happen. A
/// dry run counts and changes nothing. An account from before account creation was recorded has no date to count from, so
/// the first real run starts its clock instead of removing it.
/// </remarks>
public sealed class RetentionService(CivicPermitsDbContext database, UserManager<ApplicationUser> users) : IRetentionService
{
    /// <inheritdoc />
    public async Task<RetentionResponse> RunAsync(Guid actorId, DateTimeOffset now, bool dryRun, CancellationToken cancellationToken)
    {
        // The shortest period is a year, so anything created within the last year cannot have expired: a cheap first cut
        // in the database, before each application's last change is worked out from its history. It is a little loose
        // (a day) on purpose, so a leap day can never leave out something the exact rule below would remove.
        DateTimeOffset oldest = now.AddYears(-RetentionPolicy.DraftOrWithdrawnApplicationYears).AddDays(1);
        List<PermitApplication> candidates = await database.PermitApplications
            .Include(application => application.History)
            .Include(application => application.Inspections)
            .AsSplitQuery()
            .Where(application => application.CreatedOn <= oldest)
            .ToListAsync(cancellationToken);

        List<PermitApplication> expired = [.. candidates.Where(application =>
            RetentionPolicy.IsExpired(application.Status, application.LastChangedOn(), now))];
        int decided = expired.Count(application => RetentionPolicy.ClassOf(application.Status) == RetentionClass.Decided);
        int drafts = expired.Count(application => RetentionPolicy.ClassOf(application.Status) == RetentionClass.Draft);
        int withdrawn = expired.Count(application => RetentionPolicy.ClassOf(application.Status) == RetentionClass.Withdrawn);

        (List<ApplicationUser> unused, List<ApplicationUser> unstamped) = await FindUnusedAccountsAsync(now, expired, cancellationToken);
        RetentionResponse result = new(dryRun, decided, drafts, withdrawn, unused.Count);

        if (dryRun)
        {
            return result;
        }

        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        database.PermitApplications.RemoveRange(expired);

        foreach (ApplicationUser person in unstamped)
        {
            person.CreatedOn = now;
            Require(await users.UpdateAsync(person), "start an account's retention clock");
        }

        foreach (ApplicationUser person in unused)
        {
            Require(await users.DeleteAsync(person), "remove an unused account");
        }

        database.AuditEntries.Add(AuditEntry.Record(
            Guid.CreateVersion7(),
            now,
            actorId,
            "retention.purged",
            "Retention",
            subjectId: null,
            $"Removed {decided} decided application(s) past {RetentionPolicy.DecidedApplicationYears} years, {drafts} draft(s) and {withdrawn} withdrawn " +
            $"application(s) past {RetentionPolicy.DraftOrWithdrawnApplicationYears} year, and {unused.Count} unused applicant account(s) past {RetentionPolicy.InactiveAccountYears} years."));

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    /// <summary>
    /// Applicant accounts with no application, last used (or made) long enough ago. An account whose applications are all
    /// being removed in this run is left for the next one. An account with no date at all is returned separately.
    /// </summary>
    private async Task<(List<ApplicationUser> Unused, List<ApplicationUser> Unstamped)> FindUnusedAccountsAsync(
        DateTimeOffset now, List<PermitApplication> beingRemoved, CancellationToken cancellationToken)
    {
        HashSet<Guid> withApplications = [.. await database.PermitApplications.Select(application => application.ApplicantId).Distinct().ToListAsync(cancellationToken)];
        withApplications.UnionWith(beingRemoved.Select(application => application.ApplicantId));

        List<ApplicationUser> unused = [];
        List<ApplicationUser> unstamped = [];

        foreach (ApplicationUser person in await users.GetUsersInRoleAsync(nameof(ActorRole.Applicant)))
        {
            if (withApplications.Contains(person.Id))
            {
                continue;
            }

            if ((person.LastSignInOn ?? person.CreatedOn) is not { } lastActive)
            {
                unstamped.Add(person);
            }
            else if (RetentionPolicy.AccountIsExpired(lastActive, now))
            {
                unused.Add(person);
            }
        }

        return (unused, unstamped);
    }

    private static void Require(IdentityResult result, string what)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Could not {what}: {string.Join("; ", result.Errors.Select(error => error.Code))}.");
        }
    }
}

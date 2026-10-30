using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Permits;
using Microsoft.EntityFrameworkCore;

namespace CivicPermits.Infrastructure.Persistence.Seeding;

/// <summary>
/// Puts the fictional town's permit types and applications into an empty or part-filled database. It is safe to
/// run any number of times: anything already there, found by its fixed identifier, is left exactly as it is.
/// </summary>
public sealed class SampleDataSeeder(CivicPermitsDbContext database, TimeProvider time)
{
    /// <summary>Adds whatever sample data is missing and returns how many permit types and applications were added.</summary>
    public async Task<(int PermitTypes, int Applications)> SeedAsync(CancellationToken cancellationToken = default)
    {
        HashSet<Guid> knownTypes = [.. await database.PermitTypes.Select(type => type.Id).ToListAsync(cancellationToken)];
        List<PermitType> newTypes = [.. SampleData.PermitTypes().Where(type => !knownTypes.Contains(type.Id))];

        database.PermitTypes.AddRange(newTypes);
        await database.SaveChangesAsync(cancellationToken);

        // The sample applications are worked out from the sample definitions, not from whatever the database holds
        // now: an administrator may since have retired or repriced a type, and that must not stop the rest being added.
        Dictionary<Guid, PermitType> types = SampleData.PermitTypes().ToDictionary(type => type.Id);
        HashSet<Guid> knownApplications = [.. await database.PermitApplications.Select(application => application.Id).ToListAsync(cancellationToken)];

        List<PermitApplication> newApplications =
        [
            .. SampleData.Applications(types, time.GetUtcNow()).Where(application => !knownApplications.Contains(application.Id)),
        ];

        database.PermitApplications.AddRange(newApplications);
        await AdvanceNumberCountersAsync(newApplications, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);

        return (newTypes.Count, newApplications.Count);
    }

    /// <summary>
    /// The sample applications carry numbers the allocator never handed out. Moving each year's counter up to the
    /// highest of them means the next real submission gets the next free number instead of a duplicate.
    /// </summary>
    private async Task AdvanceNumberCountersAsync(IEnumerable<PermitApplication> seeded, CancellationToken cancellationToken)
    {
        foreach (var year in seeded.Where(application => application.Number is not null)
                     .GroupBy(application => application.Number!.Value.Year))
        {
            int highest = year.Max(application => application.Number!.Value.Sequence);
            PermitNumberCounter? counter = await database.Set<PermitNumberCounter>()
                .FirstOrDefaultAsync(candidate => candidate.Year == year.Key, cancellationToken);

            if (counter is null)
            {
                database.Set<PermitNumberCounter>().Add(new PermitNumberCounter { Year = year.Key, LastSequence = highest });
            }
            else if (counter.LastSequence < highest)
            {
                counter.LastSequence = highest;
            }
        }
    }
}

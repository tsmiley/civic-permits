using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;
using CivicPermits.Domain.Permits;
using CivicPermits.Infrastructure.Persistence;
using CivicPermits.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace CivicPermits.Infrastructure.Tests;

public class SeedAndPersistenceTests
{
    private static async Task<(int, int)> SeedAsync(SqliteTestDatabase database)
    {
        await using CivicPermitsDbContext context = database.CreateContext();

        return await new SampleDataSeeder(context, database.Time).SeedAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<List<PermitApplication>> LoadApplicationsAsync(SqliteTestDatabase database)
    {
        await using CivicPermitsDbContext context = database.CreateContext();

        return await context.PermitApplications.AsNoTracking().Include(a => a.History).Include(a => a.Inspections).ToListAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Seeding_an_empty_database_adds_the_whole_town()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();

        Assert.Equal((9, 12), await SeedAsync(database));

        List<PermitApplication> applications = await LoadApplicationsAsync(database);
        Dictionary<ApplicationStatus, int> byStatus = applications.GroupBy(a => a.Status).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(12, applications.Count);
        Assert.Equal(1, byStatus[ApplicationStatus.Draft]);
        Assert.Equal(2, byStatus[ApplicationStatus.Submitted]);
        Assert.Equal(1, byStatus[ApplicationStatus.InReview]);
        Assert.Equal(1, byStatus[ApplicationStatus.CorrectionsRequested]);
        Assert.Equal(3, byStatus[ApplicationStatus.Approved]);
        Assert.Equal(1, byStatus[ApplicationStatus.Denied]);
        Assert.Equal(1, byStatus[ApplicationStatus.Withdrawn]);
        Assert.Equal(2, byStatus[ApplicationStatus.Issued]);
    }

    [Fact]
    public async Task Seeding_twice_changes_nothing_the_second_time()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await SeedAsync(database);
        List<Guid> before = [.. (await LoadApplicationsAsync(database)).OrderBy(a => a.Id).Select(a => a.Id)];

        Assert.Equal((0, 0), await SeedAsync(database));

        List<PermitApplication> after = await LoadApplicationsAsync(database);
        Assert.Equal(before, after.OrderBy(a => a.Id).Select(a => a.Id));
    }

    [Fact]
    public async Task Seeding_fills_in_only_what_is_missing_and_leaves_changes_alone()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await SeedAsync(database);

        await using (CivicPermitsDbContext context = database.CreateContext())
        {
            PermitType fence = await context.PermitTypes.SingleAsync(t => t.Name == "Fence and small structure permit", TestContext.Current.CancellationToken);
            fence.Deactivate();
            context.PermitApplications.RemoveRange(await context.PermitApplications.Where(a => a.Status == ApplicationStatus.Draft).ToListAsync(TestContext.Current.CancellationToken));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal((0, 1), await SeedAsync(database));

        await using CivicPermitsDbContext check = database.CreateContext();
        Assert.False((await check.PermitTypes.SingleAsync(t => t.Name == "Fence and small structure permit", TestContext.Current.CancellationToken)).IsActive);
    }

    [Fact]
    public async Task Sample_applications_have_a_history_that_adds_up()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await SeedAsync(database);

        foreach (PermitApplication application in await LoadApplicationsAsync(database))
        {
            if (application.History.Count == 0)
            {
                Assert.Equal(ApplicationStatus.Draft, application.Status);
                Assert.Null(application.Number);
                continue;
            }

            List<StatusChange> history = [.. application.History.OrderBy(change => change.At)];

            Assert.Equal(ApplicationStatus.Draft, history[0].From);
            Assert.Equal(application.Status, history[^1].To);
            Assert.NotNull(application.Number);

            for (int i = 1; i < history.Count; i++)
            {
                Assert.Equal(history[i - 1].To, history[i].From);
            }
        }
    }

    [Fact]
    public async Task Every_issued_permit_was_paid_for_and_expires_after_it_was_issued()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await SeedAsync(database);

        List<PermitApplication> issued = [.. (await LoadApplicationsAsync(database)).Where(a => a.Status == ApplicationStatus.Issued)];

        Assert.Equal(2, issued.Count);
        Assert.All(issued, application =>
        {
            Assert.NotNull(application.FeePaidOn);
            Assert.NotNull(application.IssuedOn);
            Assert.True(application.ExpiresOn > DateOnly.FromDateTime(application.IssuedOn!.Value.UtcDateTime));

            if (application.InspectionRequired)
            {
                Assert.Contains(application.Inspections, inspection => inspection.Status == InspectionStatus.Passed);
            }
        });
    }

    [Fact]
    public async Task The_sample_data_names_no_real_person_and_holds_no_email_address()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await SeedAsync(database);

        await using CivicPermitsDbContext context = database.CreateContext();

        Assert.Equal(0, await context.Users.CountAsync(TestContext.Current.CancellationToken));

        List<string> text = [];
        text.AddRange(await context.PermitTypes.Select(t => t.Name + " " + t.Description).ToListAsync(TestContext.Current.CancellationToken));

        foreach (PermitApplication application in await LoadApplicationsAsync(database))
        {
            text.Add(application.ProjectDescription);
            text.Add(application.SiteAddress.ToString());
            text.AddRange(application.History.Select(change => change.Note ?? string.Empty));
            text.AddRange(application.Inspections.Select(inspection => inspection.Notes ?? string.Empty));
        }

        Assert.All(text, line => Assert.DoesNotContain("@", line, StringComparison.Ordinal));
        Assert.All(
            await LoadApplicationsAsync(database),
            application => Assert.Equal("Bramblewick, TG", $"{application.SiteAddress.City}, {application.SiteAddress.Region}"));
    }

    [Fact]
    public async Task An_issued_application_comes_back_from_the_database_exactly_as_it_was_saved()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await SeedAsync(database);

        PermitApplication truck = (await LoadApplicationsAsync(database))
            .Single(a => a.ProjectDescription.StartsWith("Sell wood-fired", StringComparison.Ordinal));

        Assert.Equal(ApplicationStatus.Issued, truck.Status);
        Assert.Equal(Money.FromDollars(325m), truck.FeeAssessed);
        Assert.Equal(Money.Zero, truck.DeclaredValuation);
        Assert.Equal("3 Market Square, Bramblewick, TG 70001", truck.SiteAddress.ToString());
        Assert.Matches(@"^CP-2026-\d{6}$", truck.Number.ToString());
        Assert.Equal(TimeSpan.Zero, truck.CreatedOn.Offset);
        Assert.Equal(SqliteTestDatabase.SeedTime.AddDays(-60), truck.CreatedOn);
        Assert.Equal(4, truck.History.Count);
        Assert.Single(truck.Inspections);
        Assert.Equal(InspectionStatus.Passed, truck.Inspections[0].Status);
        Assert.Equal(SampleIdentities.InspectorTwo, truck.Inspections[0].InspectorId);
        Assert.Equal(new DateOnly(2027, 9, 16), truck.ExpiresOn);
    }

    [Fact]
    public async Task Sorting_and_ranges_on_moments_and_money_are_done_by_the_database_and_agree_with_the_domain()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await SeedAsync(database);

        await using CivicPermitsDbContext context = database.CreateContext();
        List<PermitApplication> inMemory = await LoadApplicationsAsync(database);

        List<Guid> byFee = await context.PermitApplications.OrderBy(a => a.FeeAssessed).ThenBy(a => a.Id).Select(a => a.Id).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(inMemory.OrderBy(a => a.FeeAssessed.Cents).ThenBy(a => a.Id).Select(a => a.Id), byFee);

        DateTimeOffset cutoff = SqliteTestDatabase.SeedTime.AddDays(-20);
        List<Guid> recent = await context.PermitApplications.Where(a => a.CreatedOn > cutoff).OrderBy(a => a.CreatedOn).Select(a => a.Id).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(inMemory.Where(a => a.CreatedOn > cutoff).OrderBy(a => a.CreatedOn).Select(a => a.Id), recent);
        Assert.NotEmpty(recent);
    }

    [Fact]
    public async Task Two_applications_cannot_share_a_number_but_any_number_of_drafts_have_none()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await SeedAsync(database);

        await using CivicPermitsDbContext context = database.CreateContext();
        PermitType type = await context.PermitTypes.SingleAsync(t => t.Id == SampleData.Fence, TestContext.Current.CancellationToken);
        PermitNumber taken = (await context.PermitApplications.Where(a => a.Number != null).Select(a => a.Number).FirstAsync(TestContext.Current.CancellationToken))!.Value;

        context.PermitApplications.AddRange(NewDraft(type), NewDraft(type));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        PermitApplication clash = NewDraft(type);
        clash.Submit(new Actor(SampleIdentities.ApplicantOne, ActorRole.Applicant), type, taken, SqliteTestDatabase.SeedTime);
        context.PermitApplications.Add(clash);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_permit_type_that_has_applications_cannot_be_deleted()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await SeedAsync(database);

        await using CivicPermitsDbContext context = database.CreateContext();
        context.PermitTypes.Remove(await context.PermitTypes.SingleAsync(t => t.Id == SampleData.Residential, TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deleting_an_application_deletes_its_history_and_inspections()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await SeedAsync(database);

        await using (CivicPermitsDbContext context = database.CreateContext())
        {
            PermitApplication deck = await context.PermitApplications
                .SingleAsync(a => EF.Functions.Like(a.ProjectDescription, "Build a raised timber deck%"), TestContext.Current.CancellationToken);
            context.PermitApplications.Remove(deck);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using CivicPermitsDbContext check = database.CreateContext();
        Assert.Equal(11, await check.PermitApplications.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(await check.PermitApplications.SelectMany(a => a.History).CountAsync(TestContext.Current.CancellationToken), await check.Set<StatusChange>().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(await check.PermitApplications.SelectMany(a => a.Inspections).CountAsync(TestContext.Current.CancellationToken), await check.Set<Inspection>().CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Changes_made_through_the_domain_are_saved_on_an_application_loaded_from_the_database()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await SeedAsync(database);

        await using (CivicPermitsDbContext context = database.CreateContext())
        {
            PermitApplication stall = await context.PermitApplications
                .Include(a => a.History)
                .Include(a => a.Inspections)
                .SingleAsync(a => EF.Functions.Like(a.ProjectDescription, "A produce stall%"), TestContext.Current.CancellationToken);
            PermitType type = await context.PermitTypes.SingleAsync(t => t.Id == stall.PermitTypeId, TestContext.Current.CancellationToken);

            stall.Issue(new Actor(SampleIdentities.Clerk, ActorRole.Clerk), type, SqliteTestDatabase.SeedTime);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using CivicPermitsDbContext check = database.CreateContext();
        PermitApplication saved = await check.PermitApplications.Include(a => a.History)
            .SingleAsync(a => EF.Functions.Like(a.ProjectDescription, "A produce stall%"), TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationStatus.Issued, saved.Status);
        Assert.Equal(SqliteTestDatabase.SeedTime, saved.IssuedOn);
        Assert.Equal(ApplicationAction.Issue, saved.History.OrderBy(change => change.At).Last().Action);
    }

    private static PermitApplication NewDraft(PermitType type) =>
        PermitApplication.CreateDraft(
            Guid.NewGuid(),
            type,
            SampleIdentities.ApplicantOne,
            "Replace the side fence between two neighbouring gardens.",
            PostalAddress.Create("14 Lantern Row", null, "Bramblewick", "TG", "70001"),
            Money.Zero,
            SqliteTestDatabase.SeedTime);
}

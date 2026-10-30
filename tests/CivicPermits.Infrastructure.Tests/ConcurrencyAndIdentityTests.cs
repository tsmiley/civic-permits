using CivicPermits.Application.Abstractions;
using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Permits;
using CivicPermits.Infrastructure.Identity;
using CivicPermits.Infrastructure.Persistence;
using CivicPermits.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CivicPermits.Infrastructure.Tests;

public class ConcurrencyAndIdentityTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static readonly Actor Clerk = new(SampleIdentities.Clerk, ActorRole.Clerk);

    private static async Task<SqliteTestDatabase> SeededAsync()
    {
        SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using CivicPermitsDbContext context = database.CreateContext();
        await new SampleDataSeeder(context, database.Time).SeedAsync(Cancel);

        return database;
    }

    private static Task<PermitApplication> LoadAsync(CivicPermitsDbContext context, string startsWith) =>
        context.PermitApplications
            .Include(application => application.History)
            .Include(application => application.Inspections)
            .SingleAsync(application => EF.Functions.Like(application.ProjectDescription, startsWith + "%"), Cancel);

    [Fact]
    public async Task Seeding_moves_the_number_counter_to_the_highest_sample_number_so_the_next_one_is_free()
    {
        using SqliteTestDatabase database = await SeededAsync();
        await using CivicPermitsDbContext context = database.CreateContext();

        PermitNumber next = await new PermitNumberAllocator(context).NextAsync(SqliteTestDatabase.SeedTime.Year, Cancel);

        // Eleven of the twelve sample applications were submitted; one is a draft.
        Assert.Equal(12, next.Sequence);
        Assert.Equal(SqliteTestDatabase.SeedTime.Year, next.Year);
    }

    [Fact]
    public async Task Reseeding_never_moves_the_counter_back()
    {
        using SqliteTestDatabase database = await SeededAsync();

        await using (CivicPermitsDbContext context = database.CreateContext())
        {
            PermitNumberAllocator allocator = new(context);
            await allocator.NextAsync(SqliteTestDatabase.SeedTime.Year, Cancel);
            await new UnitOfWork(context).SaveChangesAsync(Cancel);
            await new SampleDataSeeder(context, database.Time).SeedAsync(Cancel);
        }

        await using CivicPermitsDbContext check = database.CreateContext();
        Assert.Equal(13, (await new PermitNumberAllocator(check).NextAsync(SqliteTestDatabase.SeedTime.Year, Cancel)).Sequence);
    }

    [Fact]
    public async Task Two_requests_cannot_be_given_the_same_number()
    {
        using SqliteTestDatabase database = await SeededAsync();
        await using CivicPermitsDbContext first = database.CreateContext();
        await using CivicPermitsDbContext second = database.CreateContext();
        int year = SqliteTestDatabase.SeedTime.Year;

        PermitNumber one = await new PermitNumberAllocator(first).NextAsync(year, Cancel);
        PermitNumber two = await new PermitNumberAllocator(second).NextAsync(year, Cancel);
        Assert.Equal(one, two);

        await new UnitOfWork(first).SaveChangesAsync(Cancel);
        RequestException lost = await Assert.ThrowsAsync<RequestException>(() => new UnitOfWork(second).SaveChangesAsync(Cancel));

        Assert.Equal(RequestFailure.Conflict, lost.Failure);
        Assert.Equal("request.concurrent_change", lost.Code);
    }

    [Fact]
    public async Task The_first_number_of_a_new_year_starts_at_one_and_two_first_claims_cannot_both_win()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using CivicPermitsDbContext first = database.CreateContext();
        await using CivicPermitsDbContext second = database.CreateContext();

        Assert.Equal(1, (await new PermitNumberAllocator(first).NextAsync(2031, Cancel)).Sequence);
        Assert.Equal(1, (await new PermitNumberAllocator(second).NextAsync(2031, Cancel)).Sequence);

        await new UnitOfWork(first).SaveChangesAsync(Cancel);
        RequestException lost = await Assert.ThrowsAsync<RequestException>(() => new UnitOfWork(second).SaveChangesAsync(Cancel));

        Assert.Equal("request.duplicate", lost.Code);
    }

    [Fact]
    public async Task Two_people_changing_the_same_application_cannot_both_succeed()
    {
        using SqliteTestDatabase database = await SeededAsync();
        await using CivicPermitsDbContext first = database.CreateContext();
        await using CivicPermitsDbContext second = database.CreateContext();

        PermitApplication seenByFirst = await LoadAsync(first, "Install a hanging blade sign");
        PermitApplication seenBySecond = await LoadAsync(second, "Install a hanging blade sign");
        Assert.Equal(ApplicationStatus.InReview, seenByFirst.Status);

        seenByFirst.Approve(Clerk, "Fine.", SqliteTestDatabase.SeedTime);
        await new UnitOfWork(first).SaveChangesAsync(Cancel);

        seenBySecond.Deny(Clerk, "The sign is too large for the frontage.", SqliteTestDatabase.SeedTime);
        RequestException lost = await Assert.ThrowsAsync<RequestException>(() => new UnitOfWork(second).SaveChangesAsync(Cancel));
        Assert.Equal("request.concurrent_change", lost.Code);

        await using CivicPermitsDbContext check = database.CreateContext();
        Assert.Equal(ApplicationStatus.Approved, (await LoadAsync(check, "Install a hanging blade sign")).Status);
    }

    [Fact]
    public async Task A_change_that_only_adds_a_child_still_counts_as_a_change_to_the_application()
    {
        using SqliteTestDatabase database = await SeededAsync();
        await using CivicPermitsDbContext first = database.CreateContext();
        await using CivicPermitsDbContext second = database.CreateContext();

        PermitApplication seenByFirst = await LoadAsync(first, "Build a raised timber deck");
        PermitApplication seenBySecond = await LoadAsync(second, "Build a raised timber deck");
        long before = first.Entry(seenByFirst).Property<long>("Version").CurrentValue;

        // A re-inspection is booked: the only database change is a new inspection row.
        seenByFirst.ScheduleInspection(Clerk, SampleIdentities.InspectorOne, SqliteTestDatabase.SeedTime.AddDays(7), SqliteTestDatabase.SeedTime);
        await new UnitOfWork(first).SaveChangesAsync(Cancel);

        Assert.Equal(before + 1, first.Entry(seenByFirst).Property<long>("Version").CurrentValue);

        // The second person, who loaded the application before that, can no longer act on a stale copy.
        seenBySecond.ScheduleInspection(Clerk, SampleIdentities.InspectorTwo, SqliteTestDatabase.SeedTime.AddDays(8), SqliteTestDatabase.SeedTime);
        await Assert.ThrowsAsync<RequestException>(() => new UnitOfWork(second).SaveChangesAsync(Cancel));

        await using CivicPermitsDbContext check = database.CreateContext();
        Assert.Equal(2, (await LoadAsync(check, "Build a raised timber deck")).Inspections.Count);
    }

    [Fact]
    public async Task The_version_starts_at_zero_and_rises_by_one_for_each_saved_change()
    {
        using SqliteTestDatabase database = await SeededAsync();
        await using CivicPermitsDbContext context = database.CreateContext();
        PermitApplication application = await LoadAsync(context, "A produce stall");
        Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<PermitApplication, long> version = context.Entry(application).Property<long>("Version");
        long start = version.CurrentValue;

        application.Issue(Clerk, (await context.PermitTypes.SingleAsync(type => type.Id == application.PermitTypeId, Cancel)), SqliteTestDatabase.SeedTime);
        await new UnitOfWork(context).SaveChangesAsync(Cancel);

        Assert.Equal(start + 1, version.CurrentValue);
        Assert.Equal(0, start);
    }

    [Fact]
    public async Task The_identity_bootstrapper_creates_the_roles_and_sample_people_once()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        ServiceCollection services = [];
        services.AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = $"Data Source={database.FilePath}",
        }).Build());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IdentityBootstrapper bootstrapper = scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        RoleManager<IdentityRole<Guid>> roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        await bootstrapper.EnsureRolesAsync();
        await bootstrapper.EnsureRolesAsync();
        int first = await bootstrapper.EnsureSampleUsersAsync($"Aa1-{Guid.NewGuid():N}");
        int second = await bootstrapper.EnsureSampleUsersAsync($"Aa1-{Guid.NewGuid():N}");

        Assert.Equal(4, roles.Roles.Count());
        Assert.Equal(7, first);
        Assert.Equal(0, second);

        ApplicationUser clerk = (await users.FindByIdAsync(SampleIdentities.Clerk.ToString()))!;
        Assert.True(await users.IsInRoleAsync(clerk, "Clerk"));
        Assert.False(await users.IsInRoleAsync(clerk, "Administrator"));
        Assert.EndsWith(".test", clerk.Email, StringComparison.Ordinal);
        Assert.True(await scope.ServiceProvider.GetRequiredService<IUserDirectory>().IsInspectorAsync(SampleIdentities.InspectorOne, Cancel));
        Assert.False(await scope.ServiceProvider.GetRequiredService<IUserDirectory>().IsInspectorAsync(SampleIdentities.ApplicantOne, Cancel));
        Assert.False(await scope.ServiceProvider.GetRequiredService<IUserDirectory>().IsInspectorAsync(Guid.NewGuid(), Cancel));
    }

    [Fact]
    public async Task Registering_when_the_roles_were_never_created_fails_loudly_and_leaves_no_account_behind()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        ServiceCollection services = [];
        services.AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = $"Data Source={database.FilePath}",
        }).Build());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        RegistrationService registration = scope.ServiceProvider.GetRequiredService<RegistrationService>();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // No EnsureRolesAsync: the database has migrations but no roles.
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registration.RegisterApplicantAsync("someone@bramblewick.test", $"Aa1-{Guid.NewGuid():N}", "Someone"));

        Assert.Contains("Applicant", failure.Message, StringComparison.Ordinal);
        Assert.Null(await users.FindByEmailAsync("someone@bramblewick.test"));

        // With the roles in place the same request succeeds.
        await scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().EnsureRolesAsync();
        RegistrationOutcome outcome = await registration.RegisterApplicantAsync("someone@bramblewick.test", $"Aa1-{Guid.NewGuid():N}", "Someone");

        Assert.True(outcome.Accepted);
        Assert.True(await users.IsInRoleAsync((await users.FindByEmailAsync("someone@bramblewick.test"))!, "Applicant"));
    }

    [Fact]
    public async Task Registering_when_giving_the_role_is_refused_fails_loudly_and_leaves_no_account_behind()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        ServiceCollection services = [];
        services.AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = $"Data Source={database.FilePath}",
        }).Build());
        services.AddScoped<UserManager<ApplicationUser>, RoleRefusingUserManager>(); // registered last, so it replaces Identity's
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().EnsureRolesAsync();
        RegistrationService registration = scope.ServiceProvider.GetRequiredService<RegistrationService>();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // The role exists, and the call returns a failed result (it does not throw), which is the other way it can go wrong.
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registration.RegisterApplicantAsync("someone@bramblewick.test", $"Aa1-{Guid.NewGuid():N}", "Someone"));

        Assert.Contains("Applicant", failure.Message, StringComparison.Ordinal);
        Assert.Contains("RoleRefusedByTest", failure.Message, StringComparison.Ordinal);
        Assert.Null(await users.FindByEmailAsync("someone@bramblewick.test"));
    }

    /// <summary>An Identity user manager that refuses to give anyone a role, by returning a failed result the way Identity can.</summary>
    private sealed class RoleRefusingUserManager(
        IUserStore<ApplicationUser> store,
        Microsoft.Extensions.Options.IOptions<IdentityOptions> options,
        IPasswordHasher<ApplicationUser> hasher,
        IEnumerable<IUserValidator<ApplicationUser>> userValidators,
        IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
        ILookupNormalizer normalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        Microsoft.Extensions.Logging.ILogger<UserManager<ApplicationUser>> logger)
        : UserManager<ApplicationUser>(store, options, hasher, userValidators, passwordValidators, normalizer, errors, services, logger)
    {
        public override Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role) =>
            Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "RoleRefusedByTest", Description = "Refused for the test." }));
    }

    [Fact]
    public async Task A_sample_password_that_is_too_short_stops_the_host_from_starting()
    {
        ServiceCollection services = [];
        services.AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = "Data Source=:memory:",
            ["Database:MigrateOnStartup"] = "true",
            ["Database:SeedSampleData"] = "true",
            ["Database:SampleUserPassword"] = "short",
        }).Build());
        await using ServiceProvider provider = services.BuildServiceProvider();

        Microsoft.Extensions.Options.OptionsValidationException failure = Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(
            () => provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value);

        Assert.Contains("at least 12", failure.Message, StringComparison.Ordinal);
    }
}

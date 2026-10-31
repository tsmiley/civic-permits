using CivicPermits.Application.Abstractions;
using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;
using CivicPermits.Domain.Permits;
using CivicPermits.Infrastructure.Identity;
using CivicPermits.Infrastructure.Persistence;
using CivicPermits.Shared.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CivicPermits.Infrastructure.Tests;

/// <summary>The retention purge, each class at its period to the day, and what the purge leaves alone.</summary>
public class RetentionTests
{
    private static readonly DateTimeOffset T = new(2019, 5, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Administrator = Guid.Parse("d0000000-0000-4000-8000-000000000001");
    private static readonly Guid Clerk = Guid.Parse("c0000000-0000-4000-8000-000000000001");
    private static readonly Actor ClerkActor = new(Clerk, ActorRole.Clerk);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ServiceProvider BuildProvider(SqliteTestDatabase database)
    {
        ServiceCollection services = [];
        services.AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = $"Data Source={database.FilePath}",
        }).Build());

        return services.BuildServiceProvider();
    }

    private static PermitType Type() =>
        PermitType.Create(Guid.Parse("b0000000-0000-4000-8000-000000000001"), PermitCategory.Building, "Residential building permit", "New construction.",
            Money.FromDollars(150m), 125, requiresInspection: false, validityDays: 365, targetReviewDays: 10);

    private static PermitApplication Application(PermitType type, Guid applicant, DateTimeOffset createdOn) =>
        PermitApplication.CreateDraft(Guid.CreateVersion7(), type, applicant, "Add a covered porch to the rear of the house.",
            PostalAddress.Create("100 Lantern Row", null, "Bramblewick", "TG", "70001"), Money.FromDollars(24_000m), createdOn);

    private static PermitApplication Submitted(PermitType type, Guid applicant, int number, DateTimeOffset at)
    {
        PermitApplication application = Application(type, applicant, at);
        application.Submit(new Actor(applicant, ActorRole.Applicant), type, PermitNumber.From(at.Year, number), at);

        return application;
    }

    /// <summary>One application of each class, all last changed at <see cref="T"/>, plus one still in progress that is far older.</summary>
    private static async Task SeedApplicationsAsync(CivicPermitsDbContext context, Guid applicant)
    {
        PermitType type = Type();
        context.PermitTypes.Add(type);

        PermitApplication decided = Submitted(type, applicant, 1, T.AddDays(-3));
        decided.StartReview(ClerkActor, T.AddDays(-2));
        decided.Approve(ClerkActor, "Fine.", T);

        PermitApplication draft = Application(type, applicant, T);

        PermitApplication withdrawn = Submitted(type, applicant, 2, T.AddDays(-5));
        withdrawn.Withdraw(new Actor(applicant, ActorRole.Applicant), "Changed my mind.", T);

        PermitApplication inProgress = Submitted(type, applicant, 3, T.AddYears(-10));

        context.PermitApplications.AddRange(decided, draft, withdrawn, inProgress);
        await context.SaveChangesAsync(Token);
    }

    private static async Task<RetentionResponse> RunAtAsync(ServiceProvider provider, DateTimeOffset now, bool dryRun = false)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IRetentionService>().RunAsync(Administrator, now, dryRun, Token);
    }

    private static async Task<int> CountAsync(CivicPermitsDbContext context) => await context.PermitApplications.CountAsync(Token);

    [Fact]
    public async Task Each_class_is_removed_at_its_period_and_not_a_day_before_and_its_history_goes_with_it()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using ServiceProvider provider = BuildProvider(database);
        await using (CivicPermitsDbContext seed = database.CreateContext())
        {
            await SeedApplicationsAsync(seed, Guid.NewGuid());
        }

        // A day before a year: nothing goes.
        Assert.Equal(new RetentionResponse(false, 0, 0, 0, 0), await RunAtAsync(provider, T.AddYears(1).AddDays(-1)));
        await using (CivicPermitsDbContext check = database.CreateContext())
        {
            Assert.Equal(4, await CountAsync(check));
        }

        // A year to the instant: the draft and the withdrawn application go; the decided one stays.
        Assert.Equal(new RetentionResponse(false, 0, 1, 1, 0), await RunAtAsync(provider, T.AddYears(1)));
        await using (CivicPermitsDbContext check = database.CreateContext())
        {
            Assert.Equal(2, await CountAsync(check));
            Assert.Equal(["Approved", "Submitted"], (await check.PermitApplications.Select(a => a.Status.ToString()).ToListAsync(Token)).Order());
        }

        // A day before seven years: the decided one stays.
        Assert.Equal(new RetentionResponse(false, 0, 0, 0, 0), await RunAtAsync(provider, T.AddYears(7).AddDays(-1)));

        // Seven years to the instant: it goes. The application in progress never does, however old.
        Assert.Equal(new RetentionResponse(false, 1, 0, 0, 0), await RunAtAsync(provider, T.AddYears(7)));
        Assert.Equal(new RetentionResponse(false, 0, 0, 0, 0), await RunAtAsync(provider, T.AddYears(50)));

        await using CivicPermitsDbContext after = database.CreateContext();
        Assert.Equal(["Submitted"], (await after.PermitApplications.Select(a => a.Status.ToString()).ToListAsync(Token)));

        // The children of the removed applications went with them: only the one still in progress has history left.
        Assert.Equal(1, await after.Set<StatusChange>().Select(change => EF.Property<Guid>(change, "PermitApplicationId")).Distinct().CountAsync(Token));
    }

    [Fact]
    public async Task A_dry_run_counts_what_a_real_run_would_remove_and_removes_and_records_nothing()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using ServiceProvider provider = BuildProvider(database);
        await using (CivicPermitsDbContext seed = database.CreateContext())
        {
            await SeedApplicationsAsync(seed, Guid.NewGuid());
        }

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IRetentionService retention = scope.ServiceProvider.GetRequiredService<IRetentionService>();

        RetentionResponse dry = await retention.RunAsync(Administrator, T.AddYears(8), dryRun: true, Token);

        Assert.Equal(new RetentionResponse(true, 1, 1, 1, 0), dry);
        await using CivicPermitsDbContext check = database.CreateContext();
        Assert.Equal(4, await CountAsync(check));
        Assert.Equal(0, await check.AuditEntries.CountAsync(Token));
    }

    [Fact]
    public async Task A_real_run_writes_one_audit_entry_with_the_counts_in_the_same_transaction()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using ServiceProvider provider = BuildProvider(database);
        await using (CivicPermitsDbContext seed = database.CreateContext())
        {
            await SeedApplicationsAsync(seed, Guid.NewGuid());
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRetentionService>().RunAsync(Administrator, T.AddYears(8), dryRun: false, Token);
        }

        await using CivicPermitsDbContext check = database.CreateContext();
        AuditEntry entry = await check.AuditEntries.SingleAsync(Token);

        Assert.Equal("retention.purged", entry.Action);
        Assert.Equal("Retention", entry.SubjectType);
        Assert.Null(entry.SubjectId);
        Assert.Equal(Administrator, entry.ActorId);
        Assert.Equal(T.AddYears(8), entry.OccurredOn);
        Assert.Contains("1 decided", entry.Summary, StringComparison.Ordinal);
        Assert.Contains("1 draft", entry.Summary, StringComparison.Ordinal);
        Assert.Contains("1 withdrawn", entry.Summary, StringComparison.Ordinal);
        Assert.Contains("0 unused", entry.Summary, StringComparison.Ordinal);
    }

    // ---- Accounts ----

    private static async Task<ApplicationUser> AddAccountAsync(AsyncServiceScope scope, string email, ActorRole role, DateTimeOffset? createdOn, DateTimeOffset? lastSignInOn)
    {
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser person = new() { Id = Guid.CreateVersion7(), UserName = email, Email = email, DisplayName = email[..email.IndexOf('@', StringComparison.Ordinal)], CreatedOn = createdOn, LastSignInOn = lastSignInOn };

        Assert.True((await users.CreateAsync(person, $"Aa1-{Guid.NewGuid():N}")).Succeeded);
        Assert.True((await users.AddToRoleAsync(person, role.ToString())).Succeeded);

        return person;
    }

    [Fact]
    public async Task An_applicant_account_with_no_application_is_removed_two_years_after_it_was_last_used_and_no_day_before()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using ServiceProvider provider = BuildProvider(database);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().EnsureRolesAsync();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IRetentionService retention = scope.ServiceProvider.GetRequiredService<IRetentionService>();

        // Used long ago, but it signed in at T; made long ago and never signed in; a clerk; an applicant with an application.
        ApplicationUser lapsed = await AddAccountAsync(scope, "lapsed@bramblewick.test", ActorRole.Applicant, T.AddYears(-5), lastSignInOn: T);
        ApplicationUser neverSignedIn = await AddAccountAsync(scope, "never@bramblewick.test", ActorRole.Applicant, createdOn: T, lastSignInOn: null);
        ApplicationUser staff = await AddAccountAsync(scope, "staff@bramblewick.test", ActorRole.Clerk, T.AddYears(-5), T.AddYears(-5));
        ApplicationUser withApplication = await AddAccountAsync(scope, "has.application@bramblewick.test", ActorRole.Applicant, T.AddYears(-5), T.AddYears(-5));

        await using (CivicPermitsDbContext seed = database.CreateContext())
        {
            PermitType type = Type();
            seed.PermitTypes.Add(type);
            seed.PermitApplications.Add(Submitted(type, withApplication.Id, 9, T.AddYears(-1))); // in progress: never removed by age
            await seed.SaveChangesAsync(Token);
        }

        DateTimeOffset end = T.AddYears(2);

        Assert.Equal(0, (await retention.RunAsync(Administrator, end.AddDays(-1), dryRun: false, Token)).Accounts);
        Assert.NotNull(await users.FindByIdAsync(lapsed.Id.ToString()));

        RetentionResponse result = await retention.RunAsync(Administrator, end, dryRun: false, Token);

        Assert.Equal(2, result.Accounts);
        Assert.Null(await users.FindByIdAsync(lapsed.Id.ToString()));
        Assert.Null(await users.FindByIdAsync(neverSignedIn.Id.ToString()));
        Assert.NotNull(await users.FindByIdAsync(staff.Id.ToString())); // staff accounts are not purged by this rule
        Assert.NotNull(await users.FindByIdAsync(withApplication.Id.ToString())); // it has an application
    }

    [Fact]
    public async Task An_account_from_before_dates_were_recorded_starts_its_clock_at_the_first_real_run_instead_of_being_removed()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using ServiceProvider provider = BuildProvider(database);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().EnsureRolesAsync();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IRetentionService retention = scope.ServiceProvider.GetRequiredService<IRetentionService>();
        ApplicationUser old = await AddAccountAsync(scope, "old@bramblewick.test", ActorRole.Applicant, createdOn: null, lastSignInOn: null);
        DateTimeOffset first = T.AddYears(9);

        Assert.Equal(0, (await retention.RunAsync(Administrator, first, dryRun: true, Token)).Accounts);
        Assert.Null((await users.FindByIdAsync(old.Id.ToString()))!.CreatedOn); // a dry run changes nothing

        Assert.Equal(0, (await retention.RunAsync(Administrator, first, dryRun: false, Token)).Accounts);
        Assert.Equal(first, (await users.FindByIdAsync(old.Id.ToString()))!.CreatedOn);

        Assert.Equal(0, (await retention.RunAsync(Administrator, first.AddYears(2).AddDays(-1), dryRun: false, Token)).Accounts);
        Assert.Equal(1, (await retention.RunAsync(Administrator, first.AddYears(2), dryRun: false, Token)).Accounts);
    }

    [Fact]
    public async Task An_account_whose_applications_are_all_removed_in_a_run_is_left_for_its_own_period()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using ServiceProvider provider = BuildProvider(database);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().EnsureRolesAsync();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser person = await AddAccountAsync(scope, "kept@bramblewick.test", ActorRole.Applicant, T.AddYears(-1), T.AddYears(-1));

        await using (CivicPermitsDbContext seed = database.CreateContext())
        {
            PermitType type = Type();
            seed.PermitTypes.Add(type);
            seed.PermitApplications.Add(Application(type, person.Id, T));
            await seed.SaveChangesAsync(Token);
        }

        RetentionResponse result = await scope.ServiceProvider.GetRequiredService<IRetentionService>().RunAsync(Administrator, T.AddYears(9), dryRun: false, Token);

        Assert.Equal(new RetentionResponse(false, 0, 1, 0, 0), result);
        Assert.NotNull(await users.FindByIdAsync(person.Id.ToString()));
    }

    // ---- Closing an account ----

    [Fact]
    public async Task Closing_an_account_removes_the_name_and_email_keeps_the_applications_and_the_account_can_never_sign_in()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using ServiceProvider provider = BuildProvider(database);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().EnsureRolesAsync();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        SignInManager<ApplicationUser> signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        AccountClosing closing = scope.ServiceProvider.GetRequiredService<AccountClosing>();
        string password = $"vkxjqpzmwnbtyhd-{Guid.NewGuid():N}";
        ApplicationUser person = new() { Id = Guid.CreateVersion7(), UserName = "closing@bramblewick.test", Email = "closing@bramblewick.test", DisplayName = "Closing Person" };
        Assert.True((await users.CreateAsync(person, password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(person, nameof(ActorRole.Applicant))).Succeeded);
        string stampBefore = await users.GetSecurityStampAsync(person);

        await using (CivicPermitsDbContext seed = database.CreateContext())
        {
            PermitType type = Type();
            seed.PermitTypes.Add(type);
            seed.PermitApplications.Add(Submitted(type, person.Id, 5, T));
            await seed.SaveChangesAsync(Token);
        }

        ClosingOutcome outcome = await closing.CloseApplicantAsync(person.Id, password);

        Assert.True(outcome.Closed);
        ApplicationUser closed = (await users.FindByIdAsync(person.Id.ToString()))!;
        Assert.Equal(string.Empty, closed.DisplayName);
        Assert.DoesNotContain("closing", closed.UserName!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("closing@", closed.Email!, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("@closed.invalid", closed.Email!, StringComparison.Ordinal);
        Assert.Null(closed.PasswordHash);
        Assert.NotEqual(stampBefore, closed.SecurityStamp);
        Assert.Null(await users.FindByEmailAsync("closing@bramblewick.test"));
        Assert.Null(await users.FindByNameAsync("closing@bramblewick.test"));

        // It can never sign in: not with the old password, not with any, and not after a lockout would have lifted.
        Assert.False((await signIn.CheckPasswordSignInAsync(closed, password, lockoutOnFailure: false)).Succeeded);
        Assert.True(await users.IsLockedOutAsync(closed));
        Assert.Equal(DateTimeOffset.MaxValue, closed.LockoutEnd);

        // The applications are kept, still pointing at the closed account.
        await using CivicPermitsDbContext after = database.CreateContext();
        PermitApplication kept = await after.PermitApplications.SingleAsync(Token);
        Assert.Equal(person.Id, kept.ApplicantId);

        // One audit entry, by that account, with no name or address in it.
        AuditEntry entry = await after.AuditEntries.SingleAsync(Token);
        Assert.Equal("account.closed", entry.Action);
        Assert.Equal(person.Id, entry.ActorId);
        Assert.Equal(person.Id, entry.SubjectId);
        Assert.DoesNotContain("Closing", entry.Summary, StringComparison.Ordinal);

        // The address can be used again.
        RegistrationOutcome again = await scope.ServiceProvider.GetRequiredService<RegistrationService>()
            .RegisterApplicantAsync("closing@bramblewick.test", $"vkxjqpzmwnbtyhd-{Guid.NewGuid():N}", "Closing Person");
        Assert.True(again.Accepted);
        Assert.NotNull(await users.FindByEmailAsync("closing@bramblewick.test"));
    }

    [Fact]
    public async Task A_wrong_password_closes_nothing_and_counts_toward_the_lockout_and_nobody_can_close_someone_elses()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using ServiceProvider provider = BuildProvider(database);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().EnsureRolesAsync();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        AccountClosing closing = scope.ServiceProvider.GetRequiredService<AccountClosing>();
        string passwordA = $"vkxjqpzmwnbtyhd-{Guid.NewGuid():N}";
        string passwordB = $"qvjmzxpwknbhtyd-{Guid.NewGuid():N}";
        ApplicationUser a = new() { Id = Guid.CreateVersion7(), UserName = "a@bramblewick.test", Email = "a@bramblewick.test", DisplayName = "A Person" };
        ApplicationUser b = new() { Id = Guid.CreateVersion7(), UserName = "b@bramblewick.test", Email = "b@bramblewick.test", DisplayName = "B Person" };
        ApplicationUser staff = await AddAccountAsync(scope, "clerk@bramblewick.test", ActorRole.Clerk, T, T);
        Assert.True((await users.CreateAsync(a, passwordA)).Succeeded);
        Assert.True((await users.AddToRoleAsync(a, nameof(ActorRole.Applicant))).Succeeded);
        Assert.True((await users.CreateAsync(b, passwordB)).Succeeded);
        Assert.True((await users.AddToRoleAsync(b, nameof(ActorRole.Applicant))).Succeeded);

        // B, signed in as B, cannot close A's account: the call acts on the signed-in account, and A's password is not B's.
        Assert.False((await closing.CloseApplicantAsync(b.Id, passwordA)).Closed);
        Assert.NotNull(await users.FindByEmailAsync("a@bramblewick.test"));
        Assert.NotNull(await users.FindByEmailAsync("b@bramblewick.test"));

        // Staff cannot close an account through this path, even with their own password.
        Assert.False((await closing.CloseApplicantAsync(staff.Id, "anything")).Closed);

        // Five wrong passwords lock the account, as at sign-in.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Assert.False((await closing.CloseApplicantAsync(a.Id, "not-the-password-at-all")).Closed);
        }

        Assert.False((await closing.CloseApplicantAsync(a.Id, passwordA)).Closed); // now locked, so even the right password is refused
        Assert.NotNull(await users.FindByEmailAsync("a@bramblewick.test"));
        Assert.Empty(await database.CreateContext().AuditEntries.ToListAsync(Token));
    }
}

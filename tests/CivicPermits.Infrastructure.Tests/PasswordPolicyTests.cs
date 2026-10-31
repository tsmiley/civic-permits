using System.Buffers.Binary;
using CivicPermits.Infrastructure.Identity;
using CivicPermits.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CivicPermits.Infrastructure.Tests;

/// <summary>NIST SP 800-63B-4 as the project's security baseline adopts it: length and a blocklist, no composition rules, a strong hash.</summary>
public class PasswordPolicyTests
{
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

    // ---- The hash ----

    [Fact]
    public async Task A_new_accounts_stored_hash_records_the_iteration_count_the_policy_asks_for()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using ServiceProvider provider = BuildProvider(database);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().EnsureRolesAsync();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        RegistrationOutcome outcome = await scope.ServiceProvider.GetRequiredService<RegistrationService>()
            .RegisterApplicantAsync("someone@bramblewick.test", $"vkxjqpzmwnbtyhd-{Guid.NewGuid():N}", "Someone");
        ApplicationUser stored = (await users.FindByEmailAsync("someone@bramblewick.test"))!;

        // Identity's format: a marker byte (1 = PBKDF2 version 3), then the PRF, the iteration count and the salt size,
        // each four bytes in network order.
        byte[] hash = Convert.FromBase64String(stored.PasswordHash!);

        Assert.True(outcome.Accepted);
        Assert.Equal(1, hash[0]);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(1, 4))); // Identity's code for HMAC-SHA512 (0 is SHA1, 1 is SHA256)
        Assert.Equal((uint)PasswordPolicy.IterationCount, BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(5, 4)));
        Assert.True(PasswordPolicy.IterationCount >= 220_000, "The security baseline's floor is 220,000 iterations.");
    }

    [Fact]
    public async Task A_hash_made_with_fewer_iterations_is_replaced_at_the_next_successful_check()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using ServiceProvider provider = BuildProvider(database);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IPasswordHasher<ApplicationUser> hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        ApplicationUser person = new() { UserName = "someone@bramblewick.test" };
        string password = $"vkxjqpzmwnbtyhd-{Guid.NewGuid():N}";
        string old = new PasswordHasher<ApplicationUser>(Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions { IterationCount = 100_000 }))
            .HashPassword(person, password);

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, hasher.VerifyHashedPassword(person, old, password));
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(person, hasher.HashPassword(person, password), password));
    }

    // ---- Length, with no composition rules ----

    [Theory]
    [InlineData("vkxjqpzmwnbtyhd")] // fifteen lower-case letters: one kind of character, long enough
    [InlineData("VKXJQPZMWNBTYHD")] // fifteen capitals
    [InlineData("739185264073918")] // fifteen digits
    [InlineData("the rain in spain falls mainly on the plain")] // a sentence with spaces
    public async Task Fifteen_characters_of_a_single_kind_are_accepted(string password)
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using ServiceProvider provider = BuildProvider(database);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().EnsureRolesAsync();

        RegistrationOutcome outcome = await scope.ServiceProvider.GetRequiredService<RegistrationService>()
            .RegisterApplicantAsync("someone@bramblewick.test", password, "Someone");

        Assert.True(outcome.Accepted, string.Join(" ", outcome.Errors.SelectMany(pair => pair.Value)));
    }

    [Fact]
    public async Task Fourteen_characters_are_refused_and_fifteen_accepted_whatever_they_are_made_of()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using ServiceProvider provider = BuildProvider(database);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().EnsureRolesAsync();
        RegistrationService registration = scope.ServiceProvider.GetRequiredService<RegistrationService>();

        RegistrationOutcome shortOne = await registration.RegisterApplicantAsync("someone@bramblewick.test", "Vkxjqpzm-nbty1", "Someone");
        RegistrationOutcome longEnough = await registration.RegisterApplicantAsync("someone@bramblewick.test", "vkxjqpzmwnbtyhd", "Someone");

        Assert.False(shortOne.Accepted);
        Assert.Contains("15", string.Join(" ", shortOne.Errors["password"]), StringComparison.Ordinal);
        Assert.True(longEnough.Accepted);
    }

    [Fact]
    public async Task A_very_long_passphrase_is_accepted_and_an_absurd_one_is_not()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using ServiceProvider provider = BuildProvider(database);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().EnsureRolesAsync();
        RegistrationService registration = scope.ServiceProvider.GetRequiredService<RegistrationService>();
        string sixtyFourOrMore = string.Join(' ', Enumerable.Repeat("a long sentence nobody else would think of", 3)) + " 7";

        RegistrationOutcome accepted = await registration.RegisterApplicantAsync("someone@bramblewick.test", sixtyFourOrMore, "Someone");
        RegistrationOutcome absurd = await registration.RegisterApplicantAsync("another@bramblewick.test", new string('x', CommonPasswordValidator.MaximumLength) + "-q7", "Another");

        Assert.True(sixtyFourOrMore.Length >= 64);
        Assert.True(accepted.Accepted, string.Join(" ", accepted.Errors.SelectMany(pair => pair.Value)));
        Assert.False(absurd.Accepted);
    }

    // ---- The blocklist ----

    [Theory]
    [InlineData("password123456789")] // a stem and a tail of digits
    [InlineData("Password123456789!")] // other capitals and a symbol
    [InlineData("p@ssw0rd p@ssw0rd")] // look-alike swaps, a space, repeated
    [InlineData("passwordpassword")]
    [InlineData("correcthorsebatterystaple")] // a famous phrase
    [InlineData("Correct Horse Battery Staple")]
    [InlineData("iloveyou-iloveyou-1")]
    [InlineData("dragon monkey football 2026!")] // three stems and filler
    [InlineData("qwertyuiopasdfghjkl")] // a keyboard walk
    [InlineData("Qwerty-Qwerty-Qwerty-9")]
    [InlineData("111111111111111")] // one character repeated
    [InlineData("123456789012345")] // a counting run
    [InlineData("9876543210987654")] // counting down
    [InlineData("abcdefghijklmnopq")]
    [InlineData("CivicPermits2026!!")] // this application's own name
    [InlineData("1q2w3e4r5t6y7u8i")] // a walk down the keyboard, a digit then the letter below it (a famous breached password)
    [InlineData("zaq12wsxcde34rfv")] // a walk up the diagonals
    [InlineData("i8u7y6t5r4e3w2q1")] // the same shapes backwards
    [InlineData("vfr43edcxsw21qaz")]
    [InlineData("1q2w3e4r5t6y7u8i9o0p")]
    [InlineData("qazxswedcvfrtgbn")]
    [InlineData("1Q2W3E4R5T6Y7U8I")] // other capitals
    [InlineData("1q2w3e4r")] // eight keys is a walk by itself
    public void A_password_on_the_list_or_made_only_of_it_is_guessable(string password) =>
        Assert.True(CommonPasswordValidator.IsGuessable(password), $"'{password}' should have been refused.");

    [Theory]
    [InlineData("vkxjqpzmwnbtyhd")]
    [InlineData("739185264073918")]
    [InlineData("the rain in spain falls mainly on the plain")]
    [InlineData("dragonmonkeyfootballshadow")] // four stems: a passphrase of words is allowed to be common words
    [InlineData("purple tractor sings opera at midnight")]
    [InlineData("q1w2e3")] // too short to count as a walk
    [InlineData("1q2w3e4r then a long sentence about gardens and weather")] // a walk is a small part of it
    [InlineData("a1b2c3d4e5f6g7h8i9")] // letters and digits in order but not neighbouring keys
    public void A_password_that_is_not_made_only_of_the_list_is_not_guessable(string password) =>
        Assert.False(CommonPasswordValidator.IsGuessable(password), $"'{password}' should not have been refused.");

    [Theory]
    [InlineData("someone-and-friends-forever")] // the part of the address before the @, even among other words
    [InlineData("My Someone 2026 login")]
    [InlineData("s0me0ne-and-some-others")] // with look-alikes swapped
    public void A_password_that_contains_the_accounts_own_name_is_guessable(string password) =>
        Assert.True(CommonPasswordValidator.IsGuessable(password, new ApplicationUser { UserName = "someone@bramblewick.test" }), $"'{password}' should have been refused.");

    [Theory]
    [InlineData("password123456789")]
    [InlineData("correcthorsebatterystaple")]
    public async Task A_listed_password_is_refused_at_registration_with_a_reason_the_person_can_act_on(string password)
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using ServiceProvider provider = BuildProvider(database);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().EnsureRolesAsync();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        RegistrationOutcome outcome = await scope.ServiceProvider.GetRequiredService<RegistrationService>()
            .RegisterApplicantAsync("someone@bramblewick.test", password, "Someone");

        Assert.False(outcome.Accepted);
        Assert.Contains("first that people guess", string.Join(" ", outcome.Errors["password"]), StringComparison.Ordinal);
        Assert.Null(await users.FindByEmailAsync("someone@bramblewick.test"));
    }

    [Fact]
    public void The_list_is_only_lower_case_letters_and_no_stem_is_shorter_than_the_matcher_looks_for()
    {
        Assert.All(CommonPasswords.Stems, stem =>
        {
            Assert.True(stem.Length >= 4, $"'{stem}' is shorter than four letters.");
            Assert.True(stem.All(char.IsAsciiLetterLower), $"'{stem}' is not only a to z.");
        });
        Assert.True(CommonPasswords.Stems.Length >= 400, "The list should hold hundreds of stems.");
    }

    [Fact]
    public void A_sample_password_shorter_than_the_policy_stops_the_host_from_starting_with_a_message_that_says_so()
    {
        DatabaseOptionsValidator validator = new();

        Microsoft.Extensions.Options.ValidateOptionsResult result = validator.Validate(null, new DatabaseOptions
        {
            MigrateOnStartup = true,
            SeedSampleData = true,
            SampleUserPassword = new string('x', PasswordPolicy.MinimumLength - 1),
        });

        Assert.True(result.Failed);
        Assert.Contains($"at least {PasswordPolicy.MinimumLength} characters", string.Join(" ", result.Failures!), StringComparison.Ordinal);
    }
}

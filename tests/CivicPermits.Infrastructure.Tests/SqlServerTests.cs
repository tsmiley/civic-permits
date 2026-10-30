using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;
using CivicPermits.Domain.Permits;
using CivicPermits.Infrastructure.Persistence;
using CivicPermits.Infrastructure.Persistence.Seeding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CivicPermits.Infrastructure.Tests;

/// <summary>
/// Runs the same migrations and sample data against SQL Server, the second supported provider.
/// </summary>
/// <remarks>
/// Where it runs: against the server named by <c>CP_SQLSERVER_PASSWORD</c> (and, optionally, <c>CP_SQLSERVER_HOST</c>,
/// default <c>localhost,1433</c>) as the <c>sa</c> login, which is how CI runs it against a container; otherwise
/// against LocalDB on a Windows developer machine. Elsewhere it is skipped with a stated reason, unless
/// <c>CP_REQUIRE_SQLSERVER=true</c> is set (CI sets it), in which case an unreachable server fails the test instead of
/// skipping it, so CI cannot go green without ever having run it. The password is never written into the repository or
/// into a message: CI generates it for each run.
/// </remarks>
public class SqlServerTests
{
    private const string Instance = "(localdb)\\MSSQLLocalDB";
    private const string PasswordVariable = "CP_SQLSERVER_PASSWORD";
    private const string HostVariable = "CP_SQLSERVER_HOST";
    private const string RequireVariable = "CP_REQUIRE_SQLSERVER";

    [Fact]
    public async Task Migrations_sample_data_and_constraints_work_on_sql_server()
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        bool required = string.Equals(Environment.GetEnvironmentVariable(RequireVariable), "true", StringComparison.OrdinalIgnoreCase);
        bool viaContainer = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(PasswordVariable));

        if (!viaContainer && !OperatingSystem.IsWindows())
        {
            Unavailable(required, $"Set {PasswordVariable} to run against a SQL Server, or run on Windows with LocalDB.");
        }

        if (!await ServerIsAvailableAsync(viaContainer, cancel))
        {
            Unavailable(required, viaContainer
                ? "The SQL Server named by the environment could not be reached."
                : "SQL Server LocalDB is not installed or could not be started on this machine.");
        }

        string name = $"CivicPermitsTest_{Guid.NewGuid():N}";
        DbContextOptions<CivicPermitsDbContext> options = new DbContextOptionsBuilder<CivicPermitsDbContext>()
            .UseSqlServer(ConnectionString(name, viaContainer), sql => sql.MigrationsAssembly(MigrationAssemblies.SqlServer))
            .Options;

        FakeTimeProvider time = new(SqliteTestDatabase.SeedTime);

        try
        {
            await using (CivicPermitsDbContext context = new(options))
            {
                await context.Database.MigrateAsync(cancel);

                Assert.Empty(await context.Database.GetPendingMigrationsAsync(cancel));
                Assert.False(context.Database.HasPendingModelChanges());
                Assert.Equal((9, 12), await new SampleDataSeeder(context, time).SeedAsync(cancel));
            }

            await using (CivicPermitsDbContext context = new(options))
            {
                Assert.Equal((0, 0), await new SampleDataSeeder(context, time).SeedAsync(cancel));

                List<PermitApplication> applications = await context.PermitApplications
                    .AsNoTracking()
                    .Include(a => a.History)
                    .Include(a => a.Inspections)
                    .ToListAsync(cancel);

                Assert.Equal(12, applications.Count);
                Assert.All(applications, a => Assert.Equal(TimeSpan.Zero, a.CreatedOn.Offset));

                List<Guid> byFee = await context.PermitApplications
                    .OrderBy(a => a.FeeAssessed).ThenBy(a => a.Id)
                    .Select(a => a.Id)
                    .ToListAsync(cancel);
                Assert.Equal(applications.OrderBy(a => a.FeeAssessed.Cents).ThenBy(a => a.Id).Select(a => a.Id), byFee);

                // Many drafts share a NULL number; two real applications may not share a number.
                PermitType type = await context.PermitTypes.SingleAsync(t => t.Id == SampleData.Fence, cancel);
                PermitNumber taken = applications.First(a => a.Number is not null).Number!.Value;
                PermitApplication clash = PermitApplication.CreateDraft(
                    Guid.NewGuid(),
                    type,
                    SampleIdentities.ApplicantOne,
                    "Replace the side fence between two neighbouring gardens.",
                    PostalAddress.Create("14 Lantern Row", null, "Bramblewick", "TG", "70001"),
                    Money.Zero,
                    SqliteTestDatabase.SeedTime);
                clash.Submit(new Actor(SampleIdentities.ApplicantOne, ActorRole.Applicant), type, taken, SqliteTestDatabase.SeedTime);
                context.PermitApplications.Add(clash);

                await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(cancel));
            }
        }
        finally
        {
            await using CivicPermitsDbContext cleanup = new(options);
            await cleanup.Database.EnsureDeletedAsync(CancellationToken.None);
        }
    }

    private static void Unavailable(bool required, string reason)
    {
        if (required)
        {
            Assert.Fail($"{RequireVariable} is true, so the SQL Server test must run, but it cannot: {reason}");
        }

        Assert.Skip(reason);
    }

    /// <summary>The connection string for a database on the chosen server. It holds the password, so it is never printed.</summary>
    private static string ConnectionString(string database, bool viaContainer)
    {
        SqlConnectionStringBuilder builder = new()
        {
            InitialCatalog = database,
            TrustServerCertificate = true,
            ConnectTimeout = 15,
        };

        if (viaContainer)
        {
            builder.DataSource = Environment.GetEnvironmentVariable(HostVariable) is { Length: > 0 } host ? host : "localhost,1433";
            builder.UserID = "sa";
            builder.Password = Environment.GetEnvironmentVariable(PasswordVariable);
        }
        else
        {
            builder.DataSource = Instance;
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Opens a connection to the server's master database. A server that was only just started needs a while before it
    /// answers, so a container is retried for up to a minute; LocalDB is tried once, as before.
    /// </summary>
    private static async Task<bool> ServerIsAvailableAsync(bool viaContainer, CancellationToken cancel)
    {
        int attempts = viaContainer ? 30 : 1;

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await using SqlConnection connection = new(ConnectionString("master", viaContainer));
                await connection.OpenAsync(cancel);

                return true;
            }
            catch (SqlException) when (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancel);
            }
            catch (SqlException)
            {
                return false;
            }
        }

        return false;
    }
}

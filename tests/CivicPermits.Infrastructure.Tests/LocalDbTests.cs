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
/// Runs the same migrations and sample data against SQL Server LocalDB, the second supported provider. Skipped,
/// not failed, on a machine without LocalDB, so the suite stays green on Linux and on minimal CI images.
/// </summary>
public class LocalDbTests
{
    private const string Instance = "(localdb)\\MSSQLLocalDB";

    [Fact]
    public async Task Migrations_sample_data_and_constraints_work_on_sql_server_localdb()
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;

        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("LocalDB runs on Windows only.");
        }

        if (!await InstanceIsAvailableAsync(cancel))
        {
            Assert.Skip("SQL Server LocalDB is not installed or could not be started on this machine.");
        }

        string name = $"CivicPermitsTest_{Guid.NewGuid():N}";
        DbContextOptions<CivicPermitsDbContext> options = new DbContextOptionsBuilder<CivicPermitsDbContext>()
            .UseSqlServer(
                $"Server={Instance};Database={name};Trusted_Connection=True;TrustServerCertificate=True;",
                sql => sql.MigrationsAssembly(MigrationAssemblies.SqlServer))
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

    private static async Task<bool> InstanceIsAvailableAsync(CancellationToken cancel)
    {
        try
        {
            await using SqlConnection connection = new($"Server={Instance};Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15;");
            await connection.OpenAsync(cancel);

            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }
}

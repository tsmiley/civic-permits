# Running it for real

Civic Permits is a portfolio demonstration and is not deployed anywhere. This page says how it would be run, what each
setting does, and what a production deployment would still need. The last section is the honest list of what is not
done.

## Configuration

Everything comes from configuration (`appsettings.json`, then environment variables, then user secrets in development).
Nested names use a double underscore in environment variables.

| Setting | Default | Meaning |
|---|---|---|
| `Database__Provider` | `Sqlite` | `Sqlite` or `SqlServer` |
| `Database__ConnectionString` | `Data Source=civic-permits.db` | SQLite is a file path; SQL Server uses Windows authentication on LocalDB. A deployment that needs a password supplies the whole string by environment variable, never in a file in the repository. |
| `Database__MigrateOnStartup` | `false` (`true` in Development) | Apply pending migrations when the host starts. A real deployment applies migrations as a deliberate step before the new version runs. |
| `Database__SeedSampleData` | `false` (`true` in Development) | Load the fictional town. Needs `MigrateOnStartup`, and is **refused outright in Production**. |
| `Database__SampleUserPassword` | unset | The password for the sample sign-ins, at least twelve characters. Environment or user secrets only. |
| `Hosting__RedirectToHttps` | `true` | Turn off only behind a proxy that already terminates TLS and redirects |
| `RateLimiting__AuthPerWindow` | 10 | Sign-in, registration and refresh, per client address |
| `RateLimiting__PublicPerWindow` | 30 | Public permit lookup, per client address |
| `RateLimiting__GlobalPerWindow` | 600 | Everything else, per signed-in person or address |
| `RateLimiting__WindowSeconds` | 60 | The length of a window |

Options are validated when the host starts: a missing or malformed value stops it with a message that names the setting.

## Migrations

One history per provider. After changing the model:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/CivicPermits.Migrations.Sqlite    --startup-project src/CivicPermits.Migrations.Sqlite    --output-dir Migrations
dotnet ef migrations add <Name> --project src/CivicPermits.Migrations.SqlServer --startup-project src/CivicPermits.Migrations.SqlServer --output-dir Migrations
```

A test fails if the model and either provider's migrations disagree. To apply them without starting a host, run
`dotnet ef database update` against the same projects with a connection string.

## Health

`/health/live` answers whenever the process is up and runs no checks. `/health/ready` needs the database. Neither says
more than a status word, so neither is a way to learn what is installed.

## Behind a reverse proxy

Two things need configuring that the code cannot guess:

1. **Forwarded headers.** Rate limiting counts the connecting address. Behind a proxy that is the proxy's address, so
   every client shares one budget until forwarded headers are configured for the proxy. Configure
   `ForwardedHeadersOptions` with the proxy's address as a known proxy and do not trust the headers from anyone else:
   an `X-Forwarded-For` that anyone can set defeats the limit.
2. **Data-protection keys.** Bearer tokens, cookies and anti-forgery tokens are protected with the host's
   data-protection keys. The default key ring is local to the machine, which is fine for one instance. With more than
   one instance, or with a restart that must not sign everyone out, persist the key ring to shared storage and protect
   it (for example with a certificate or a key vault). Without this, a token made by one instance is unreadable to
   another.

TLS belongs to the proxy or the host. In Production the site sets `Secure` on its cookies and sends HSTS; set
`Hosting__RedirectToHttps` to `false` only when the proxy already redirects.

## Logging

Structured logging through Serilog to the console. Every line carries the request's correlation id; a failure's problem
document and the log line share it. Request logging is on, and records paths and query strings (so a looked-up permit number appears in the log). By
design the application does not write passwords, tokens or connection strings to the log; that is reviewed, not covered by
an automated test. A test does check that a deliberate failure never reaches the response, and the logging configuration
keeps the framework at Warning.

## Backups and data

The database holds the only record of applications, their history and inspections. A deployment needs its own backup
and restore plan for it. Nothing in the application deletes an application.

## What is not production-ready

This list is the point of this page.

- **No email.** There is no email confirmation, no password reset, and no notification to an applicant when a decision
  is made. A production service needs an email service and the flows that use it ([ADR 0011](adr/0011-registration-cannot-reveal-who-has-an-account.md)).
- **No payment.** The fee is recorded as paid by a clerk at the counter. No card input exists, by design.
- **One database, one writer pattern.** The design is correct for several instances (concurrency is guarded in the
  database), but it has only been run as one.
- **No multi-factor sign-in, and no audit log beyond each application's own history.**
- **Session ends silently after 8 hours** without activity ([accessibility report, 2.2.1](ACCESSIBILITY_REPORT.md)).
- **Times are UTC.** The town's time zone is not used.
- **No document upload.** Applications carry a description, not plans or attachments.
- **Not load tested.** The rate limits are sensible defaults, not measured ones.
- **No manual assistive-technology testing has been recorded** ([ACCESSIBILITY_TESTING.md](ACCESSIBILITY_TESTING.md)).

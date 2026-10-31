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
| `Database__SeedSampleData` | `false` (`true` in Development) | Load the fictional town. Needs `MigrateOnStartup`, and is **refused outright anywhere but Development**, Staging included, so no sample sign-in can exist on a deployed host. |
| `Database__SampleUserPassword` | unset | The password for the sample sign-ins, at least fifteen characters (the password policy's minimum). Environment or user secrets only. |
| `AllowedHosts` | `*` | **Set this in production** to the real host names, separated by semicolons (for example `permits.example.org`). The shipped `*` accepts any `Host` header. The cookie handler builds absolute redirect addresses from the request's host, so a forged one affects only the sender's own response today, but nothing should depend on that. |
| `Hosting__RedirectToHttps` | `true` | Turn off only behind a proxy that already terminates TLS and redirects |
| `RateLimiting__AuthPerWindow` | 10 | Sign-in, registration and refresh, per client address |
| `RateLimiting__PublicPerWindow` | 30 | Public permit lookup, per client address |
| `RateLimiting__GlobalPerWindow` | 600 | Everything else, per signed-in person or address |
| `RateLimiting__WindowSeconds` | 60 | The length of a window |

Options are validated when the host starts: a missing or malformed value stops it with a message that names the setting.

## Runtime

Run on ASP.NET Core **10.0.7 or later** (10.0.14 is the version the tests run on). Versions 10.0.0 to 10.0.6 on hosts that
are not Windows had a flaw (GHSA-9mv3-2cwr-p262) that let authentication cookies be forged, and both hosts protect their
cookies and bearer tokens with the framework's data protection. Outside Development each host checks the version it is
running on when it starts and refuses to run on an older one (or one it cannot read), with a message that says so.

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

TLS belongs to the proxy or the host. Outside Development the site sets `Secure` on its cookies and both hosts send
`Strict-Transport-Security: max-age=63072000; includeSubDomains` (two years, every subdomain; no `preload`, which is a
public commitment that is hard to undo and is left to the owner of the domain); set
`Hosting__RedirectToHttps` to `false` only when the proxy already redirects.

## Headers and caching

Both hosts send the same security headers on every response, success or refusal, page or file: a content security
policy with no inline script or style, `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy`,
`Permissions-Policy`, `Cross-Origin-Opener-Policy: same-origin`, `Cross-Origin-Resource-Policy: same-site` and
`X-Permitted-Cross-Domain-Policies: none`, plus `Strict-Transport-Security` outside Development. The server sends no
`Server` header. Tests check this in memory and, for the `Server` header, against real Kestrel.

Nothing a signed-in person receives is kept: every API response and every page served to a signed-in person, and the
account pages, carry `Cache-Control: no-store`. Signing out sends `Clear-Site-Data` for cache, cookies and storage.
Public pages and static files may be cached. A proxy or CDN in front must not override these headers.

## Logging

Structured logging through Serilog to the console. Every line carries the request's correlation id; a failure's problem
document and the log line share it. Request logging is on, and records paths and query strings (so a looked-up permit number appears in the log). By
design the application does not write passwords, tokens or connection strings to the log; that is reviewed, not covered by
an automated test. A test does check that a deliberate failure never reaches the response, and the logging configuration
keeps the framework at Warning.

A refused request leaves one extra line at Warning: not signed in, forbidden, rate limited, a redirect to sign-in or to the access-denied page, or any
request that was answered with a stable error code. It names the account's id (never its address), the method, the route
pattern, the status and the code, and tests check that no email address, password or token appears in any event. A
successful sign-in is not logged by the application.

## Backups, data and retention

The database holds the only record of applications, their history and inspections. A deployment needs its own backup
and restore plan for it. Nothing in the application deletes an application except the retention purge below.

### How long data is kept

| What | Kept | Counted from |
|---|---|---|
| A decided application (approved, denied, issued), with its history and inspections | 7 years | its last change |
| A draft, or a withdrawn application | 1 year | its last change |
| An application still in progress (submitted, in review, corrections requested) | not removed by age | |
| An applicant account with no application | 2 years | its last sign-in, or its creation if it never signed in |
| Staff accounts (clerk, inspector, administrator) | not removed by age; the owner manages them | |

The last change of an application is the latest of its creation, its history entries, its dates and the closing of its
inspections. The periods are one decision, written once in `RetentionPolicy`, and tested to the day: removed when the whole
period has passed, never a day before. An account made before creation dates were recorded starts its two years at the first
real purge run.

### The purge

Nothing runs by itself. An administrator (or a scheduled job signed in as one) calls `POST /api/v1/retention/purge`;
`?dryRun=true` only counts. A real run removes everything past its period in one transaction and writes **one audit entry
with the counts** (action `retention.purged`), or does nothing at all if it fails. An account whose applications are all
removed in a run is left for the next run, for its own period. Run it on a schedule that suits the deployment (monthly is
plenty) and keep an eye on the audit record (`/admin/audit`).

### Closing an account

An applicant can close their own account on the site (`/account/close`) or through the API (`POST /api/v1/auth/close-account`),
after entering their password again. The name and email address are removed from the account (an opaque placeholder
takes the address's place, because Identity needs a unique one), its password is removed, it is locked for good, every
session and refresh token it had ends, and one audit entry (`account.closed`, no name or address in it) is written. The
applications are kept under the periods above: they are the office's record, and they still hold the project description and the
site address the applicant gave. An access token already issued lasts until its 15 minutes are up.

## What is not production-ready

This list is the point of this page.

- **No email.** There is no email confirmation, no password reset, and no notification to an applicant when a decision
  is made. A production service needs an email service and the flows that use it ([ADR 0011](adr/0011-registration-cannot-reveal-who-has-an-account.md)).
- **No payment.** The fee is recorded as paid by a clerk at the counter. No card input exists, by design.
- **One database, one writer pattern.** The design is correct for several instances (concurrency on applications and
  permit numbers is guarded in the database; permit-type edits are last-write-wins), but it has only been run as one.
- **No breached-password check.** The password blocklist is bundled and small. A deployment that wants more should add a
  breached-password service as a second password validator; that is a network call and a choice for the deployment.
- **No multi-factor sign-in, and an audit record that is not tamper-proof.** Each application has its own history and
  permit-type changes have the audit record (`/admin/audit`), but the program only adds to it: someone with write access to
  the database could edit it. Sign-ins are not recorded. Making the record append-only at the database level, or shipping it
  somewhere the application cannot write, is up to the deployment.
- **Session ends silently after 8 hours** without activity ([accessibility report, 2.2.1](ACCESSIBILITY_REPORT.md)).
- **Times are UTC.** The town's time zone is not used.
- **No document upload.** Applications carry a description, not plans or attachments.
- **Not load tested.** The rate limits are sensible defaults, not measured ones.
- **No manual assistive-technology testing has been recorded** ([ACCESSIBILITY_TESTING.md](ACCESSIBILITY_TESTING.md)).

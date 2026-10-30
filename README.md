# Civic Permits

> **Work in progress: portfolio demonstration.** This repository is being built slice by slice and is not yet
> complete. This banner stays until the documentation slice lands.

An online service for a city permit office, built to show what a senior engineer's architecture, security and
accessibility work looks like in a realistic .NET application.

Residents and businesses apply for permits (building, sign, event, vendor) through accessible multi-step forms.
Clerks review applications, request corrections, and approve or deny with reasons. Inspectors schedule and
record inspections. Administrators configure permit types, fees and workflow rules. Anyone can look up a
permit's status by its number.

## About this repository

**This is newly developed portfolio work. It is original code written from an empty folder for this
repository. It is not source code from any employer, client or other company, and it was not adapted from any
private project.** The town, people, addresses and permits in the sample data are entirely fictional.

## Accessibility is the feature

The interface is built and tested against WCAG 2.1 AA and the Section 508 requirements that reference it.
That means "tested against", not "certified": no formal audit has been done, no manual screen-reader run has been
recorded yet, and the [accessibility report](docs/ACCESSIBILITY_REPORT.md) lists what was tested, how, what was found and what remains.

## Quick start

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Git. Nothing else: the
default database is a local SQLite file.

```bash
git clone https://github.com/tsmiley/civic-permits.git
cd civic-permits
dotnet build
dotnet test
dotnet run --project src/CivicPermits.Web
```

Then open <http://localhost:5211>. In Development the site creates its database, loads the fictional town's sample data
and, when you give it a password, the sample sign-ins (applicant, clerk, inspector, administrator). The password is
yours to choose; it is read from the environment and never written down:

```bash
export Database__SampleUserPassword="a password of at least twelve characters"
dotnet run --project src/CivicPermits.Web
```

Then sign in as `clerk@bramblewick.test` (or any address in the API section below) with that password. The REST API runs separately with
`dotnet run --project src/CivicPermits.Api` at <http://localhost:5210> (OpenAPI document at `/openapi/v1.json`
in Development).

To use SQL Server instead of SQLite (the free LocalDB edition works):

```bash
export Database__Provider=SqlServer
export Database__ConnectionString="Server=(localdb)\\MSSQLLocalDB;Database=CivicPermits;Trusted_Connection=True;TrustServerCertificate=True"
```

## Architecture

Clean architecture in one solution. Dependencies point inward: the domain depends on nothing.

| Project | Responsibility |
|---|---|
| `CivicPermits.Domain` | Entities, value objects and rules. No dependencies. |
| `CivicPermits.Application` | Use cases as request handlers behind a small dispatcher; contracts with the outer layers. |
| `CivicPermits.Infrastructure` | EF Core (SQLite and SQL Server), ASP.NET Core Identity, logging, sample-data seeding, and the hosting pieces both web hosts share. |
| `CivicPermits.Migrations.Sqlite`, `.SqlServer` | One migration history per database provider, kept out of Infrastructure so the two never mix. |
| `CivicPermits.Api` | The REST API: problem-details errors, health checks, OpenAPI. |
| `CivicPermits.Web` | The Blazor web site: server-rendered pages, accessibility built in. |
| `CivicPermits.Shared` | Plain contracts shared by the API and its clients. |

Tests: `Domain.Tests` and `Application.Tests` (unit), `Infrastructure.Tests` (migrations, seeding and
constraints against real SQLite and, where installed, SQL Server LocalDB), `Api.IntegrationTests` (the real API in
memory), `Web.A11yTests` (a real browser driving the running site).

## The domain

A permit application moves through a fixed workflow, held as one table in `ApplicationWorkflow` and checked in
the tests against a separate statement of the rules:

```
Draft ──submit──► Submitted ──start review──► InReview ──approve──► Approved ──issue──► Issued
  │                  ▲                           │  └─deny──► Denied
  │                  └────── submit again ───────┘
  │                                   request corrections ──► CorrectionsRequested
  └── withdraw (until the permit is issued or denied) ──► Withdrawn
```

The rules the code enforces, each with a test: only the applicant who owns an application can submit, edit or
withdraw it; only staff review and decide; a reason is required to deny or to ask for corrections; the fee is
worked out at submission from the permit type (a base amount plus a share of the declared value, in whole cents
and basis points, never floating point); a permit is issued only once the fee is recorded as paid and, where the
permit type needs one, the latest inspection has passed; every step is recorded with who took it and when.

## The web site

Server-rendered Blazor pages that work without JavaScript; the small scripts only improve focus handling.

| Who | What they can do |
|---|---|
| Anyone | Browse the permits and their fees, look up a permit by number, register, sign in |
| Applicants | Start an application in three steps (choose, describe, check and send), change a draft or one sent back, withdraw, follow the history |
| Clerks | See every application, start a review, approve, deny or ask for changes (with a reason the applicant reads), record the fee payment, book and cancel inspections, issue the permit |
| Inspectors | See the applications they are booked to inspect and record the result |
| Administrators | Everything a clerk can do, plus define, change, open and close permit types |

Accessibility is built in rather than added, and tested in a real browser on every change (WCAG 2.1 AA is the
target; this is not a claim of certification):

- one `h1` per page, landmarks, a skip link that really moves focus, a title that says where you are, and the
  current page marked in the navigation;
- every field has a visible label, hint and error tied to it with `aria-describedby`; required fields say so in
  words; forms are not validated by the browser, so errors are written, specific and listed together;
- a form that comes back with problems puts focus on an error summary whose links go to each field;
- a multi-step journey shows where you are in words (`aria-current="step"`), and every status is written out, never
  shown by colour alone;
- all text and interface parts meet the contrast ratios noted in `app.css` (computed, not estimated), focus is always
  visible, targets are at least 44px, tables scroll inside their own focusable region, and nothing scrolls sideways
  at 320px or at 200% text size;
- the content security policy forbids inline script and style, so the pages need neither.

What was tested, how, what was found and what remains is in [docs/ACCESSIBILITY_REPORT.md](docs/ACCESSIBILITY_REPORT.md), criterion by
criterion, and the procedure for the manual testing with assistive technology (not yet done, and said so) is in
[docs/ACCESSIBILITY_TESTING.md](docs/ACCESSIBILITY_TESTING.md). Every page in every state passes the axe-core rules; that is
necessary, not sufficient.

## The API

REST under `/api/v1`, documented by the OpenAPI document at `/openapi/v1.json` (Development). Errors are RFC 9457
problem documents with a stable `code` and a correlation id; an unexpected failure is a bare 500 that names nothing.

| Area | Endpoints | Who |
|---|---|---|
| Sign-in | `POST /auth/register`, `/auth/login`, `/auth/refresh`; `GET /auth/me` | anyone (rate limited); `me` needs a token |
| Permit types | `GET /permit-types[/{id}]` | anyone |
| | `POST`, `PUT /permit-types[/{id}]`, `POST /permit-types/{id}/open`, `/close` | administrators |
| Applications | `POST /applications`, `PUT /applications/{id}`, `POST .../submit`, `.../withdraw` | the applicant who owns it |
| | `POST .../start-review`, `.../request-corrections`, `.../approve`, `.../deny`, `.../issue`, `.../payment`, `.../inspections` | clerks and administrators |
| | `POST .../inspections/{id}/result` | the assigned inspector, or an administrator |
| | `GET /applications`, `GET /applications/{id}` | whoever may see them (see below) |
| Public lookup | `GET /public/permits/{number}` | anyone (rate limited): type, status and dates only |

Who may see an application: an applicant sees their own, staff see all, an inspector sees those with an inspection
booked for them. To anyone else it does not exist, and the answer is 404, never 403.

How the API stays safe: opaque data-protected bearer tokens (15 minutes, refresh for 8 hours, nothing to sign with so
no key in configuration); passwords of twelve or more characters, five wrong attempts lock the account for fifteen
minutes, and a wrong password, an unknown address and a locked account get the same answer; registering an address
that exists looks exactly like registering a new one; roles are checked by policy at the endpoint and again in the
handler; every change carries an optimistic-concurrency version so two people cannot both win; permit numbers come
from a per-year counter saved with the submission, so a failed request never uses one up. In this demonstration
there is no email confirmation, and bearer tokens are protected with the host's data-protection keys, which a
multi-instance deployment must share.

Sample sign-ins exist only when `Database:SampleUserPassword` is supplied (an environment variable or user secrets,
never a file in the repository). Their addresses use the reserved `.test` domain:
`applicant.one@bramblewick.test`, `clerk@bramblewick.test`, `administrator@bramblewick.test`,
`inspector.one@bramblewick.test` and so on.

## Database

SQLite is the default and needs nothing installed. Setting `Database:MigrateOnStartup` applies the migrations
when the API starts, and `Database:SeedSampleData` (Development only, refused in Production) loads the fictional
Town of Bramblewick: nine permit types and twelve applications at every stage of the process. Seeding is
idempotent. Sample data holds no names, no email addresses and no passwords.

To add a migration after changing the model (one per provider):

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/CivicPermits.Migrations.Sqlite --startup-project src/CivicPermits.Migrations.Sqlite --output-dir Migrations
dotnet ef migrations add <Name> --project src/CivicPermits.Migrations.SqlServer --startup-project src/CivicPermits.Migrations.SqlServer --output-dir Migrations
```

A test fails if the model and either provider's migrations disagree.

## Status

| Slice | What | State |
|---|---|---|
| S0 | Solution scaffold, hosting, health, problem details, CI | in review |
| S1 | Domain model, workflow, database, sample data | in review |
| S2 | Identity, roles, API endpoints | in review |
| S3 | Web interface | in review |
| S4 | Automated and manual accessibility testing, report | in review |
| S5 | Documentation, screenshots, diagrams | planned |

## Licence

MIT, see [LICENSE](LICENSE). Third-party packages are listed in [NOTICE](NOTICE).

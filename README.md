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
That means "tested against", not "certified": no formal audit has been done, and the accessibility report
(added with the accessibility slice) will list what was tested, how, what was found and what remains.

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

Then open <http://localhost:5211>. The REST API runs separately with
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
| `CivicPermits.Infrastructure` | EF Core (SQLite and SQL Server), ASP.NET Core Identity, logging, and the hosting pieces both web hosts share. |
| `CivicPermits.Api` | The REST API: problem-details errors, health checks, OpenAPI. |
| `CivicPermits.Web` | The Blazor web site: server-rendered pages, accessibility built in. |
| `CivicPermits.Shared` | Plain contracts shared by the API and its clients. |

Tests: `Domain.Tests` and `Application.Tests` (unit), `Api.IntegrationTests` (the real API in memory),
`Web.A11yTests` (a real browser driving the running site).

## Status

| Slice | What | State |
|---|---|---|
| S0 | Solution scaffold, hosting, health, problem details, CI | in review |
| S1 | Domain model, workflow, database, sample data | planned |
| S2 | Identity, roles, API endpoints | planned |
| S3 | Web interface | planned |
| S4 | Automated and manual accessibility testing, report | planned |
| S5 | Documentation, screenshots, diagrams | planned |

## Licence

MIT, see [LICENSE](LICENSE). Third-party packages are listed in [NOTICE](NOTICE).

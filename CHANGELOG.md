# Changelog

All notable changes are recorded here, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added
- S4: the accessibility testing slice. An axe-core sweep (unmodified, through Deque.AxeCore.Playwright, with a canary
  that proves it can find defects) over 53 states of every page as every kind of person; browser checks for visible
  focus on every keyboard stop, boundaries under forced colours and text-spacing overrides; the manual testing
  procedure (`docs/ACCESSIBILITY_TESTING.md`) and the accessibility report with a criterion-by-criterion table
  (`docs/ACCESSIBILITY_REPORT.md`) that separates what is verified from what is only designed and what is owed.
  The skip link moved inside the banner landmark. axe-core is listed in NOTICE as MPL-2.0, a reviewed exception.
- S3: the web site. Server-rendered Blazor pages for browsing and looking up permits, registering and signing in
  (cookie sessions, anti-forgery on every post), the three-step application journey, the clerk, inspector and
  administrator workspaces, and permit type management; shared form components with errors tied to fields; focus
  management that leaves a first load alone and moves to the error summary after a refused post; 33 browser tests
  covering whole journeys, keyboard use, reflow at 320px and 200% text.
- S3: collecting rule checks (`PostalAddress.Check`, `PermitApplication.CheckDetails`) so a form lists every problem
  at once; a shared registration service and rate limiting used by both hosts; a staff-only inspector list.
- CI and CodeQL now run on push and pull request; Dependabot version updates are on.
- S2: use cases for permit types, applications, inspections and the public lookup, behind the dispatcher, with store
  abstractions implemented in Infrastructure; bearer-token sign-in with Identity, four roles and policies; the REST
  endpoints with problem-details errors, per-address rate limits, OpenAPI with bearer security; a per-year permit
  number allocator and an optimistic-concurrency version on applications (a second migration per provider); sample
  sign-ins from a runtime password; 74 API integration tests.
- A security ledger (`docs/SECURITY_LEDGER.md`) with its first row.
- S1: the domain model. Permit types with fee rules, an application aggregate with a workflow held as one table,
  status history, payment recording and inspections; money in whole cents; strict address and number value
  objects; rule violations carry stable codes.
- S1: EF Core mapping with value converters (money to cents, moments to UTC, permit numbers to text), one
  migration history per provider (SQLite and SQL Server), and migrate-on-start and seed-on-start options.
- S1: sample data for the fictional Town of Bramblewick, built through the real domain methods and idempotent.
- S1: tests for the domain (including an independent specification of the workflow), migrations on both providers,
  seeding, constraints and a LocalDB run that skips when LocalDB is absent.
- S0: the solution scaffold on .NET 10 LTS: domain, application, infrastructure, API, web and shared projects, and
  four test projects.
- A dispatcher for use cases, and a first use case that describes the running service.
- The permit number value object, with strict parsing.
- Hosting shared by the API and the web site: structured logging, correlation ids, security headers, problem-details
  errors, and liveness and readiness health checks.
- EF Core persistence with SQLite by default and SQL Server by configuration; ASP.NET Core Identity stores.
- An accessible page shell: skip link, landmarks, focus on navigation, visible focus, reduced motion.
- Build discipline: warnings as errors, central package management, vulnerability audit, a license audit script,
  and CI and CodeQL workflows.

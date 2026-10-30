# Changelog

All notable changes are recorded here, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Security
- Fixes from the whole-repository security review (ledger rows 3 to 15). The Lows: HSTS is two years with subdomains;
  sample data and sample sign-ins are refused anywhere but Development; an inspector stops seeing an application once
  their booking is cancelled before any visit; hosts refuse to start outside Development on an ASP.NET Core older than
  10.0.7 (a version that let cookies be forged); the register and refresh routes and the site's register form have
  rate-limit tests; the deployment guide names `AllowedHosts` and the minimum runtime.
- Hardening after the independent review of S0 to S4. An applicant who does not own an application now gets the same
  refusal whatever its status; withdrawing or issuing cancels inspections still scheduled. The API is closed by
  default (a fallback policy requires a sign-in; ten operations are public and a test pins the list), and each
  applicant and staff step is also guarded by policy at the endpoint. Everything under `/api`, every page a signed-in
  person receives and the account pages are `Cache-Control: no-store`; signing out sends `Clear-Site-Data`. Two more
  security headers, and no `Server` header. An unknown address now costs the same password check as a wrong password
  on both the API and the site. A refused request leaves one log line with the account id, route, status and code.
  A failed role assignment at registration removes the new account instead of leaving it. The dispatcher surfaces a
  handler's own exception and tells apart a request that is two kinds of request. The correlation-id pattern refuses a
  trailing newline; the return-address check refuses every control character.
- Tests: a new `Web.IntegrationTests` project for the site's cookies, caching, headers and sign-in behaviour; hardening
  tests in `Api.IntegrationTests`; a real-Kestrel header test; the accessibility canary now also proves the assertion
  fails when it should and that each axe tag in use finds something, and ignores only a cancelled page navigation.
- The security ledger has an Owner column and a row for the review.

- CI runs the SQL Server tests on every push against the official Developer-edition container, with a password made up
  inside the run and masked before any use (never in the repository, on a command line or in a log). The SQL Server test
  now takes a server from `CP_SQLSERVER_PASSWORD` and `CP_SQLSERVER_HOST`, keeps LocalDB for Windows machines, and fails
  instead of skipping when `CP_REQUIRE_SQLSERVER=true`.

### Added
- S5: the documentation slice. A rewritten README with screenshots (generated from the running site, with text
  alternatives) and the work-in-progress banner removed; `docs/ARCHITECTURE.md` with the dependency rule, request
  flow, data model and workflow diagrams; eleven architecture decision records; an API guide; a deployment guide with
  an explicit list of what is not production-ready; a threat model; and the security ledger closed on its first row.
  The workflow diagram is drawn from the code by a test, the data diagram is checked against the real model, and every
  relative link in the documentation is checked to go somewhere that exists.
- S4: the accessibility testing slice. An axe-core sweep (unmodified, through Deque.AxeCore.Playwright, with a canary
  that proves it can find defects) over the 53 page states listed in the report, as each kind of person; browser checks for visible
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

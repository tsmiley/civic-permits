# 0003. SQLite by default, SQL Server by configuration, one migration history each
Status: Accepted
Date: 2026-10-30

## Context
A reader should be able to clone the repository and run it with nothing installed. A real permit office would run
SQL Server. Both must work, and neither may be an afterthought, because a provider that is not tested rots.

## Decision
SQLite is the default (a file; no server). SQL Server, including the free LocalDB edition, is selected by
configuration. The two providers differ in column types and generated SQL, so each has its own migration project
(`Migrations.Sqlite`, `Migrations.SqlServer`) with its own history. Infrastructure chooses the migrations assembly by
provider. Types that the providers handle differently are converted in one place so both behave the same: money to
whole cents, moments to UTC, permit numbers to text.

Tests keep both honest: the migrations are applied on SQLite and on SQL Server (a Developer-edition container in CI, LocalDB on a Windows machine), and a test fails if the model and either
provider's migrations disagree (so a forgotten migration is caught).

## Consequences
- Anyone can run the project in one command.
- Two migrations per model change. The README gives the two commands; the test catches a forgotten one.
- The SQL Server test skips, with a stated reason, on a machine with neither LocalDB nor a server named by
  `CP_SQLSERVER_PASSWORD`, so a developer's suite stays green; CI sets `CP_REQUIRE_SQLSERVER=true`, which turns that
  skip into a failure, so CI cannot pass without having run it. The SQL Server model-versus-migrations test needs no
  server and always runs. The CI container image is a floating tag (`2022-latest`) that nothing updates automatically.
- SQLite cannot sort or sum `DateTimeOffset` or decimals reliably; the converters make that a non-issue.

## Alternatives
- **SQL Server only.** Realistic, but a reader without it could not run the project.
- **One migration history for both.** Not possible without lowest-common-denominator column types.
- **A provider abstraction that hides the differences.** The converters are the small, honest version of this.

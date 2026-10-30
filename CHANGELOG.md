# Changelog

All notable changes are recorded here, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added
- S0: the solution scaffold on .NET 10 LTS: domain, application, infrastructure, API, web and shared projects, and
  four test projects.
- A dispatcher for use cases, and a first use case that describes the running service.
- The permit number value object, with strict parsing.
- Hosting shared by the API and the web site: structured logging, correlation ids, security headers, problem-details
  errors, and liveness and readiness health checks.
- EF Core persistence with SQLite by default and SQL Server by configuration; ASP.NET Core Identity stores.
- An accessible page shell: skip link, landmarks, focus on navigation, visible focus, reduced motion.
- Build discipline: warnings as errors, central package management, vulnerability audit, a license audit script,
  and CI and CodeQL workflows that run on demand until the repository is public.

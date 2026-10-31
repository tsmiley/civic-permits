# 0001. Clean architecture in small projects
Status: Accepted
Date: 2026-10-30

## Context
The point of the project is to show how a permit office's rules are kept right over time. Those rules are the
valuable part; the web framework, the database and the user interface will all be replaced eventually. A single web
project would let a controller reach for a database context in one line, and within a year the rules would be spread
across pages, handlers and queries.

## Decision
One solution of small projects with dependencies pointing inward: `Domain` depends on nothing; `Application` depends
on `Domain` and the plain contracts in `Shared`; `Infrastructure` implements the contracts `Application` defines; the
two hosts (`Api`, `Web`) are thin and ask the application layer. Each provider's migrations live in a project of their
own. The project boundaries are enforced by the compiler: a project that does not reference EF Core cannot use it.

## Consequences
- The rules can be unit-tested with no server, database or framework, and they run in milliseconds (over 600 tests in about a second).
- The web site and the API share every rule by construction, because neither contains any.
- More projects, and a little ceremony: a store contract in `Application` and its implementation in `Infrastructure`
  for each thing the rules need.
- A reader has to learn where things are. [ARCHITECTURE.md](../ARCHITECTURE.md) is that map.

## Alternatives
- **One web project with folders.** Fewer files, but the boundary is a convention that erodes; rejected because the
  boundary is the thing being demonstrated.
- **Vertical slices in one project.** Good locality, but it makes "the domain depends on nothing" a promise instead of
  a fact the build checks.

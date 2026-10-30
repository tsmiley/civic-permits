# Architecture decision records

One short record for each decision that shaped the design and that a reader might question. Each says what the
situation was, what was decided, what it costs, and what was rejected. A decision that is reversed gets a new record
that supersedes the old one; the old one is never edited to hide it.

| # | Decision |
|---|---|
| [0001](0001-clean-architecture-in-small-projects.md) | Clean architecture in small projects |
| [0002](0002-a-forty-line-dispatcher-instead-of-a-mediator-library.md) | A forty-line dispatcher instead of a mediator library |
| [0003](0003-sqlite-by-default-sql-server-by-configuration.md) | SQLite by default, SQL Server by configuration, one migration history each |
| [0004](0004-money-in-cents-moments-in-utc.md) | Money in whole cents, moments in UTC |
| [0005](0005-the-workflow-is-one-table.md) | The workflow is one table |
| [0006](0006-the-web-site-calls-the-application-layer-directly.md) | The web site calls the application layer directly |
| [0007](0007-opaque-bearer-tokens-and-cookie-sessions.md) | Opaque bearer tokens for the API, cookie sessions for the site |
| [0008](0008-optimistic-concurrency-and-a-number-allocator.md) | Optimistic concurrency and a transactional number allocator |
| [0009](0009-accessibility-is-tested-in-layers-and-never-claimed-as-certified.md) | Accessibility is tested in layers and never claimed as certified |
| [0010](0010-no-foreign-key-from-an-application-to-its-applicant.md) | No foreign key from an application to its applicant |
| [0011](0011-registration-cannot-reveal-who-has-an-account.md) | Registration cannot reveal who has an account |

## Template

```
# NNNN. Title
Status: Accepted | Superseded by NNNN
Date: YYYY-MM-DD

## Context        what forced a decision
## Decision       what was chosen, in one paragraph
## Consequences   what it costs and what it makes easy
## Alternatives   what was rejected, and why
```

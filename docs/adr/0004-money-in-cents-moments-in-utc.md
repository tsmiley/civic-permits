# 0004. Money in whole cents, moments in UTC
Status: Accepted
Date: 2026-10-30

## Context
Fees are money and rules compare dates, so rounding and time zones are where permit offices get things wrong.
Floating-point money loses cents; a fee computed in one place and displayed in another must agree to the cent. A
moment stored with a local offset sorts wrongly when text is compared.

## Decision
`Money` is a value object over a whole number of cents. It cannot be negative, adding past the largest amount fails
instead of wrapping, and a value-based share of it uses basis points (hundredths of a percent, an integer) and rounds
half up to the cent, with the rounding rule tested at its edges. Dollars with a fraction of a cent are refused rather
than rounded. Every moment is stored as UTC; time is always passed into the domain and never read from a clock.

## Consequences
- The fee for a permit is exact and the same everywhere; sorting and summing in the database are exact.
- The interface must convert: people type dollars, so the web site parses "24,000.50" into cents and refuses a third
  decimal.
- Times are shown in UTC with the zone stated. The town's own time zone is a future change, recorded as a known gap.

## Alternatives
- **`decimal` everywhere.** Exact in .NET, but SQLite stores it as floating point and cannot compare it reliably.
- **Store `DateTimeOffset` as given.** Sorts and ranges wrongly on SQLite.

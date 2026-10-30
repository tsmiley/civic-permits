# 0008. Optimistic concurrency and a transactional number allocator
Status: Accepted
Date: 2026-10-30

## Context
Two clerks can act on the same application at the same moment: one approves, one denies. Two applicants can submit at
the same moment: both need a public number, and numbers must be unique, human-friendly (`CP-2026-000042`), and not
wasted by a request that fails.

## Decision
- **Concurrency:** every application carries a version, raised by the database context on any change, including a
  change that only adds a history entry or an inspection (which would otherwise leave the application row unguarded).
  A stale write loses and becomes a 409 `request.concurrent_change`. The domain does not know the version exists.
- **Numbers:** a per-year counter row, itself guarded by using its value as a concurrency token, is advanced in the
  same transaction as the submission. Two requests cannot get the same number; a submission that fails never uses one
  up (the claim is simply not saved). A unique index on the number is the last line of defence. The sample-data
  seeder advances the counter past the numbers it writes.

## Consequences
- No locks held across a request, and lost updates are impossible rather than unlikely.
- A losing request gets a clear conflict and the person reloads; there is no automatic merge.
- Numbers within a year are gap-free in the absence of failures at save time, and restart each year.

## Alternatives
- **A database sequence.** Atomic, but a failed request would burn a number, and SQLite has none.
- **`MAX(number) + 1`.** Races, as described.
- **Pessimistic locks.** Hold resources across slow human steps.

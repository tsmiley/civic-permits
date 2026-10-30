# 0005. The workflow is one table
Status: Accepted
Date: 2026-10-30

## Context
An application moves through statuses, and who may take which step in which status is the heart of the office's
rules. Scattered `if` statements for this are where permit systems go wrong: a step allowed in one screen and not in
another, an applicant who can approve their own application, a dead end nobody can leave.

## Decision
`ApplicationWorkflow` holds the process as data: one row per allowed step (from, action, to, roles). The aggregate
asks the table. Each method then checks, in a fixed order, that the workflow allows the step, that the role may take
it (and an applicant owns the application), and the rules specific to the step. A refused step changes nothing.

The table is tested against a separately written specification (so a typo cannot be copied into both), the test then
drives the real aggregate through every status, action and role (576 combinations), and reachability and
"no dead end" are asserted. The documentation's diagram is drawn from the table by a test and fails when it drifts.

## Consequences
- What the process is can be read in twelve lines and checked by a person who is not a programmer.
- Adding a status or a step is a row plus its tests; the exhaustive test fails until every combination is decided.
- A consequence of the fixed order of checks: an applicant who tries an impossible step is told "cannot be done now"
  (409) rather than "not allowed" (403). The role refusal is tested on a step that is possible.
- Rules that are not edges (the fee must be paid, an inspection must have passed, a reason is required) stay in the
  aggregate's methods, each with its own test and stable error code.

## Alternatives
- **A state-machine library.** Another dependency for a table.
- **A `switch` on status in each method.** Readable locally and unverifiable as a whole.

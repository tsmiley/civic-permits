# Security ledger

Findings from reviews and incidents, one row each, with an owner and a date. Nothing Critical or High ships open.
Newest last. This file holds no secret and no personal data.

| # | Date | Kind | Finding | Severity | Status | Prevention |
|---|---|---|---|---|---|---|
| 1 | 2026-10-30 | Incident | A personal email address appeared in a merge commit's author field in the history of this private repository, because the hosting site's web merge uses the account's primary address. Found before publication. | Medium (private repository, no outside exposure) | Open. The repository is to be recreated before it is made public, which removes the commit; this row is closed when that is done and the history has been checked. | The account's email-privacy settings ("keep my email addresses private" and "block command line pushes that expose my email") must be on before any merge; every local commit uses the no-reply identity; the publication checklist runs `git log --all --format='%an <%ae> %cn <%ce>' \| sort -u` and expects one no-reply address. |

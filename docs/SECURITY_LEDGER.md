# Security ledger

Findings from reviews and incidents, one row each, with an owner and a date. Nothing Critical or High ships open.
Newest last. This file holds no secret and no personal data.

| # | Date | Kind | Finding | Severity | Status | Prevention |
|---|---|---|---|---|---|---|
| 1 | 2026-10-30 | Incident | A personal email address appeared in a merge commit's author field in the history of this private repository, because the hosting site's web merge uses the account's primary address. Found before publication. | Medium (private repository, no outside exposure) | **Closed 2026-10-30**, resolved by recreation and deletion: the repository was recreated from a history rewritten with the no-reply identity, the original copy was deleted, and the history of the recreated repository was checked on a fresh clone before it was made public. | The account's email-privacy settings ("keep my email addresses private" and "block command line pushes that expose my email") are on; every local commit uses the no-reply identity; the publication checklist runs `git log --all --format='%an <%ae> %cn <%ce>' \| sort -u` and expects only the no-reply address (and, for GitHub's own generated pull-request merge references, GitHub's own no-reply committer). |

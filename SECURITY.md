# Security policy

This is a portfolio project and is not deployed anywhere. It holds no real data.

## Reporting a problem

If you find a security problem in the code, please report it privately through GitHub's **Report a vulnerability**
button on the Security tab of this repository (private vulnerability reporting), rather than opening a public
issue. Expect an acknowledgement within a few days. There is no bug bounty.

## How long personal data is kept

Decided applications (approved, denied or issued) are kept **7 years after their last change**; drafts and withdrawn
applications **1 year** after theirs; an applicant's account with no application **2 years after the last time it was
used** (its last sign-in, or its creation if it never signed in). An application still in progress is never removed by age.
An applicant can **close their own account** at any time: their name and email address are removed, it can never sign in
again, and their applications are kept under the periods above. Details and how a purge is run are in
[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md#backups-data-and-retention).

## What the project does about security

The design is written down in [docs/THREAT_MODEL.md](docs/THREAT_MODEL.md): what is protected, who might attack it,
how, what is done about each threat with the test that shows it, and what is not done. Findings and their fixes are
recorded in [docs/SECURITY_LEDGER.md](docs/SECURITY_LEDGER.md). How it would be run, and what a production deployment
still needs, is in [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

The standing practices:

- Secrets never enter the repository: user secrets in development, environment variables elsewhere. The sample
  sign-ins take their password at run time; the tests make a fresh one per run.
- Warnings are build errors and a package with a known vulnerability fails the build. Dependabot, CodeQL and GitHub's
  secret scanning with push protection run on this repository.
- Every response carries a content security policy, `nosniff`, a no-framing rule and a correlation id; an unexpected
  failure is a bare 500 that reveals nothing.
- Access is decided in one place, and a thing someone may not see is reported as not found, never forbidden.
- Input is validated at the boundary and invariants are enforced in the domain.

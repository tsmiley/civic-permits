# Threat model

A small, honest threat model for a permit office's online service. It names what is protected, who might attack it,
how, what is done about each, and what is not done. It is written for this project as it stands, and it is revisited
at each release.

## What is protected

| Asset | Why it matters |
|---|---|
| The record of applications, decisions and inspections | It is the office's legal record; altering or losing it is the worst outcome |
| Who has applied for what | Applicants' privacy; an address and a project are personal |
| Staff accounts | A clerk or administrator can decide applications and change fees |
| Permit numbers | A public identifier; guessing must not reveal anything |
| Availability | Applicants must be able to apply; the office must be able to work |

## Who and how

| Actor | Capability | Goal |
|---|---|---|
| Anonymous visitor | Reaches the public pages and the unauthenticated API | Enumerate accounts or permits; scrape; deny service |
| Applicant | An account | See someone else's application; approve their own; cheat a fee |
| Staff or inspector | An account | Act beyond their role; act on others' behalf |
| Anyone with a link | Can make a signed-in person's browser send a request | Make a person do something they did not intend |

## Threats and what is done

| Threat | What is done | Evidence |
|---|---|---|
| Reading another person's application | Access is decided in one place (`Access.cs`): an applicant sees their own, staff see all, an inspector sees those with an inspection booked for them; anyone else gets **404, never 403**, so an identifier cannot be confirmed | API and browser tests of cross-applicant access; `Another_applicants_application_is_a_real_404...` |
| An applicant approving, reviewing or paying for their own application | The workflow table names the roles; the role is checked in the aggregate and again by policy at the endpoint | The 576-combination test; `An_applicant_cannot_review_decide_or_record_payment...` |
| Skipping a step (issuing before the fee is paid or an inspection has passed) | Preconditions inside `Issue`; each refusal has a code | `A_permit_that_needs_an_inspection_cannot_be_issued_until...` and the others |
| Two staff acting at once; losing an update | A version on every application, raised on any change; a stale write is a 409 | `Two_people_changing_the_same_application_cannot_both_succeed` |
| Duplicate or wasted permit numbers | A per-year counter saved with the submission, guarded by a concurrency token, with a unique index behind it | `Two_requests_cannot_be_given_the_same_number` |
| Guessing passwords | Twelve-character minimum; five failures lock for fifteen minutes; per-address rate limit; one answer for every failure | `Five_wrong_passwords_lock_the_account...`, rate-limit tests |
| Finding out who has an account | Registration answers a taken address like a new one; sign-in gives one answer; the public lookup treats malformed and unknown numbers alike | `Registering_an_address_that_exists_looks_exactly_like...` |
| Scraping or guessing permit numbers | The public lookup returns type, status and dates only, is rate limited, and parses strictly (a trailing newline or wrong case is refused) | The lookup tests, including injection-shaped input |
| Cross-site request forgery | Cookie `SameSite=Lax`; an anti-forgery token (`SameSite=Strict` cookie) on every post, sign-out included | Journeys post real forms with the token |
| Cross-site scripting | The content security policy forbids inline script and style and allows only the site's own origin; output is encoded by the framework; success messages are fixed text looked up from a key, never echoed input | The page-load test fails on any policy violation |
| Open redirect after sign-in | A return address must be a path on this site; `//host`, `https://host` and `/\host` are refused | `A_sign_in_link_that_points_off_the_site_never_sends_anyone_there` |
| Clickjacking | `frame-ancestors 'none'` and no framing | Header tests |
| Information leaks in errors | Unexpected failures are a bare 500; expected ones are problem documents with no stack, type name or connection string | `An_unexpected_failure_is_a_500_problem_that_reveals_nothing...` (the deliberate-failure test) |
| Injection into the database | Entity Framework parameterises every query; no raw SQL is written | Reviewed; the lookup tests include SQL-shaped input |
| A secret in the repository | Secrets are never written: user secrets in development, environment variables elsewhere; sample sign-ins take their password at run time; the tests make a fresh one per run | A secret and name sweep over the full history before publication |
| A vulnerable dependency | A package with a known vulnerability fails the restore; Dependabot and CodeQL run on the public repository; every package is MIT, Apache-2.0 or BSD apart from reviewed, listed exceptions | CI; `scripts/license-audit.ps1` |
| Sample data that is someone's real data | The town, people and addresses are invented; sample addresses use the reserved `.test` domain | A test asserts the sample data has no `@` and no user rows |

## What is not done (and why it is acceptable here)

- **Timing differences** between "address taken" and "address new" in registration are not hidden. A determined
  attacker could measure them. Equalising them needs an email flow.
- **No multi-factor sign-in, no password reset, no email confirmation.** All need an email service; listed in
  [DEPLOYMENT.md](DEPLOYMENT.md).
- **No audit trail beyond each application's own history.** Who signed in, and failed attempts, are in the log but not
  in a queryable record.
- **Rate limiting trusts the connecting address.** Behind a proxy it must be given forwarded headers it can trust
  ([DEPLOYMENT.md](DEPLOYMENT.md)); until then every client behind the proxy shares one budget.
- **No denial-of-service protection beyond the rate limits.** That belongs in front of the application.
- **Not penetration tested** by anyone but the author, and not independently reviewed for security beyond the pull
  request reviews.

## Reporting

See [SECURITY.md](../SECURITY.md). Findings and their fixes are recorded in [SECURITY_LEDGER.md](SECURITY_LEDGER.md).

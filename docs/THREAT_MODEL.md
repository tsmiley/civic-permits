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
| Reading another person's application | Access is decided in one place (`Access.cs`): an applicant sees their own, staff see all, an inspector sees those with an inspection booked for them or carried out by them, and stops seeing one whose booking was cancelled before any visit; anyone else gets **404, never 403**, so an identifier cannot be confirmed | API and browser tests of cross-applicant access; `Another_applicants_application_is_a_real_404...` |
| An applicant approving, reviewing or paying for their own application | The workflow table names the roles; the role is checked in the aggregate, which an applicant who does not own the application cannot get past whatever its status, and again by policy at each endpoint (applicant steps need the Applicant role, staff steps the Staff policy) | The 576-combination test; `Every_staff_step_is_closed_to_an_applicant_at_the_endpoint...`, `Every_applicant_step_is_closed_to_staff_at_the_endpoint` |
| Skipping a step (issuing before the fee is paid or an inspection has passed) | Preconditions inside `Issue`; each refusal has a code | `A_permit_that_needs_an_inspection_cannot_be_issued_until...` and the others |
| Two staff acting at once; losing an update to an application | A version on every application, raised on any change to it or its history or inspections; a stale write is a 409. Permit-type edits are **not** guarded: the last administrator to save wins | `Two_people_changing_the_same_application_cannot_both_succeed` |
| Duplicate or wasted permit numbers | A per-year counter saved with the submission, guarded by a concurrency token, with a unique index behind it | `Two_requests_cannot_be_given_the_same_number` |
| Guessing passwords | Twelve-character minimum; five failures lock for fifteen minutes; per-address rate limit (ten sign-in attempts a minute by default, with no settings at all); one answer for every failure | `Five_wrong_passwords_lock_the_account...`, `With_no_settings_at_all_the_eleventh_sign_in_attempt_in_a_minute_is_refused`, `The_rate_limit_defaults_are_pinned` |
| Finding out who has an account | Registration answers a taken address like a new one; sign-in gives one answer, and an address nobody registered costs the same password check as one that exists, so the time does not tell them apart (equalised work, not a measured guarantee); the public lookup treats malformed and unknown numbers alike | `Registering_an_address_that_exists_looks_exactly_like...`, `An_address_nobody_registered_costs_as_many_password_checks_as_one_that_exists`, `A_wrong_password_and_an_unknown_address_get_the_same_page` |
| An endpoint added later that nobody remembered to protect | The API requires a signed-in person by default; the ten public operations are marked one by one and a test pins that exact list | `Exactly_the_ten_public_operations_are_open_to_anyone_and_everything_else_needs_a_sign_in_by_default` |
| Another person using the same browser after sign-out, or a shared cache | Everything the API sends, and every page a signed-in person receives or the account pages, carries `Cache-Control: no-store`; signing out sends `Clear-Site-Data` for cache, cookies and storage | `Responses_that_carry_a_persons_data_are_never_kept`, `Pages_a_signed_in_person_receives_and_the_account_pages_are_never_kept_...`, `Signing_out_tells_the_browser_to_forget_the_site_...` |
| Learning what the server runs | The server does not send a `Server` header, and no `X-Powered-By` is sent | `The_real_server_names_neither_itself_nor_its_version...` (real Kestrel, over a socket) |
| Scraping or guessing permit numbers | The public lookup returns type, status and dates only, is rate limited, and parses strictly (a trailing newline or wrong case is refused) | The lookup tests, including injection-shaped input |
| Cross-site request forgery | Cookie `SameSite=Lax`; an anti-forgery token (`SameSite=Strict` cookie) on every post, sign-out included | Journeys post real forms with the token |
| Cross-site scripting | The content security policy forbids inline script and style and allows only the site's own origin; output is encoded by the framework; success messages are fixed text looked up from a key, never echoed input | The page-load test fails on any policy violation |
| Open redirect after sign-in | A return address must be a path on this site; `//host`, `https://host`, `/\host` and anything containing a control character are refused | `A_sign_in_link_that_points_off_the_site_never_sends_anyone_there` |
| Clickjacking | `frame-ancestors 'none'` and no framing | Header tests |
| Information leaks in errors | Unexpected failures are a bare 500; expected ones are problem documents with no stack, type name or connection string | `An_unexpected_failure_is_a_500_problem_that_reveals_nothing...` (the deliberate-failure test) |
| Injection into the database | Entity Framework parameterises every query; no raw SQL is written | Reviewed; the lookup tests include SQL-shaped input |
| A secret in the repository | Secrets are never written: user secrets in development, environment variables elsewhere; sample sign-ins take their password at run time; the tests make a fresh one per run | A secret and name sweep over the full history before publication |
| A vulnerable dependency | A package with a known vulnerability fails the restore; Dependabot and CodeQL run on the public repository; every package is MIT, Apache-2.0 or BSD apart from reviewed, listed exceptions | CI; `scripts/license-audit.ps1` |
| Sample data that is someone's real data | The town, people and addresses are invented; sample addresses use the reserved `.test` domain | A test asserts the sample data has no `@` and no user rows |

## What is not done (and why it is acceptable here)

- **Timing differences** between "address taken" and "address new" in registration are not hidden. A determined
  attacker could measure them. Equalising them needs an email flow.
- **A locked account answers faster than a wrong password**, because the lockout is checked before the password is
  hashed. Someone who can time responses can tell a locked account from an unlocked one, and so that it exists. Closing
  that would mean doing the hash work for a locked account too; it is not done, and it is listed here so it is a known
  limit and not a surprise.
- **No multi-factor sign-in, no password reset, no email confirmation.** All need an email service; listed in
  [DEPLOYMENT.md](DEPLOYMENT.md).
- **No audit trail beyond each application's own history.** A refused request (not signed in, forbidden, hidden,
  rate limited, or a failed sign-in for a real account) leaves one log line with the account's id, the method, the route
  and the code, and never an address, password or token. Successful sign-ins are not logged by the application, and
  nothing is stored in a record that can be queried.
- **Rate limiting trusts the connecting address.** Behind a proxy it must be given forwarded headers it can trust
  ([DEPLOYMENT.md](DEPLOYMENT.md)); until then every client behind the proxy shares one budget.
- **No denial-of-service protection beyond the rate limits.** That belongs in front of the application.
- **Not penetration tested** by anyone but the author, and not independently reviewed for security beyond the pull
  request reviews.

## Reporting

See [SECURITY.md](../SECURITY.md). Findings and their fixes are recorded in [SECURITY_LEDGER.md](SECURITY_LEDGER.md).

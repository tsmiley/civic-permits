# The REST API

The API is versioned under `/api/v1`. The authoritative description is the OpenAPI document the running API serves at
`/openapi/v1.json` in Development (every operation has a name, a summary and its possible responses, and a test
enforces that). This page is the orientation: how to sign in, how errors look, and what each area does.

## Try it

```bash
export Database__SampleUserPassword="a password of at least fifteen characters"   # yours to choose; never written down
dotnet run --project src/CivicPermits.Api                                        # http://localhost:5210
```

In Development the API creates its SQLite database, loads the fictional Town of Bramblewick and, because a password
was given, creates the sample sign-ins (`applicant.one@bramblewick.test`, `clerk@bramblewick.test`,
`inspector.one@bramblewick.test`, `administrator@bramblewick.test`, and a few more on the reserved `.test` domain).

```bash
# Public: no sign-in
curl http://localhost:5210/api/v1/permit-types
curl http://localhost:5210/api/v1/public/permits/<a number from the sample data>   # type, status and dates only

# Sign in; the answer holds an access token and a refresh token
curl -X POST http://localhost:5210/api/v1/auth/login \
     -H "Content-Type: application/json" \
     -d '{"email":"clerk@bramblewick.test","password":"<the password you chose>"}'

# Use the access token
curl http://localhost:5210/api/v1/applications?status=Approved \
     -H "Authorization: Bearer <accessToken>"
```

## Signing in

| Call | What it does |
|---|---|
| `POST /auth/register` | Makes an applicant account. Answers 202 whether or not the address was already registered ([ADR 0011](adr/0011-registration-cannot-reveal-who-has-an-account.md)). |
| `POST /auth/login` | Returns `{ tokenType, accessToken, expiresIn, refreshToken }`. Five wrong passwords lock the account for fifteen minutes. |
| `POST /auth/refresh` | Exchanges a refresh token for a new pair; refused if the person's security stamp has changed. |
| `POST /auth/close-account` | Applicants only; body `{ "password": "..." }`. Removes the account's name and email, ends its sessions and refresh tokens and locks it for good; its applications are kept under the retention periods (answers 204; a wrong password is a 422 and counts toward the lockout). |
| `POST /auth/sign-out` | Needs a sign-in. Changes the account's security stamp, so no refresh token made before the call can be exchanged again (answers 204). An access token already issued keeps working until its 15 minutes are up. |
| `GET /auth/me` | Says who the access token belongs to and the role they act in. |

**Passwords** follow NIST SP 800-63B-4: at least 15 characters, any characters (spaces included) up to 256, no rule about
capitals, digits or symbols, and a refusal for a password on the bundled list of the ones people guess first or made only
of them, including a path across neighbouring keys (a 422 whose message says so). The list ships inside the program and no password leaves the host. A password hash
uses 250,000 PBKDF2-HMAC-SHA512 iterations.

Access tokens last 15 minutes and refresh tokens 8 hours. They are opaque and protected with the host's data-protection
keys ([ADR 0007](adr/0007-opaque-bearer-tokens-and-cookie-sessions.md)). Registration, sign-in and refresh are rate
limited per address.

## Errors

Every error is an [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem document with a stable `code` and the
request's correlation id (also in the `X-Correlation-Id` header):

```json
{
  "status": 409,
  "title": "Not possible now",
  "detail": "An application that is a draft cannot be approved.",
  "code": "workflow.transition_not_allowed",
  "correlationId": "7d3f0c1e9a2b4f6d8c5e1a0b3d9f2c47"
}
```

| Status | Meaning | Examples of `code` |
|---|---|---|
| 400 | The body could not be read | `request.malformed` |
| 401 | Not signed in | `request.unauthorized` |
| 403 | Signed in, but the role may not do this | `workflow.role_not_allowed`, `request.forbidden`, `inspection.not_assigned` |
| 404 | Not found, or not yours to know about | `application.not_found`, `permit.not_found`, `permit_type.not_found` |
| 409 | Right request, wrong moment, or a clash | `workflow.transition_not_allowed`, `issue.fee_unpaid`, `issue.inspection_not_passed`, `payment.already_recorded`, `request.concurrent_change`, `permit_type.name_in_use` |
| 422 | A value is out of range or missing | `address.postal_code`, `application.description`, `application.reason_required`, `permit_type.valuation_rate` |
| 429 | Too many requests; see `Retry-After` | |
| 500 | Something unexpected; nothing about it is revealed | |

## Who can do what

| Area | Endpoints | Who |
|---|---|---|
| Permit types | `GET /permit-types`, `GET /permit-types/{id}` | anyone |
| | `POST /permit-types`, `PUT /permit-types/{id}`, `POST /permit-types/{id}/open`, `/close` | administrators; each writes one entry to the audit record |
| Retention | `POST /retention/purge` (`?dryRun=true` to count only) | administrators; a real run writes one audit entry with the counts |
| Audit | `GET /audit` | administrators; read only: nothing in the API adds to, changes or removes an entry |
| Applications | `POST /applications`, `PUT /applications/{id}`, `POST .../submit`, `.../withdraw` | the applicant who owns it |
| | `POST .../start-review`, `.../request-corrections`, `.../approve`, `.../deny`, `.../issue`, `.../payment`, `.../inspections` | clerks and administrators |
| | `POST .../inspections/{id}/result` | the assigned inspector, or an administrator |
| | `POST .../inspections/{id}/cancel` | clerks and administrators |
| | `GET /applications`, `GET /applications/{id}` | applicants see their own, staff see all, inspectors see those with an inspection booked for them or carried out by them (a cancelled booking no longer counts) |
| | `GET /inspectors` | clerks and administrators (to choose one) |
| Public | `GET /public/permits/{number}` | anyone; rate limited; type, status and dates only |
| System | `GET /info`, `/health/live`, `/health/ready` | anyone |

Only the operations marked "anyone" above, plus sign-in, refresh, registration and the OpenAPI document, are open to a
person who is not signed in; every other address, including one that does not exist, answers a signed-out caller with
401. A test pins that list, so a new endpoint is closed until someone opens it on purpose. Each applicant step needs
the Applicant role and each staff step the Staff policy at the endpoint, as well as in the aggregate.

A step that the workflow does not allow in the application's current status is a 409 for the person whose step it is;
an applicant who does not own the application gets the same answer whatever the status. The full table is
in [ARCHITECTURE.md](ARCHITECTURE.md#the-workflow).

## What every response carries

Every response, including errors and the OpenAPI document, carries the security headers in
[DEPLOYMENT.md](DEPLOYMENT.md#headers-and-caching), and no `Server` header. Every response under `/api` also carries
`Cache-Control: no-store`, so a person's own applications never sit in a shared or browser cache.

## Money, dates and numbers

- Amounts are whole cents in fields named `...Cents`; there are no decimals on the wire.
- Moments are ISO 8601 in UTC.
- A permit number looks like `CP-2026-000042` and is given when an application is first sent; an application keeps it
  when it is sent back and sent again.

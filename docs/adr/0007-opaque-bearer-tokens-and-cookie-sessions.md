# 0007. Opaque bearer tokens for the API, cookie sessions for the site
Status: Accepted
Date: 2026-10-30

## Context
The API needs sign-in for other clients and the site needs sign-in for a browser. A signed token (a JWT) needs a
signing key, and a key in configuration is a secret that must be stored, rotated and kept out of the repository.

## Decision
- **API:** ASP.NET Core's bearer-token handler issues opaque, data-protected tokens: 15 minutes for access and 8 hours
  for refresh. There is nothing to sign with, so no key in configuration. Refresh re-checks the security stamp, so a
  changed password ends old sessions.
- **Site:** the Identity application cookie: `HttpOnly`, `SameSite=Lax`, `Secure` outside development, 8 hours sliding,
  with an anti-forgery token (`SameSite=Strict` cookie) on every post, sign-out included.
- Password length over composition rules (fifteen characters; raised from twelve on 2026-10-30, security ledger row 6; no forced symbols, a bundled blocklist of the passwords
  people guess first, as NIST SP 800-63B-4 asks); five failures lock the account for fifteen minutes; a wrong password, an
  unknown address and a locked account give one identical answer (an unknown address costs the same password check).

## Consequences
- No secret to manage for sign-in. The tokens are only as good as the host's data-protection keys; a deployment with
  more than one instance must share and protect those keys (see [DEPLOYMENT.md](../DEPLOYMENT.md)).
- An opaque token cannot be read by a client or by a third party, and cannot be verified without the server. That is a
  limitation for a service that wants other services to verify tokens; this one does not.
- There is no email confirmation and no password reset: both need an email service this project does not have. They
  are listed as production gaps.

## Alternatives
- **JWT access tokens.** Verifiable by others, but needs a signing key and a rotation story.
- **A hosted identity provider.** The right answer for production; out of scope for a self-contained demonstration.

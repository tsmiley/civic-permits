# 0011. Registration cannot reveal who has an account
Status: Accepted
Date: 2026-10-30

## Context
A registration form that says "that address is already registered" lets anyone test which email addresses have an
account. For a permit office that is a privacy leak about who has applied for what.

## Decision
Registering an address that is already taken is answered exactly like registering a new one: the API returns 202 and
the site redirects to sign-in with the same message. Both hosts register through one `RegistrationService`. Sign-in
gives one answer for a wrong password, an unknown address and a locked account. The public permit lookup answers a
malformed number and an unknown one identically.

## Consequences
- The responses cannot be used to enumerate accounts or permit numbers.
- A person who mistypes while registering an address that is already theirs gets no "you already have an account"
  hint. A production service would email the owner of the address, which needs an email service (a listed gap); until
  then the sign-in page's message is the only guidance.
- Timing differences between the two paths are not hidden; a determined attacker could still measure them. Noted in
  the threat model.

## Alternatives
- **Say that the address is taken.** Friendlier, and an enumeration oracle.
- **Require email confirmation first.** The right design with an email service.

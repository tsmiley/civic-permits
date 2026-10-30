# 0010. No foreign key from an application to its applicant
Status: Accepted
Date: 2026-10-30

## Context
An application belongs to a person who signs in with an ASP.NET Core Identity account. A foreign key from the
applications table to the users table would give referential integrity.

## Decision
`PermitApplication.ApplicantId` is an indexed identifier with no foreign key. The domain does not know about Identity,
and the sample data is deliberately about people who do not exist: the sample applications refer to bare identifiers,
and sample sign-ins are created only when a password is supplied at run time.

## Consequences
- The domain and the permit tables stand alone; the identity store can be replaced without touching them.
- Nothing in the database stops an application pointing at an identifier that has no user. The application layer only
  ever writes the signed-in person's own identifier, and the sample data is seeded before or without users.
- Deleting a user does not remove or orphan-check their applications. A real office would not delete users who have
  applications (records are kept); a production deployment would add a soft-delete rule rather than a cascade.

## Alternatives
- **A foreign key with restrict.** Stronger integrity, but couples the permit schema to Identity and forces the sample
  data to create users with passwords, which the project refuses to commit.

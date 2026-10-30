# Security policy

This is a portfolio project and is not deployed anywhere. It holds no real data.

## Reporting a problem

If you find a security problem in the code, please report it privately through GitHub's **Report a vulnerability**
button on the Security tab of this repository (private vulnerability reporting), rather than opening a public
issue. Expect an acknowledgement within a few days. There is no bug bounty.

## What the project does about security

A fuller description (threat model, authentication and authorization design, and the checks run before each
release) is written with the documentation slice. Until then, the standing practices are:

- Secrets never enter the repository: user secrets in development, environment variables elsewhere.
- Warnings are build errors and a package with a known vulnerability fails the build.
- Security headers on every response, and error responses that never reveal exception details.
- Input is validated at the boundary and invariants are enforced in the domain.

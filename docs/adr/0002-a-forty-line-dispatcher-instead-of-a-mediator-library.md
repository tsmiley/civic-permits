# 0002. A forty-line dispatcher instead of a mediator library
Status: Accepted
Date: 2026-10-30

## Context
Each use case is a request type and a handler, so the hosts need a way to send a request to its one handler. Popular
mediator libraries do this, and also bring pipelines, notifications, licensing and upgrade concerns that this project
does not need. Some have moved to commercial licenses; the project accepts only MIT, Apache-2.0 and BSD packages.

## Decision
`Dispatcher` resolves the handler for a request from the container and invokes it. The generic plumbing is built once
per request type and cached. Handlers are registered by scanning the assembly, so adding one needs no registration.
The whole mechanism is a few dozen lines that a reviewer can read in a minute.

## Consequences
- No dependency, no license or upgrade risk, and nothing to learn but the code.
- A request with no handler fails with a message that names the request, never a silent no-op.
- There are no pipeline behaviours (validation, logging, transactions). Cross-cutting concerns live where they belong
  instead: validation in the domain, the transaction in the unit of work, logging and correlation in the host.
- If pipelines were ever needed, they would be added here, in the open.

## Alternatives
- **A mediator library.** Rejected for the reasons above.
- **Calling handlers directly through interfaces.** Simpler still, but every host would then list the handlers it
  uses; the dispatcher keeps the hosts ignorant of which class answers a request.

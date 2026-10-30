# 0006. The web site calls the application layer directly
Status: Accepted
Date: 2026-10-30

## Context
The Blazor site and the REST API both need the same use cases. The site could call the API over HTTP, as a separate
client would, or call the application layer in its own process.

## Decision
The site sends requests to the dispatcher in the same process, with the signed-in person taken from the cookie
session. The API exists for other clients and is kept honest by its own integration tests.

## Consequences
- One set of rules and one place that decides who may see what; the site cannot drift from the API.
- No tokens in the browser, no second round trip, and no duplicated error mapping: the site turns a refused request
  into a message at a field, the API into a problem document, from the same two exception types.
- The site and the API are deployed together or at least built from the same code; they are not independent clients of
  each other. A deployment that wanted a separately hosted front end would put the API between them, and this decision
  would be superseded.

## Alternatives
- **The site as a client of the API.** The better separation for a separately hosted front end, but it doubles the
  surface (tokens, CORS, error translation) for no gain here.

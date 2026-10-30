# Contributing

This is a personal portfolio project, so it is not seeking outside contributions. The rules below are how the
work itself is done, written down so a reader can see the discipline.

## Every change is a pull request

`main` accepts changes only through a reviewed pull request. A pull request is one slice that can be reviewed on
its own, with its tests, and its description states the evidence: what was built, the test results, and the
format and inspection results.

## Before pushing

Continuous integration runs the same checks on every pull request and every push to `main` (and CodeQL runs
beside it), but run them first.

```bash
dotnet build                          # warnings are errors
dotnet test tests/CivicPermits.Domain.Tests   # run each test project in turn (see the README); all must pass
dotnet format --verify-no-changes     # formatting and style
pwsh scripts/inspect.ps1              # JetBrains InspectCode: no warnings in the code
pwsh scripts/license-audit.ps1        # every package is MIT, Apache-2.0 or BSD (or a reviewed exception)
```

## Rules

- **Original work only.** Nothing is copied or adapted from any employer, client or other private project. Every
  pull request description repeats this.
- **No secrets, ever.** Development secrets live in `dotnet user-secrets`; anything else comes from environment
  variables. A secret scan runs over the full history before the repository is made public.
- **No real people or data.** Sample data is fictional and generated; it is never a real person, address, email
  or customer.
- **Dependencies:** MIT, Apache-2.0 or BSD only, each listed in `NOTICE`. A vulnerable package fails the build.
- **Accessibility is a requirement, not a polish step.** A page is not done until it is keyboard-operable, labelled,
  and passes the automated checks.
- **Claims are tested.** The documentation says "tested against WCAG 2.1 AA", never "compliant" or "certified",
  unless a formal audit supports it.

## Documentation is checked too

The workflow diagram in `docs/ARCHITECTURE.md` is drawn from the code by a test; if you change the workflow table,
regenerate it with `CP_UPDATE_DOCS=1 dotnet test tests/CivicPermits.Domain.Tests --filter DocumentationTests`. The
screenshots are regenerated, never edited: `CP_SCREENSHOTS=1 dotnet test tests/CivicPermits.Web.A11yTests --filter ScreenshotTests`.
A decision that shaped the design gets a short record in `docs/adr/`.

## Commit messages

A short imperative subject (about 70 characters), then a body that says why when the reason is not obvious.

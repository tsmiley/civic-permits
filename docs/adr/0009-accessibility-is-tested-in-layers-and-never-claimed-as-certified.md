# 0009. Accessibility is tested in layers and never claimed as certified
Status: Accepted
Date: 2026-10-30

## Context
Accessibility is the feature this project is meant to show, and it is the area where over-claiming is most common.
Automated tools find a minority to about half of the problems; most success criteria need a person using assistive
technology. A "compliant" badge that no audit supports is worse than none.

## Decision
Three layers, each saying what it can and cannot show:
1. **An axe-core sweep** of the page states the tests list (53 at the time of writing: the pages of the site, in the states a test could reach, as each role that can reach them), with a canary that proves it detects
   defects, run on every change.
2. **Scripted browser tests** for what a tool cannot judge: keyboard order, a visible focus indicator on every stop,
   reflow, text spacing, forced colours, and the behaviour of error handling.
3. **A manual procedure** with assistive technology, with a results log that is empty until someone does it.

The documentation says "tested against WCAG 2.1 AA". It never says "compliant", "conformant" or "certified". The report
lists every success criterion with a status that separates *verified by a machine*, *designed in*, *not applicable* and
*known gap*, and counts "designed in" as unverified.

## Consequences
- The claim is limited to what the evidence supports, and the evidence is in the repository.
- The most valuable testing (people using assistive technology) is named as not yet done. That is uncomfortable and
  correct.
- Tests run under the site's real content security policy, so the pages cannot rely on inline script or style, and a
  blocked resource fails a test.

## Alternatives
- **Automated testing only, and say "accessible".** Rejected: it would overstate what was shown.
- **Wait for an audit before saying anything.** Rejected: honest, specific statements about what was done are useful now.

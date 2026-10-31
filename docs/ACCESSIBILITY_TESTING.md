# Accessibility testing

How Civic Permits is tested for accessibility, and how to repeat and extend that testing. The results are in
[ACCESSIBILITY_REPORT.md](ACCESSIBILITY_REPORT.md).

The target is WCAG 2.1 level AA. That is a target the site is tested against, not a claim of conformance or
certification: automated tools can check only a part of the success criteria, and the part that needs a person
using assistive technology is recorded in this repository as a procedure and a results log, not asserted.

## 1. Automated tests (run on every change)

All of them drive the real site in a real browser (the Chrome or Edge already installed on the machine; nothing is
downloaded) with the project's own content security policy in force, so a blocked script or style fails the test.

```bash
dotnet test tests/CivicPermits.Web.A11yTests
```

Set `CP_BROWSER_CHANNEL` to use another Playwright channel (for example `msedge` or `chrome-beta`). With no browser
the tests skip with a message that says so; they never pass silently.

| Test class | What it checks |
|---|---|
| `AxeSweepTests` | The axe-core engine (unmodified, through Deque.AxeCore.Playwright) over the 57 page states listed in the report (the pages of the site in the states the tests reach, as each kind of person who can reach them), at WCAG 2.0 and 2.1 A and AA plus axe's best-practice rules. A state that is not on the list has not been scanned. A canary test puts known defects into a real page and requires axe to find each one, so a clean result means something. |
| `SiteJourneyTests` | Whole journeys done the way a person does them, by label and role: applicant, clerk, inspector and administrator paths; page structure (language, a title of its own, one `h1`, landmarks, a name for every control, unique ids, no skipped heading level); error summary focus and field links; keyboard-only sign-in and tab order; sign-in return addresses; 404 and 403 pages; reflow at 320 px and at 200% text; 44 px targets; no console error or failed request. |
| `DisplayAdaptationTests` | What axe cannot judge: a visible focus indicator on every keyboard stop (with a canary that removes the indicator and must be noticed), controls keeping a boundary when the system forces its own colours, and text still fitting when its spacing is overridden to the WCAG 1.4.12 values. |
| `HomePageStructureTests` | The first checks written: structure, skip link behaviour, policy compliance, a real 404. |

To add a page or a state, add it to the sweep in `AxeSweepTests` and bump the expected count: the count is asserted so
a state cannot be dropped silently.

## 2. Manual procedure (to be repeated before each release)

Do this with the site running with sample data (see the README). Use a real keyboard and real assistive technology.
Record each run in the results log in section 4. A page or state passes only when every pass criterion holds.

### 2.1 Keyboard only (no mouse, no touch)
Do the four journeys in the README's "The web site" table with the keyboard alone.
- Every control can be reached with Tab and Shift+Tab, in an order that matches the reading order.
- Every control can be used: links and buttons with Enter, buttons and checkboxes with Space, radios and selects with
  the arrow keys.
- Focus is always visible, and never lost or trapped. After a refused form post, focus is on the error summary.
- The skip link is the first stop, and after it Tab continues from the main content.

### 2.2 Screen reader
A short version that one person can do in twenty minutes with Windows Narrator, with a table to fill in and date, is in
[MANUAL_CHECK_NARRATOR.md](MANUAL_CHECK_NARRATOR.md). Start there; the list below is the fuller version.

Windows: NVDA with Firefox and with Chrome (and JAWS if available). macOS and iOS: VoiceOver with Safari. Android:
TalkBack with Chrome. For each of the pages below, with the screen reader's speech on:
- The page title and the main heading are announced on arrival, and say where you are.
- Landmarks and headings give a usable outline (use the landmark and headings lists).
- Every field is announced with its label, whether it is required, its hint, and its error when it has one.
- The error summary is announced when a form comes back refused, and each of its links moves to the field.
- Status messages (a saved draft, a decision recorded) are announced without moving focus.
- Tables are announced with their caption, and cells with their column and row headers.
- Statuses are understood from words alone (turn off colour or use a monochrome filter to check).
- In the three-step journey the current step is announced as the current step.
Pages and states: home; permits; lookup (empty, found, not found); register and sign in (including the refused state);
choose a permit (including nothing chosen); project details (including everything missing); an application in each
status as an applicant and as a clerk; booking an inspection; recording an inspection result; permit type editing.

### 2.3 Zoom, reflow and spacing
- Browser zoom to 400% at a 1280 px window (equivalent to 320 CSS px): no content lost, no sideways scrolling except
  inside a table's own region, which must be reachable and scrollable with the keyboard.
- Text size only to 200% (the browser's text-only zoom): nothing clipped or overlapping.
- Apply a text-spacing bookmarklet (line height 1.5, letter spacing 0.12em, word spacing 0.16em, paragraph spacing 2em):
  nothing clipped or overlapping.
- Landscape and portrait on a phone: nothing locked to one orientation.

### 2.4 Colour, contrast and display settings
- Windows Contrast themes (high contrast) and macOS Increase Contrast: every control and its state remain visible.
- Dark mode and light mode on the system (the site has one colour scheme; it must stay readable).
- Greyscale: every status and error is still understood.
- Reduced motion on: nothing moves.

### 2.5 Voice control
Dragon or Voice Control: say the visible name of each link and button ("click Sign in", "click Send application"): it
activates the right one. This checks that the accessible name contains the visible label (WCAG 2.5.3).

### 2.6 Content and forms (a person reading, not a tool)
- Every instruction, hint and error is in plain words and says what to do, not only what is wrong.
- A mistake can be corrected without re-entering everything that was right.
- Nothing is lost on a refused post, and nothing is done by accident: sending, withdrawing and issuing are separate,
  named buttons.
- A person who is slow, uses a switch, or comes back after a break is not penalised: note what happens at the end of
  the session (see the known gaps in the report).

## 3. When a defect is found
1. Record it in the results log with the page, the state, the technology and what happened.
2. Fix it in the page or the shared component, not around it.
3. Add a test that fails without the fix (in the sweep, the journeys or the adaptation tests), or add a step to this
   procedure when only a person can judge it.
4. Update the report.

## 4. Results log (manual runs)
| Date | Tester | Technology and version | Browser | Page and state | Result | Notes |
|---|---|---|---|---|---|---|
| | | | | | | No manual assistive-technology run has been recorded yet. |

# Accessibility report

**Status in one paragraph.** Civic Permits is built and tested against WCAG 2.1 level AA. The 57 page states listed in
section 1 (the pages of the site, in the states a test could reach, as each kind of person who can reach them) pass the
axe-core automated rules with no violation; a state not on that list has not been scanned. A set of scripted browser
tests covers the things automated rules cannot
judge, such as keyboard order, visible focus, reflow, text spacing and the behaviour of error handling. **No manual
test with a screen reader or other assistive technology has been recorded yet**, no third-party audit has been done,
and no real user with a disability has tested the site. This report therefore makes **no claim of WCAG conformance,
Section 508 conformance or certification**, and it says which criteria are verified, which are only designed in, and
what remains. The procedure for the manual testing is in [ACCESSIBILITY_TESTING.md](ACCESSIBILITY_TESTING.md).

## 1. What was tested

**Site under test:** the Blazor web site in this repository, run with the fictional sample data, in Chrome (the
installed stable channel on the development machine), with its real content security policy.

**Automated, every change:** `dotnet test tests/CivicPermits.Web.A11yTests` (47 browser tests at the time of writing).

| Group | States scanned with axe-core |
|---|---|
| Public (14) | home; permits; lookup (empty, not found, found); sign in (plain, after registering, wrong password); register (plain, nothing entered, weak password); page not found; status 429 page; account closed |
| Applicant (19) | my applications; choose a permit (plain, nothing chosen); project details (plain, everything missing); a draft; change a draft (plain, a problem); an application that is approved with an inspection booked, issued, approved and paid, submitted, denied, in review, sent back for corrections, withdrawn; the "not allowed" (403) page; close my account (plain, wrong password) |
| Staff and inspector (13) | all applications; filtered by status; a submitted application (start review); in review (approve, deny, ask for changes; deny without a reason); approved and unpaid with an inspection booked; approved with a failed inspection (book another; booking in the past); approved and paid (issue); issued; an inspector's list; recording a result (plain, without a choice) |
| Administrator (5) | permit types; add a permit type (plain, everything missing); change a permit type; the audit record |
| 320 px screen (6) | home; permits; lookup; sign in; the clerk's list; an application |

**Engine:** axe-core 4.13.0 (as the engine reports itself), used unmodified through Deque.AxeCore.Playwright 4.13.0.
Rules: WCAG 2.0 and 2.1 A and AA, plus axe's best-practice rules. A canary test injects an image without a text
alternative, an unnamed button, an unlabelled field and an unnamed link into a real page and requires axe to report
every one, so a clean sweep cannot be a sweep that detects nothing.

**Scripted browser tests** (`SiteJourneyTests`, `DisplayAdaptationTests`) cover: the structure of every public page
(language, a title of its own, one `h1`, banner, main and footer landmarks, a name for every control, unique ids, no
skipped heading level, a name for every link and button); the whole applicant, clerk, inspector and administrator
journeys; error summaries, focus and field links; keyboard-only sign-in and its tab order; refusal of off-site return
addresses; real 404 and 403 pages; reflow at 320 px (and the tables' own scrolling regions); 200% text; 44 px
interface parts; a visible focus indicator on every keyboard stop; boundaries kept under forced colours; text
spacing overrides; and no console error or failed request.

## 2. Results

- **axe-core:** 0 violations in 57 states.
- **Scripted tests:** all pass (see the test run for the current count).
- **Defects found by this testing and fixed** (each has a test that fails without the fix):
  1. *In-page links went to the home page.* The document has a `<base href="/">`, so `#main` and `#field` resolved
     against it. The skip link and the error summary's links now carry the page's own address.
  2. *The first Tab skipped the skip link.* A framework helper focused the page heading on every first load, so the
     keyboard's starting point moved past the skip link. It was replaced with a small script that leaves a first load
     alone and moves focus only after a refused form or an in-page navigation. The earlier structure test had passed
     only by accident (nothing focusable followed the heading).
  3. *Error summary links did not move focus.* Blazor handles same-page links itself and scrolls without focusing; a
     script now moves focus to the field.
  4. *The skip link sat outside every landmark* on a long page at 320 px (axe `region`). It now sits first inside the
     banner.
- The tests also caught a start-up failure unrelated to accessibility (the sign-in services were not registered for the
  site), which is why every page is exercised on each change.

## 3. WCAG 2.1 level A and AA, criterion by criterion

How to read the status column:

- **Automated**: an axe rule covers it and passes in all 57 states.
- **Scripted**: a written browser test covers it (named in the notes).
- **Designed**: built in and reviewed in code and markup, but only a person with assistive technology can confirm it;
  the check is in the manual procedure and has **not yet been done**.
- **Not applicable**: the site has no such content.
- **Known gap**: the criterion is not fully met; see section 5.

A criterion can carry more than one status. "Designed" is never counted as verified.

**Of the 50 criteria at levels A and AA:** 13 are not applicable (no media, gestures, motion or hover content); 24 have
at least one automated or scripted check (several also depend on a manual confirmation, as noted); 12 are designed in
and verified by nothing yet except review; 1 is a known gap.

| SC | Name | Level | Status | How it is addressed, and the evidence |
|---|---|---|---|---|
| 1.1.1 | Non-text Content | A | Automated | No content images; the only image is the icon. axe `image-alt` and related rules pass; the canary proves they run. |
| 1.2.1 | Audio-only and Video-only | A | Not applicable | No audio or video. |
| 1.2.2 | Captions (Prerecorded) | A | Not applicable | No media. |
| 1.2.3 | Audio Description or Media Alternative | A | Not applicable | No media. |
| 1.2.4 | Captions (Live) | AA | Not applicable | No media. |
| 1.2.5 | Audio Description (Prerecorded) | AA | Not applicable | No media. |
| 1.3.1 | Info and Relationships | A | Automated, Scripted, Designed | Real headings, landmarks, lists, `dl` for facts, tables with captions and `scope`, `fieldset`/`legend` for groups, labels tied to controls, hints and errors tied with `aria-describedby`. axe `label`, `th-has-data-cells`, `scope-attr-valid`, `list`, `definition-list`, `landmark-*`; structure tests. Announcement by a screen reader unconfirmed. |
| 1.3.2 | Meaningful Sequence | A | Scripted, Designed | One column; DOM order is reading order; the grid is used only for cards. Tab-order tests. |
| 1.3.3 | Sensory Characteristics | A | Designed | Instructions use words ("the button below" is not used; buttons are named by what they do). |
| 1.3.4 | Orientation | AA | Designed | No orientation lock; layout reflows. Landscape check is in the manual procedure, not yet done. |
| 1.3.5 | Identify Input Purpose | AA | Automated | `autocomplete` tokens on the sign-in and register fields (`username`, `current-password`, `new-password`, `name`, `email`); axe `autocomplete-valid`. Site-address fields are not the person's own details and carry none. |
| 1.4.1 | Use of Color | A | Scripted, Designed | Status is always written ("Approved"); errors carry text and a heavier border; links are underlined; the current navigation item is bold and marked `aria-current`. axe `link-in-text-block` passes. |
| 1.4.2 | Audio Control | A | Not applicable | No audio. |
| 1.4.3 | Contrast (Minimum) | AA | Automated | axe `color-contrast` passes in all 57 states; ratios are also computed with the WCAG formula and written beside each colour in `app.css` (text 8:1 or better; focus ring 4.7:1). |
| 1.4.4 | Resize Text | AA | Scripted | `Text_can_be_enlarged_to_200_percent_without_losing_content`; axe `meta-viewport` passes (zoom is not blocked). |
| 1.4.5 | Images of Text | AA | Designed | No images of text. |
| 1.4.10 | Reflow | AA | Scripted | `Public_pages_reflow_to_a_320_pixel_screen...`, `Signed_in_pages_with_tables_reflow...`, and the 320 px axe sweep. Tables scroll inside their own focusable region, which the criterion allows for data tables. |
| 1.4.11 | Non-text Contrast | AA | Scripted, Designed | Field borders 4.54:1, focus ring 4.72:1 (computed). Boundaries kept under forced colours is tested. Not every state of every control is measured by a tool. |
| 1.4.12 | Text Spacing | AA | Scripted | `Text_still_fits_when_the_person_overrides_its_spacing` (WCAG values, set through the style object because the policy forbids inline style). Tested on one form page. |
| 1.4.13 | Content on Hover or Focus | AA | Not applicable | Nothing appears on hover or focus; no `title` tooltips. |
| 2.1.1 | Keyboard | A | Scripted, Designed | All controls are native elements. `Signing_in_can_be_done_with_the_keyboard_alone...` and the tab-stop tests. The other journeys were driven with pointer-style clicks, so a full keyboard-only pass of each is in the manual procedure, not yet done. |
| 2.1.2 | No Keyboard Trap | A | Scripted | The focus-stop tests tab through each page until focus returns to the start; no custom widgets. |
| 2.1.4 | Character Key Shortcuts | A | Not applicable | No single-key shortcuts. |
| 2.2.1 | Timing Adjustable | A | Known gap | The session expires after 8 hours without activity, with no warning or way to extend. Section 5. |
| 2.2.2 | Pause, Stop, Hide | A | Not applicable | Nothing moves, blinks or updates by itself. |
| 2.3.1 | Three Flashes or Below Threshold | A | Not applicable | Nothing flashes. |
| 2.4.1 | Bypass Blocks | A | Automated, Scripted | Skip link, first Tab stop, moves focus to `main`; landmarks. axe `bypass`, `region`; `The_first_tab_stop_is_the_skip_link...`. |
| 2.4.2 | Page Titled | A | Automated, Scripted | A title of its own on every page, prefixed "Error:" when a form is refused. axe `document-title`; `Every_page_has_a_title_of_its_own`. |
| 2.4.3 | Focus Order | A | Scripted | Tab-order and focus-stop tests; focus moves to the error summary after a refused post. |
| 2.4.4 | Link Purpose (In Context) | A | Automated, Designed | axe `link-name`. Repeated links carry hidden text ("Apply for Residential building permit", "Next page (page 2)"). |
| 2.4.5 | Multiple Ways | AA | Designed | Main navigation, the permit list, and the lookup by number. |
| 2.4.6 | Headings and Labels | AA | Scripted, Designed | One `h1`, no skipped level, a label for every control. Whether the wording is descriptive is a review judgement; not independently reviewed. |
| 2.4.7 | Focus Visible | AA | Scripted | `Every_keyboard_stop_shows_where_focus_is` (a 3 px outline on every stop of four pages) with a canary that removes the indicator and must be noticed. |
| 2.5.1 | Pointer Gestures | A | Not applicable | No path-based or multi-point gestures. |
| 2.5.2 | Pointer Cancellation | A | Designed | Only native links and buttons, which act on release. |
| 2.5.3 | Label in Name | A | Scripted, Designed | The tests find every control by its visible label. Voice control is in the manual procedure, not yet done. |
| 2.5.4 | Motion Actuation | A | Not applicable | No motion-triggered function. |
| 3.1.1 | Language of Page | A | Automated, Scripted | `lang="en"`; axe `html-has-lang`, `html-lang-valid`. |
| 3.1.2 | Language of Parts | AA | Not applicable | English only. |
| 3.2.1 | On Focus | A | Designed | Receiving focus never changes the page. (Focus is moved by the site only after a post or an in-page navigation, never by the person focusing something.) |
| 3.2.2 | On Input | A | Designed | No control submits on change; every filter and form has its own button. |
| 3.2.3 | Consistent Navigation | AA | Designed | One layout and one navigation on every page. |
| 3.2.4 | Consistent Identification | AA | Designed | The same shared components, with the same labels and wording, throughout. |
| 3.3.1 | Error Identification | A | Scripted | An error summary and a message at each field, `aria-invalid`, focus on the summary, links to the fields (`A_wrong_password_puts_focus...`, `Starting_an_application_asks_for_every_missing_thing_at_once...`). |
| 3.3.2 | Labels or Instructions | A | Automated, Scripted | A visible label on every control, hints, and "(required)" in words; axe `label`. |
| 3.3.3 | Error Suggestion | AA | Designed | Messages say how to put it right ("Enter a five-digit postal code", "Enter an amount in dollars, like 24000 or 24,000.50"). Wording not independently reviewed. |
| 3.3.4 | Error Prevention (Legal, Financial, Data) | AA | Designed | An application is checked on a review page before it is sent; withdrawing, deciding and issuing are separate, named buttons. The applicant makes no payment through the site. |
| 4.1.1 | Parsing | A | Automated, Scripted | axe `duplicate-id`; the structure test checks unique ids. (This criterion is obsolete in WCAG 2.2.) |
| 4.1.2 | Name, Role, Value | A | Automated, Designed | Native elements throughout; axe `aria-*`, `button-name`, `label`, `select-name` and related rules. |
| 4.1.3 | Status Messages | AA | Designed | A saved draft or a recorded decision appears in a `role="status"` region; the error summary is `role="alert"`. The tests assert the roles; announcement by a screen reader is unconfirmed. |

## 4. Section 508

The Revised Section 508 Standards (2017) require web content to meet WCAG 2.0 level A and AA, and add functional
performance criteria. WCAG 2.1 level AA includes every WCAG 2.0 A and AA criterion, so this testing is aimed at the
same target. No accessibility conformance report (VPAT) has been produced, and this is not a Section 508 conformance
claim.

## 5. Known gaps and what remains

1. **No manual assistive-technology testing is recorded.** Every "Designed" row above depends on it. The procedure is
   written, including a [twenty-minute Narrator check](MANUAL_CHECK_NARRATOR.md) that one person can do and date; the
   results log is empty.
2. **Session timeout (2.2.1).** A session ends after 8 hours without activity and the person is not warned. The
   remedy is a warning with a way to extend, or a longer limit.
3. **The date-and-time control** for booking an inspection is the browser's own control, whose accessibility varies
   by browser and assistive technology. Times are entered and shown in UTC, which the label states; the town's time
   zone is not used.
4. **Error summary focus needs JavaScript.** Without script the summary is still the first block under the heading,
   but it is not announced on arrival.
5. **Focus after an in-page navigation** (moving to the new page's heading) is implemented but not asserted by an
   automated test.
6. **Plain-language level** of the wording has not been measured or reviewed by a plain-language specialist.
7. **One colour scheme.** There is no dark mode, and no print stylesheet.
8. **Axe finds a minority to about half of accessibility problems.** A clean sweep is necessary, not sufficient.
9. **One browser.** The automated tests run in Chrome. Firefox and Safari are in the manual procedure only.

## 6. Reporting a problem

An accessibility barrier on this site is a defect. Please open an issue on the repository describing the page, what
you were trying to do, and the browser and assistive technology you used.

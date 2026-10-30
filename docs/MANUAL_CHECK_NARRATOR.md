# A twenty-minute check with Windows Narrator

This is the shortest useful manual accessibility check: one person, one Windows computer, the Narrator screen reader
that comes with Windows, and twenty minutes. It does not replace the fuller procedure in
[ACCESSIBILITY_TESTING.md](ACCESSIBILITY_TESTING.md), and it does not make the site "accessible" or "compliant": it
records, with a date and a name, what a screen reader says on the pages that matter most. Until a table below has rows in
it, the [accessibility report](ACCESSIBILITY_REPORT.md) says that **no manual assistive-technology run is recorded**,
and that statement stays true.

You do not need to be a programmer. You need to listen, and to write down what you hear.

## 1. Get ready (about 4 minutes)

1. Open a terminal in the folder where the repository is (for example, in File Explorer open the `civic-permits`
   folder, click the address bar, type `powershell` and press Enter).
2. Choose a password of at least fifteen characters for the practice accounts. It is only for this run and is never
   saved. Then start the site (two lines, one after the other):

   ```powershell
   $env:Database__SampleUserPassword = "type-your-chosen-password-here"
   dotnet run --project src/CivicPermits.Web
   ```

3. Wait until the terminal says the site is listening, then open **Microsoft Edge** (or Chrome) and go to
   `http://localhost:5211`. Leave the terminal open; close it with Ctrl+C when you finish.
4. Turn Narrator on with **Windows key + Ctrl + Enter**. (Turn it off the same way.) Narrator's own key is **Caps Lock**
   (or Insert); written below as **Narrator**. If any key below does not do what is described, press **Narrator + F1** to
   see Narrator's commands, and note in the table which key you used instead.
5. In the browser, press **Narrator + Space** to switch *scan mode* on, so single letters move through the page:
   **H** next heading, **D** next landmark, **K** next link, **B** next button, **F** next form field, **T** next table
   (add Shift to go backwards). Press **Tab** to move through links and fields as a keyboard user does.

Practice accounts (password: the one you chose):

| Role | Sign in with |
|---|---|
| Applicant | `applicant.two@bramblewick.test` |
| Clerk | `clerk@bramblewick.test` |

## 2. The check (about 15 minutes)

For each step, do what it says, listen, and write **Pass**, **Fail** or **Unsure** in the results table, with a short note
of what Narrator actually said when it is not what is expected. Do not try to fix anything: record it.

| # | Do this | You should hear |
|---|---|---|
| 1 | Open the home page `http://localhost:5211`. | The page title ("Civic Permits"). Press **H**: the main heading "Civic Permits", then headings for each panel, in order. Press **D**: the landmarks (banner, navigation, main, footer) one by one. |
| 2 | Reload the page. Press **Tab** once. | "Skip to main content", as a link. Press **Enter**: focus moves into the main part of the page, and **Tab** then goes to the first link in the main content, not back to the top. |
| 3 | Go to **Permits** (link in the navigation). Press **H** several times. | A heading for each category ("Building permits", and so on), then a heading for each permit. Each "Apply" link is read as "Apply for" and the permit's name, not just "Apply". |
| 4 | Go to **Check a permit**. Press **F** to reach the field. | "Permit number", that it is required, and the hint ("For example CP-2026-000042"). Type `CP-2026-999999`, press **Enter**. Narrator reads the page title starting "Error:" and moves to "There is a problem". Press **Tab**, then **Enter** on the link: focus goes to the permit number field, and it says the error message with it. |
| 5 | Go to **Sign in**. Without typing anything, press the **Sign in** button. | "There is a problem", then a list with one or two messages. **Tab** to a message link and press **Enter**: the matching field is focused and its name and error are read. |
| 6 | Sign in as the **applicant**. Listen to the page that opens ("My applications"). Press **T**. | A table, with its caption ("My applications, newest first. 4 in all"). Move across a row: each cell is read with its column name (Application, Permit, Status, and so on). The status is spoken as a word ("Approved"), not only shown in a colour. |
| 7 | Choose **Start a new application**. Listen. | "Step 1 of 3 Choose a permit", marked as the current step. Press **F**: a group called "Which permit do you need?" with radio buttons; each is read with its name, and its hint (description and fee) is read too. |
| 8 | Choose any permit, then **Continue**. On "Project details", press the **Save and continue** button without typing. | "There is a problem" and a list of messages (about six). Each message link takes you to its own field. Each field, read again, says it is required and says its error. |
| 9 | Sign out (link in the navigation, then the **Sign out** button). Sign in as the **clerk**. Open any application whose status is **In review** (use the status filter, "Show", then the **Show applications** button). | A heading "Application CP-…", then "Status: In review". Press **H**: "What you can do", then a heading for each action ("Approve", "Ask for changes", "Deny"). |
| 10 | On that page, press the **Deny application** button with the reason box empty. | "There is a problem"; a message that a reason is needed, and the link takes you to the reason box. Type a reason and press **Deny application**. After the page reloads, Narrator reads "The application is denied." **without** you having to look for it (it is announced as a status message). |
| 11 | Press **T** on the same page. | The "History" table: each row begins with the date and time, and the cells are read with their column names. The step you just took is the last row. |
| 12 | Turn scan mode off (**Narrator + Space**) and press **Tab** through the whole page once from the top. | Every stop says what it is (link, button, edit, radio button) and has a name. Nothing is read as just "button" or "link" with no name. You can always tell where you are. |

## 3. Results

Copy these rows and fill them in, one per step. Keep the table in this file (a copy of the whole file is fine) and send it
back, or paste the rows into the results log in [ACCESSIBILITY_TESTING.md](ACCESSIBILITY_TESTING.md#4-results-log-manual-runs).

Run details: date `____-__-__`, tester `__________`, Windows version `__________` (Settings, System, About), Narrator
version `__________` (if shown), browser and version `__________`.

| # | Pass / Fail / Unsure | What Narrator said, if it was not what is expected |
|---|---|---|
| 1 | | |
| 2 | | |
| 3 | | |
| 4 | | |
| 5 | | |
| 6 | | |
| 7 | | |
| 8 | | |
| 9 | | |
| 10 | | |
| 11 | | |
| 12 | | |

## 4. What happens next

Whoever records the run commits the filled-in table (or sends it to the developer, who does). The accessibility report
then changes from "no manual run recorded" to "one run recorded on *date*, with these results", every **Fail** and
**Unsure** becomes an issue with a test or a fix, and the report's "Designed" rows that this run covers are updated to say
what was heard. One run with one screen reader on one browser is a start, not a conclusion: the report will say so.

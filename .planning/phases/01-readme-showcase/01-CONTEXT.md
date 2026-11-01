# Phase 1: README Showcase - Context

**Gathered:** 2026-11-01
**Status:** Ready for planning

<domain>
## Phase Boundary

The README gets three generated images: the main job list, the job editor for an FDM job, and page 1 of a sample
invoice. One command rebuilds all three offscreen from sample data, and the README explains how to run it.
Requirements: SHOW-01 to SHOW-05.

Not in this phase: screenshots of the printers / filaments / settings screens, any change to app behavior, and
render-comparison tests for invoices (that is v2, FIX2-06, though it may later reuse this generator).

</domain>

<decisions>
## Implementation Decisions

### Sample data
- **D-01:** Extend the existing `SampleShop` (`tests/PrintInvoicer.UiTests/SampleShop.cs`) instead of creating a
  separate set of showcase data. The tests and the README images use the same data. Existing UI tests must still
  pass. Check `Job_list_totals_follow_the_filters` and the other `ScreenTests` asserts after any change to the data.
- **D-02:** The job list shows 3 to 4 jobs, covering draft, finalized and paid. Today SampleShop has an FDM draft, a
  resin draft and a finalized FDM job, but nothing paid. Add a paid state, either by marking a job paid or by adding a
  4th job, whichever disturbs the existing tests less.
- **D-03:** The editor image and the invoice image both feature the FDM "Wire Brush Inserts" job (the Bambu file, the
  friends-and-family discount, customer-supplied filament), because it shows the most pricing features.
- **D-04:** The invoice image is a Final invoice that is not marked paid (no PAID line). It shows what a customer
  usually receives.
- **D-05:** Only sample names: "Sample Printing Co.", "Harbor Dive Shop", 555.0100 numbers. No personal data in the
  images.

### Image look and size
- **D-06:** Render the window images at 2x (192 DPI) so they stay sharp when GitHub scales them. Today
  `WpfHost.Screenshot` renders at 96 DPI only, so it needs a scale or DPI option, or a sibling helper.
- **D-07:** Capture the main window at its default 1100x680. Size the job editor at its default size.
- **D-08:** Capture the window content only, as `RenderTargetBitmap` does today, in the app's dark Fluent theme. No
  drawn title bar, shadow or frame.
- **D-09:** The invoice image is the full page 1 at about 110 DPI (roughly 935x1210 px for letter size), from
  `InvoicePdf.RenderPages(job, quote, book, InvoiceKind.Final, dpi)`.

### Claude's Discretion
- **The regenerate command:** pick the form. Suggested: a PowerShell script at the repo root, next to `publish.ps1`
  (for example `make-readme-images.ps1`), that runs one tagged UI test (a trait or category filter). The test writes
  PNGs into a committed folder such as `docs/images/` only when the script asks for it, for example through an
  environment variable or an output path. A normal `dotnet test` run and CI must never rewrite the committed images.
  It must still follow the UI test rules: offscreen through `WpfHost`, no window shown, activated or focused, no
  dialogs.
- **Stable output:** keep regenerated images identical when nothing changed. Use fixed dates in the sample data (the
  finalized job already uses 2026-10-30), and avoid anything based on `DateTime.Today`, so a rerun doesn't churn git.
- **README placement and captions:** put the images near the top so a visitor sees them early, but keep the existing
  "Personal project" and "AI use disclosure" notices intact. Give each image a short caption. Link the invoice image
  so it opens at full size. Add a short "Regenerating the README images" note under "Development".
- **File size:** keep each PNG reasonably small (a few hundred KB at most). Plain PNG encoding is fine. No extra tools
  unless they're needed.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Project rules and scope
- `CLAUDE.md`: UI tests offscreen only (WpfHost), no personal data, plain English with no em dashes, AUDIT rule for
  file writes
- `.planning/REQUIREMENTS.md`: SHOW-01 to SHOW-05
- `.planning/ROADMAP.md`: Phase 1 success criteria
- `AUDITING.md`: any new file write in app code needs a row. Test-only writes follow the existing UI test practice;
  check how the screenshots folder is treated there.

### Codebase maps
- `.planning/codebase/TESTING.md`: how the test projects run, and what CI does
- `.planning/codebase/CONVENTIONS.md`: naming and comment style

No external specs. Requirements are fully captured in the decisions above.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `tests/PrintInvoicer.UiTests/WpfHost.cs`: a single STA thread with the app's resources loaded. `Offscreen(window)`
  shows a window at -20000,-20000 without activating it. `Screenshot(window, name)` saves a 96 DPI PNG of the window
  content to `bin/.../screens/`. Extend this for 2x and for a chosen output folder.
- `tests/PrintInvoicer.UiTests/SampleShop.cs`: a throwaway database with "Sample Printing Co.", an FDM job from
  `Wire_Brush_Insert.gcode.3mf` (with discount and customer-supplied filament), a resin job from a generated `.goo`,
  and a finalized job from `Cylindex_Tube_Cap.gcode.3mf`. It sets `App.App.Db`.
- `tests/PrintInvoicer.UiTests/ScreenTests.cs`: already screenshots `main-*`, `editor-fdm`, `editor-resin`,
  `editor-finalized` and `main-update-bar`. Shows how to open `MainWindow` and `JobEditorWindow` offscreen and close
  them quietly (`CloseQuietly`).
- `src/PrintInvoicer.App/Pdf/InvoicePdf.cs`: `RenderPages(job, quote, book, kind, dpi)` returns PNG bytes per page.
  `InvoiceKind { Preview, Final, Internal }`.
- `tests/PrintInvoicer.Tests/Fixtures/*.gcode.3mf` and `TestFiles.GooHeader(...)`: the only sample slice inputs.

### Established Patterns
- UI tests run with parallelization off (one WPF thread). `MainWindow.CheckForUpdatesOnLaunch = false`, and window
  layout is redirected to a temp file.
- CI (`.github/workflows/ci.yml`) builds and runs `tests/PrintInvoicer.UiTests` on Windows and uploads `screens/` as
  an artifact.
- The app theme is dark Fluent (`App.xaml` `ThemeMode="Dark"`). Main window is 1100x680 by default.

### Integration Points
- New output folder for committed images (for example `docs/images/`), referenced from `README.md`.
- New script at the repo root, next to `publish.ps1`. The README "Development" section documents it.

</code_context>

<specifics>
## Specific Ideas

- The user wants a visitor who stumbles on the repo to get an instant visual idea of what the app is and what it
  produces. The images serve that, not documentation of every screen.
- Job list states to show: draft, finalized, paid.

</specifics>

<deferred>
## Deferred Ideas

- A render-comparison test for old invoices (C18 / FIX2-06) could reuse this generator. It's v2, not this phase.
- Screenshots of the printers / filaments / settings screens: out of scope by the earlier decision.

</deferred>

---

*Phase: 01-readme-showcase*
*Context gathered: 2026-11-01*

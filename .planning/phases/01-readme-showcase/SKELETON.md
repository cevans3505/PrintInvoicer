# Walking Skeleton - PrintInvoicer README Showcase

**Phase:** 1
**Generated:** 2026-11-01

## Why this skeleton is not a project scaffold

PrintInvoicer is an existing, shipped app (v3.6.0): the .NET 10 solution, WPF windows, SQLite storage, QuestPDF invoices,
CI and release workflows all exist and are not touched by this phase. The generic Walking Skeleton items (framework init,
routing, a first DB read/write, a first UI interaction, a deploy target) are therefore already in place and are marked
not applicable below. What this phase needs proven end to end is the image pipeline: sample data, offscreen render, a
gated write into the repository, one command, and the README that shows the result.

## Capability Proven End-to-End

A maintainer runs `.\make-readme-images.ps1` from the repo root and the job list picture `docs/images/main-jobs.png` is
rebuilt offscreen (no window shown, activated or focused, no dialog) from the sample shop and shown near the top of
`README.md`.

This is the leading `type="tracer"` task of plan 01-01. Every later slice in the phase (the paid state and fixed dates,
the editor picture, the invoice picture, the regeneration docs) reuses this pipeline without changing its shape.

## Architectural Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Generator host | One xUnit test, `ReadmeImageTests.Readme_images_are_rebuilt_from_the_sample_shop`, in `tests/PrintInvoicer.UiTests`, tagged `[Trait("Category", "ReadmeImages")]` | Only the UI test host has the STA thread, the app's theme and resources, and the offscreen rules (`WpfHost`). Keeping it in `tests/` means no shipped code changes and no app `AUDIT:` row. |
| Window render | `WpfHost.RenderPng(Window window, double scale = 1)` renders the window's root visual (`VisualTreeHelper.GetChild(window, 0)`) at `96 * scale` DPI; README images use scale 2 (D-06, D-08) | Root visual is the full client area (the old `window.Content` render crops the editor's margin); no title bar or frame is drawn. |
| Invoice render | `InvoicePdf.RenderPages(job, quote, book, InvoiceKind.Final, 110)[0]` on an in-memory finalized copy of the Wire Brush job (D-09, D-03, D-04) | Same layout code as the real PDF; byte-stable; the database job stays a draft for the editor picture. |
| Sample data | Existing `SampleShop`, extended with a fourth finalized and paid job and fixed dates on every job (D-01, D-02, D-05) | Tests and README images share one data set; fixed dates stop daily churn. |
| Write gate | Always render into `bin\...\screens\readme`; copy into the folder named by `PRINTINVOICER_README_IMAGES` only when it is set | A normal `dotnet test`, CI and `publish.ps1` never rewrite committed files; CI still proves the pictures render. |
| Entry point | `make-readme-images.ps1` at the repo root, next to `publish.ps1`, Windows PowerShell 5.1, ASCII only | Same style as `publish.ps1`; sets the variable, runs `dotnet test --filter "Category=ReadmeImages"`, clears the variable, and fails if any picture was not rewritten (a zero-match filter exits 0). |
| Output location | `docs/images/` with fixed names `main-jobs.png`, `job-editor.png`, `invoice-page-1.png` | Plain relative paths for the README; names never come from input. |
| README layout | New `## What it looks like` section right after the intro paragraph, before the two notices (text of the notices unchanged); `### Regenerating the README images` under `## Development` | A visitor sees the pictures in the first screen; the notices stay intact. |

## Stack Touched in Phase 1

- [x] Project scaffold: not applicable (existing .NET 10 solution, `global.json`, CI); nothing new is scaffolded
- [x] Routing: not applicable (desktop app; the real `MainWindow` and `JobEditorWindow` are opened as they are)
- [ ] Database: real throwaway SQLite database from `SampleShop` (writes the sample jobs, reads them back for the windows)
- [ ] UI: the real `MainWindow` rendered offscreen with the real view model and bindings
- [ ] Deployment: documented local run command `.\make-readme-images.ps1`; output committed under `docs/images` and shown by GitHub's README rendering

## Out of Scope (Deferred to Later Slices or Other Phases)

- Screenshots of the Materials, Printers and Settings tabs (out of scope by an earlier decision)
- Render-comparison tests for old invoices (FIX2-06, v2); they may reuse this generator later
- Any change under `src/` (app behavior, pricing, invoice layout)
- Image compression tools (plain WPF PNG is small enough)

## Subsequent Slice Plan

Each later slice adds to this skeleton without changing its decisions:

- Plan 01-01, task 2: the job list shows draft, invoiced and paid states with fixed dates (SHOW-01)
- Plan 01-02, task 1: the job editor picture for the Wire Brush Inserts draft (SHOW-02)
- Plan 01-02, task 2: page 1 of the Final invoice for the same job (SHOW-03)
- Plan 01-02, task 3: the README regeneration note, AUDITING.md bullet, CI comment, and a byte-stability check (SHOW-04, SHOW-05)
- Phase 4 later links the slice-file submission forms from the same README

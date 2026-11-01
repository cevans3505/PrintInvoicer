# PrintInvoicer

## What This Is

PrintInvoicer is a Windows desktop app (.NET 10, WPF, SQLite, QuestPDF) that prices 3D print jobs (FDM and resin) from sliced files and makes PDF invoices. It is a public, MIT-licensed personal project shared as-is at https://github.com/cevans3505/PrintInvoicer (current version 3.6.0). This milestone makes the project easier to understand at a glance, pays down known concerns in the order that gives the most value per unit of effort, and opens a channel for people to send slice files so support and accuracy can improve.

## Core Value

Prices and invoices stay correct and trustworthy: a finalized invoice never changes, and totals match what the slice file and price book say.

## Requirements

### Validated

<!-- Inferred from the existing codebase (.planning/codebase/). -->

- ✓ Build a job from Bambu Studio / Orca (.3mf, .gcode.3mf) and ELEGOO .goo slice files — existing
- ✓ Price jobs from printers, filaments/resins, labor, overhead and markup via PricingCalculator and PriceBook — existing
- ✓ Freeze prices on finalize (PriceSnapshot) with versioned PricingRules so old invoices never change — existing
- ✓ Generate and print invoice PDFs with plate images (QuestPDF) — existing
- ✓ Store data in local SQLite with daily backups and restore — existing
- ✓ Check GitHub releases for updates; build an error report on failure — existing
- ✓ Core tests (cross-platform) and offscreen WPF UI tests via WpfHost — existing

### Active

- [ ] README shows generated images of the main job list, the job editor and page 1 of a sample invoice
- [ ] Images are produced by a repeatable script/test that renders offscreen with sample data, so they can be regenerated after UI changes
- [ ] Every item in `.planning/codebase/CONCERNS.md` is scored for benefit vs cost (cost = complexity / tokens to resolve) in a planning doc with a short rationale per item
- [ ] Concerns above the cutoff are fixed in score order (most efficient first); items that are not really fixable (license, scale limits) are noted and left alone
- [ ] A GitHub issue form collects slice files plus printer make/model, printer type, slicer and version, and expected vs actual results, for both "unsupported printer/slicer" and "inaccurate calculation" reports
- [ ] The app has a menu item that opens that issue form in the browser
- [ ] The issue form and README warn that slice files can contain project names, paths and thumbnails, and should only be shared if the user is fine making them public

### Out of Scope

- In-app upload of slice files — needs a server or token and a new network path; the app keeps network access minimal
- In-app sanitizing of slice files before sharing — warning text is enough for now
- Screenshots of the printers / filaments setup screens — the three chosen images cover the story
- Fixing low-score concerns below the cutoff — tracked in the triage doc for later
- Updating CONCERNS.md with scores — the triage stays in .planning/ only

## Context

- Brownfield: codebase mapped on 2026-11-01 in `.planning/codebase/` (ARCHITECTURE, STACK, CONVENTIONS, CONCERNS, INTEGRATIONS, STRUCTURE, TESTING).
- Layout: `src/PrintInvoicer.Core` (net10.0, no UI, runs on Linux), `src/PrintInvoicer.App` (WPF), `tests/PrintInvoicer.Tests`, `tests/PrintInvoicer.UiTests` (Windows only, offscreen).
- Big files: `Data/Database.cs`, `Pdf/InvoicePdf.cs`, `ViewModels/JobEditorViewModel.cs`.
- Concerns include: repricing on revert-and-refinalize, retroactive price freeze on every start, string-typed money columns, implicit schema versioning, local-date handling, stale window after restore, backup overlap at startup, shell argument concatenation, restore accepting any DB with the main tables, output names from user data, full job list reload, unsigned releases, no crash recovery for in-progress jobs, no corruption check, and test coverage gaps.
- GitHub attachments do not accept .3mf / .goo directly; submitters will likely need to zip files (to confirm during planning).
- The app already opens GitHub URLs (UpdateService) with a repository allow-check; the new menu link should follow that pattern.

## Constraints

- **Finalized invoices never change**: pricing changes go behind a new `PricingRules` version; old totals must still match.
- **Schema changes**: only via `AddColumnIfMissing` / `CREATE TABLE IF NOT EXISTS` in `Database.EnsureCreated`, with defaults that keep existing data meaning the same.
- **Outside access**: any new network, process start, print or file read/write/delete gets an `// AUDIT:` comment and a row in `AUDITING.md`.
- **UI tests offscreen only**: never show, activate or focus a window, never trigger a dialog; use `WpfHost`. Applies to the screenshot generator too.
- **No personal data**: sample names like "Harbor Dive Shop", "Sample Printing Co." in code, tests, docs and README images.
- **Writing style**: plain English, no em dashes, in docs, comments and messages.
- **File readers**: treat files as untrusted, range-check, cap sizes, throw only `InvalidDataException` with a readable message.

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| README images generated by script, offscreen, from sample data | Repeatable, stays current with UI changes, no personal data | — Pending |
| Images: main job list, job editor, invoice PDF page 1 | Shows what it is and what it produces | — Pending |
| Score all concerns, fix those above a cutoff in score order | Most value per token spent; skip unfixable items | — Pending |
| Triage doc lives in .planning/, not public docs | Internal planning artifact | — Pending |
| Submissions via GitHub issue form, plus an app menu link | No new server or network path; reuses GitHub | — Pending |
| Privacy: warning text only, no in-app sanitizer | Lower cost; users decide what to share | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd-complete-milestone`):
1. Full review of all sections
2. Core Value check: still the right priority?
3. Audit Out of Scope: reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-11-01 after initialization*

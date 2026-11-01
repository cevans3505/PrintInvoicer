# Requirements: PrintInvoicer

**Defined:** 2026-11-01
**Core Value:** Prices and invoices stay correct and trustworthy: a finalized invoice never changes, and totals match what the slice file and price book say.

## v1 Requirements

### README Showcase

- [ ] **SHOW-01**: A visitor to the README sees an image of the main job list with sample jobs in different states (draft, finalized, paid)
- [ ] **SHOW-02**: A visitor sees an image of the job editor with a job built from a slice file, showing plates, filament use and the price breakdown
- [ ] **SHOW-03**: A visitor sees page 1 of a sample invoice as a PNG
- [ ] **SHOW-04**: A maintainer can regenerate all README images with one command that renders offscreen (no window shown) from seeded sample data with no personal data
- [ ] **SHOW-05**: The README explains how to regenerate the images

### Slice File Submissions

- [ ] **SUBM-01**: A user can open a GitHub issue form to request support for an unsupported printer or slicer, giving printer make, model and type (FDM / resin), slicer name and version, and attaching slice files
- [ ] **SUBM-02**: A user can open a GitHub issue form to report an inaccurate calculation, giving the same details plus expected vs actual values (time, material, price)
- [ ] **SUBM-03**: The issue forms say how to attach files GitHub may not accept directly (for example zip them first)
- [ ] **SUBM-04**: The issue forms and the README warn that slice files can contain project names, file paths and thumbnails, and that people should only share files they're fine making public
- [ ] **SUBM-05**: The README links to the submission forms
- [ ] **SUBM-06**: A user can pick a menu item in the app that opens the submission page in the browser (AUDIT comment and AUDITING.md row, URL restricted to this repository)

### Concerns Triage

- [x] **TRIA-01**: `.planning/CONCERNS-TRIAGE.md` scores every concern in CONCERNS.md for benefit, cost and benefit/cost, with a decision and rationale for each

### Concern Fixes (score order)

- [ ] **FIX-01** (C11, 3.00): Each release publishes a SHA-256 checksum file, and the release steps tell people to state it in the release notes
- [ ] **FIX-02** (C27, 2.50): A test opens a fixture database from before snapshots existed, with finalized jobs and no snapshot, and checks that each one gets a snapshot exactly once, with the expected totals
- [ ] **FIX-03** (C06, 2.00): Explorer / Process.Start calls pass paths with `ArgumentList`, so a path containing a quote still works
- [ ] **FIX-04** (C09, 2.00): A manual backup and the startup daily backup can't use the same temporary `.partial` file
- [ ] **FIX-05** (C10, 2.00): The update-check response is read with a size cap, and a response over the cap is treated as "no update info"
- [ ] **FIX-06** (C14, 2.00): Invoice file and folder names built from user data are sanitized and length-limited, with tests for `..`, `\`, `:` and reserved names like `CON`
- [ ] **FIX-07** (C22, 2.00): README and AUDITING.md state the QuestPDF Community license revenue-threshold condition
- [ ] **FIX-08** (C01, 1.50): The one-time freeze of older finalized jobs is recorded in `settings` and doesn't run again once it's done, and invoices already frozen keep their totals
- [ ] **FIX-09** (C08 + C19, 1.50): After a backup restore, nothing can save over the restored database before the restart, and the restart works from an installed copy
- [ ] **FIX-10** (C12, 1.50): The ZIP slice reader caps the total uncompressed size across all entries and throws `InvalidDataException` when it's over, with a robustness test
- [ ] **FIX-11** (C20, 1.50): A second copy of the app started on the same PC shows a message and exits instead of opening the same database
- [ ] **FIX-12** (C25, 1.50): The live database is checked with `PRAGMA integrity_check` before the daily backup and from a Settings button, and the user is warned if it fails

## v2 Requirements

Deferred to the next milestone (triage score 1.0 to 1.49).

### Concern Fixes

- **FIX2-01** (C02): Reverting and finalizing again keeps the previous snapshot and total in a history table, and the prompt shows the old and new totals
- **FIX2-02** (C04): A schema version is stored, and the app refuses to write to a database from a newer build
- **FIX2-03** (C26a): Tests for MainViewModel finalize/generate and InvoicePdf output
- **FIX2-04** (C07): "Today" is captured once per action, with year-boundary tests for invoice numbers
- **FIX2-05** (C13): Restore checks the schema version and the column lists
- **FIX2-06** (C18): A test catches changes to how an old invoice renders
- **FIX2-07** (C21): Manual backups have a count or size limit, and Settings shows their total size

## Out of Scope

| Feature | Reason |
|---------|--------|
| In-app upload of slice files | Needs a server or token and a new network path |
| In-app sanitizing of slice files | Warning text was chosen instead |
| Screenshots of the printers / filaments screens | The three chosen images are enough |
| Code signing releases | Needs a certificate; checksums cover verification for now |
| Moving off QuestPDF | Large port; documenting the license is enough |
| Changing existing money columns from text | Frozen data depends on them |
| Splitting large files (C03), crash recovery (C24), list paging (C15), pricing caching (C16), broad UI test coverage (C26b), multi-PC sharing (C29) | Triage score below 1.0; see CONCERNS-TRIAGE.md |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| SHOW-01 | Phase 1 | Pending |
| SHOW-02 | Phase 1 | Pending |
| SHOW-03 | Phase 1 | Pending |
| SHOW-04 | Phase 1 | Pending |
| SHOW-05 | Phase 1 | Pending |
| TRIA-01 | Phase 2 | Complete |
| FIX-01 | Phase 2 | Pending |
| FIX-02 | Phase 2 | Pending |
| FIX-03 | Phase 2 | Pending |
| FIX-04 | Phase 2 | Pending |
| FIX-05 | Phase 2 | Pending |
| FIX-06 | Phase 2 | Pending |
| FIX-07 | Phase 2 | Pending |
| FIX-08 | Phase 3 | Pending |
| FIX-09 | Phase 3 | Pending |
| FIX-10 | Phase 3 | Pending |
| FIX-11 | Phase 3 | Pending |
| FIX-12 | Phase 3 | Pending |
| SUBM-01 | Phase 4 | Pending |
| SUBM-02 | Phase 4 | Pending |
| SUBM-03 | Phase 4 | Pending |
| SUBM-04 | Phase 4 | Pending |
| SUBM-05 | Phase 4 | Pending |
| SUBM-06 | Phase 4 | Pending |

**Coverage:**
- v1 requirements: 24 total
- Mapped to phases: 24
- Complete: 1 (TRIA-01)
- Unmapped: 0

---
*Requirements defined: 2026-11-01*
*Last updated: 2026-11-01 after roadmap creation*

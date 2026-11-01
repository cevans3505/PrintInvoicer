# Roadmap: PrintInvoicer

## Overview

This milestone makes PrintInvoicer easier to understand at a glance, pays down known concerns in the order that gives the most value per unit of effort, and opens a way for people to send slice files. It starts with generated README images of the job list, the job editor and a sample invoice. Next come the concern fixes in triage score order: first the quick wins scored 2.0 and above, then the data-safety fixes scored 1.5. It ends with GitHub issue forms for slice file submissions, linked from the README and from a menu item in the app. Finalized invoices must never change at any point along the way.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [x] **Phase 1: README Showcase** - Generated offscreen images of the job list, job editor and invoice page 1 in the README (completed 2026-11-01)
- [ ] **Phase 2: Quick-Win Concern Fixes** - Concerns scored 2.0 and above fixed in score order (checksums, legacy database test, safer paths, names and reads)
- [ ] **Phase 3: Data Safety Fixes** - Concerns scored 1.5 fixed in score order (freeze once, safe restore, ZIP cap, single instance, integrity check)
- [ ] **Phase 4: Slice File Submissions** - GitHub issue forms for slice files, linked from the README and an app menu item, with privacy warnings

## Phase Details

### Phase 1: README Showcase
**Goal**: A visitor to the README can see what PrintInvoicer looks like and what it produces, and a maintainer can regenerate those images with one command after UI changes.
**Mode:** mvp
**Depends on**: Nothing (first phase)
**Requirements**: SHOW-01, SHOW-02, SHOW-03, SHOW-04, SHOW-05
**Success Criteria** (what must be TRUE):
  1. The README shows an image of the main job list with sample jobs in draft, finalized and paid states
  2. The README shows an image of the job editor with a job built from a slice file, showing plates, filament use and the price breakdown
  3. The README shows page 1 of a sample invoice as a PNG
  4. A maintainer runs one command and all three images are rebuilt offscreen (no window or dialog appears) from seeded sample data that uses only sample names such as "Harbor Dive Shop"
  5. The README explains how to run that command to regenerate the images
**Plans**: 2/2 plans complete
Plans:
**Wave 1**
- [x] 01-01-PLAN.md - Tracer: job list picture through the one command (2x offscreen render, gated write, README section), then a paid job and fixed dates in SampleShop (SHOW-01, SHOW-04)

**Wave 2** *(blocked on Wave 1 completion)*
- [x] 01-02-PLAN.md - Job editor and invoice page 1 pictures on the same pipeline, the README regeneration note, AUDITING.md bullet and CI comment (SHOW-02, SHOW-03, SHOW-04, SHOW-05)
**UI hint**: yes

### Phase 2: Quick-Win Concern Fixes
**Goal**: The concerns with the best benefit for the cost (triage score 2.0 and above) are fixed in score order, so releases can be checked, invoices already sent are guarded by a test, and paths, file names and update checks are handled safely.
**Mode:** mvp
**Depends on**: Phase 1 (run order only; no code dependency)
**Requirements**: TRIA-01, FIX-01, FIX-02, FIX-03, FIX-04, FIX-05, FIX-06, FIX-07
**Success Criteria** (what must be TRUE):
  1. `.planning/CONCERNS-TRIAGE.md` scores every concern (TRIA-01, already done), and any item found to be already handled when its code is checked is re-scored there rather than changed
  2. Each release has a SHA-256 checksum file next to the zip, the release steps say to state it in the release notes, and the README and AUDITING.md state the QuestPDF Community license revenue-threshold condition
  3. A test opens a fixture database from before snapshots existed and shows that each finalized job gets a snapshot exactly once, with the expected totals
  4. Opening a file or folder whose path contains a quote works, and a manual backup and the startup daily backup never share the same `.partial` file
  5. An update-check response over the size cap is treated as "no update info", and invoice file and folder names built from user data containing `..`, `\`, `:` or reserved names like `CON` come out safe and length-limited, with tests
**Plans**: TBD

### Phase 3: Data Safety Fixes
**Goal**: The remaining v1 concerns (triage score 1.5) are fixed in score order, so the price freeze runs only once, a restore or a second copy of the app can't overwrite data, oversized slice archives are rejected, and database corruption is caught early.
**Mode:** mvp
**Depends on**: Phase 2 (FIX-08 changes the freeze path that the FIX-02 legacy-database test guards)
**Requirements**: FIX-08, FIX-09, FIX-10, FIX-11, FIX-12
**Success Criteria** (what must be TRUE):
  1. Once the one-time freeze of older finalized jobs has run, `settings` records it and later starts skip it, while the Phase 2 legacy-database test still shows the same totals for invoices already frozen
  2. After a backup restore, nothing can save over the restored database before the restart, and the restart works when the app runs from an installed copy
  3. Opening a slice file whose ZIP entries add up to more than the total uncompressed cap shows a readable error (`InvalidDataException`), covered by a robustness test
  4. Starting a second copy of the app on the same PC shows a message and exits without opening the database
  5. The live database is checked with `PRAGMA integrity_check` before the daily backup and from a Settings button, and the user sees a warning when the check fails
**Plans**: TBD
**UI hint**: yes

### Phase 4: Slice File Submissions
**Goal**: People can send slice files and printer and slicer details through GitHub issue forms, reached from the README or a menu item in the app, and are warned about what slice files can reveal before they share them.
**Mode:** mvp
**Depends on**: Phase 1 (README layout); independent of the fix phases
**Requirements**: SUBM-01, SUBM-02, SUBM-03, SUBM-04, SUBM-05, SUBM-06
**Success Criteria** (what must be TRUE):
  1. On the repository's new issue page, a user can pick an "unsupported printer or slicer" form that asks for printer make, model and type (FDM or resin), slicer name and version, and slice file attachments
  2. A user can pick an "inaccurate calculation" form that asks for the same details plus expected and actual values for time, material and price
  3. Both forms explain how to attach files GitHub may not accept directly (for example zip them first), and both forms and the README warn that slice files can contain project names, file paths and thumbnails and should only be shared if the user is fine making them public
  4. The README links to both submission forms
  5. Picking the new menu item in the app opens the submission page in the browser, with the URL restricted to this repository and the call marked with an `// AUDIT:` comment and a row in AUDITING.md
**Plans**: TBD
**UI hint**: yes

## Progress

**Execution Order:**
Phases execute in numeric order: 1, 2, 3, 4

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. README Showcase | 2/2 | Complete    | 2026-11-01 |
| 2. Quick-Win Concern Fixes | 0/TBD | Not started | - |
| 3. Data Safety Fixes | 0/TBD | Not started | - |
| 4. Slice File Submissions | 0/TBD | Not started | - |

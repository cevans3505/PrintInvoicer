---
phase: 01-readme-showcase
plan: 02
subsystem: testing
tags: [wpf, ui-tests, readme, screenshots, questpdf, powershell]
requires:
  - phase: 01-readme-showcase
    provides: WpfHost.RenderPng, ReadmeImageTests pipeline, make-readme-images.ps1, SampleShop
provides:
  - docs/images/job-editor.png (Wire Brush Inserts draft, 2x window render)
  - docs/images/invoice-page-1.png (page 1 of the unpaid Final invoice, 935 x 1210)
  - make-readme-images.ps1 checks and rebuilds all three pictures
  - README "Regenerating the README images" subsection
  - AUDITING.md row for the developer tool
affects: []
actuals:
  tokens: 9000
  tasks: 3
  commits: 3
tech-stack:
  added: []
  patterns:
    - "All pictures are rendered and asserted before the first Save call, so a failed run writes nothing"
    - "The invoice job is finalized in memory only; the sample database job stays a draft"
key-files:
  created:
    - docs/images/job-editor.png
    - docs/images/invoice-page-1.png
  modified:
    - tests/PrintInvoicer.UiTests/ReadmeImageTests.cs
    - make-readme-images.ps1
    - README.md
    - AUDITING.md
    - .github/workflows/ci.yml
key-decisions:
  - "Invoice is dated 2026-10-31, the job's fixed created date, so it does not predate the job"
  - "Invoice picture is shown at width 480 inside a link to the full-size file; window pictures need no width"
requirements-completed: [SHOW-02, SHOW-03, SHOW-04, SHOW-05]
status: complete
plan_head_before: 1703d5d1f875af9b3098d2c05726590291eafc35
plan_head_after: 92a1eabefdb565b8b05bb564d4ed9f4067b21bac
commits: 3
duration: 25 min
completed: 2026-11-01
---

# Phase 1 Plan 02: The editor and invoice pictures, and the regeneration note Summary

**One command (`.\make-readme-images.ps1`) now rebuilds the job list, the Wire Brush job editor (2x, 2608 x 1562) and page 1 of its Final invoice (935 x 1210) offscreen, with the editor and invoice totals asserted equal ($23.37), and the README and AUDITING.md document it.**

## Performance

- **Duration:** about 25 min
- **Completed:** 2026-11-01
- **Tasks:** 3
- **Files modified:** 7 (2 new pictures)

## Accomplishments

- `RenderJobEditor` renders the editor at a size set in code (1320 x 820) at 2x, asserts size, byte cap and both edges (`AssertFillsFrame`) and returns the total it shows.
- `RenderInvoice` finalizes a reloaded copy in memory, renders with `InvoicePdf.RenderPages(..., Final, 110)`, requires exactly one page at 935 x 1210, and checks the database job is still not finalized.
- The test asserts editor total equals invoice total before the three saves, which are the last statements in fixed order.
- `make-readme-images.ps1` lists all three names and fails if any was not rewritten.
- README shows the three pictures above the unchanged notices (invoice linked to full size) and has `### Regenerating the README images` under Development.
- AUDITING.md has the `make-readme-images.ps1` bullet under Building and releasing and a pointer from Files written or deleted; "There are 26 tags" and the "Nothing is written anywhere else" line are untouched.
- ci.yml comment now says "all 10 pass there".

## Task Commits

1. **Task 1 (tracer): job editor picture** - `4af2ca2` (feat)
2. **Task 2: invoice page 1 picture** - `e762ad1` (feat)
3. **Task 3: docs, AUDITING.md, CI comment** - `92a1eab` (docs)

## Files Created/Modified

- `tests/PrintInvoicer.UiTests/ReadmeImageTests.cs` - RenderJobEditor, RenderInvoice, total cross-check, three saves
- `make-readme-images.ps1` - image list now three names
- `docs/images/job-editor.png`, `docs/images/invoice-page-1.png` - generated pictures
- `README.md`, `AUDITING.md`, `.github/workflows/ci.yml` - docs and CI comment

## Decisions Made

- Followed the plan (dates, widths, placement of pictures before the notices as in A-3 to A-5).

## Deviations from Plan

None - plan executed exactly as written. (One slip while editing: a Python string turned `\readme` into a carriage return in the README; caught in the diff review and fixed before the Task 3 commit.)

## Issues Encountered

None. Note the plan's ledger base file was created after the first task commit, using the pre-plan HEAD (1703d5d), so the measured commit count is correct.

## Verification

- `dotnet test PrintInvoicer.sln`: 372 unit tests passed, 10 UI tests passed, 0 failed. `git status docs/images` was clean after that normal run.
- Two regenerations in a row: `sha256sum -c` OK for all three pictures (byte-identical), no git diff.
- All Task 1 to 3 acceptance greps and awk gates pass, including notices byte-identical to dd78801, no em or en dashes, and `git diff --quiet dd78801 -- src`.
- Pictures looked at: the editor shows Wire Brush Inserts, Harbor Dive Shop, 2026-001, plate picture, five slots with grams, slot 5 Customer supplied ticked, 15% discount, Total $23.37, and the whole bottom button row. The invoice shows Sample Printing Co., 555.0100, Harbor Dive Shop, 2026-001, October 31, 2026, savings box, notes, no PAID line, Total $23.37. Only sample names.
- Not done: viewing the README in a rendered Markdown preview (human check left for the verifier).

## Known Stubs

None.

## Threat Flags

None. All new file writes were already tagged `// AUDIT:` in plan 01-01 and are now listed in AUDITING.md.

## Self-Check: PASSED

- Files exist: both PNGs, ReadmeImageTests.cs, this summary.
- Commits 4af2ca2, e762ad1 and 92a1eab are ancestors of HEAD.

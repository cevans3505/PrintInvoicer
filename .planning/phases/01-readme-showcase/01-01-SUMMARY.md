---
phase: 01-readme-showcase
plan: 01
subsystem: testing
tags: [wpf, ui-tests, readme, screenshots, powershell]
requires: []
provides:
  - WpfHost.RenderPng (window root visual as PNG bytes at a chosen scale)
  - ReadmeImageTests generator (Category=ReadmeImages), env-var gated copy into docs/images
  - make-readme-images.ps1 one-command rebuild
  - docs/images/main-jobs.png and the README "What it looks like" section
  - SampleShop.PaidJob and fixed dates on every sample job
affects: [01-02]
tech-stack:
  added: []
  patterns:
    - "Generator test writes to bin screens/readme on every run; copies to docs/images only when PRINTINVOICER_README_IMAGES is set by the script"
    - "Edge-pixel check (AssertFillsFrame) guards against a 1x render in a 2x canvas"
key-files:
  created:
    - tests/PrintInvoicer.UiTests/ReadmeImageTests.cs
    - make-readme-images.ps1
    - docs/images/main-jobs.png
  modified:
    - tests/PrintInvoicer.UiTests/WpfHost.cs
    - tests/PrintInvoicer.UiTests/SampleShop.cs
    - tests/PrintInvoicer.UiTests/ScreenTests.cs
    - README.md
key-decisions:
  - "Screenshot now delegates to RenderPng (root visual), which also stops cropping the job editor's 12 px margin"
  - "Generator asserts size, edges, row order, invoices, dates and statuses before the first Save, so a bad render writes nothing"
requirements-completed: [SHOW-01]
status: complete
actuals:
  tokens: 14000
  tasks: 2
  commits: 2
plan_head_before: d6103261955256f4f74641b158ddea5bbc344fde
plan_head_after: 970282d6b10ac501a45a336c21ee0fa817da8728
commits: 2
duration: 30 min
completed: 2026-11-01
---

# Phase 1 Plan 01: The job list picture and the one-command pipeline Summary

**`.\make-readme-images.ps1` rebuilds `docs/images/main-jobs.png` offscreen at 2x from the sample shop, and the README shows it above the notices; the picture covers two drafts, one invoiced and one paid job.**

## Accomplishments

- `WpfHost.RenderPng(window, scale)` renders the window root visual to PNG bytes; `Screenshot` uses it.
- `ReadmeImageTests` renders the job list at 2x with the culture pinned to en-US, checks PNG size, byte cap and both edges, and checks the four rows (titles, invoice numbers, dates, statuses) before writing anything. It always writes to the test output's `screens\readme`, and copies to `docs\images` only when `PRINTINVOICER_README_IMAGES` is set.
- `make-readme-images.ps1` sets that variable, runs the filtered test, clears the variable in `finally`, and fails with "was not rebuilt" if a picture was not rewritten (a filter matching no test exits 0 otherwise).
- `SampleShop` gets a fourth job (`PaidJob`, Dive light mounts, 2026-004, paid Oct 27), fixed created dates on all jobs, printed ticks on the finalized jobs and a natural note on the FDM job.
- README has a `## What it looks like` section between the intro and the notices; the notices are byte-identical to commit dd78801.

## Task Commits

1. Task 1 (tracer): a110610 - RenderPng, ReadmeImageTests, script, main-jobs.png, README section
2. Task 2: 970282d - fourth sample job, fixed dates, ScreenTests counts, row-order asserts, regenerated picture

## Verification

- Edge-check proof (Task 1 step 4): with `RenderPng` temporarily drawing at 96 DPI instead of `96 * scale`, `dotnet test --filter Category=ReadmeImages` (variable unset) failed with "The picture's right edge is empty, so the window was not drawn at full scale." With `96 * scale` restored it passed. Neither run touched `docs/images`.
- Gate check: a plain filtered `dotnet test` rewrote `bin/.../screens/readme/main-jobs.png` and left `docs/images` unchanged.
- `dotnet test tests/PrintInvoicer.UiTests`: 10 passed, 0 failed.
- `dotnet test PrintInvoicer.sln`: UI tests 10 passed; unit tests 372 passed; 0 failed.
- Two regenerations in a row: `sha256sum -c` reported OK (byte-identical, 199,792 bytes).
- All Task 1 and Task 2 acceptance criteria (greps, AUDIT-comment awk gates, README notice cmp, no dashes, `git diff --quiet dd78801 -- src`) pass.
- Picture looked at: four rows in order Regulator clips (Draft), Wire Brush Inserts (Draft), Tube caps (Invoiced Oct 30, Printed ticked), Dive light mounts (Paid Oct 27, Printed and Paid ticked), dark theme, no title bar; only sample names. The picture is 2168 x 1282 (window client area at 2x), inside the asserted range.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## Known Stubs

None.

## Threat Flags

None. The new file writes (test output folder, docs/images when asked) and the `dotnet test` launch are tagged `// AUDIT:` in the code; the `AUDITING.md` row is planned for 01-02 Task 3.

## Next Phase Readiness

Ready for 01-02: the pipeline, `RenderPng`, `AssertPng`, `AssertFillsFrame`, `Save` and the `$images` array in the script are in place for the editor and invoice pictures.

## Self-Check: PASSED

- Created files exist: ReadmeImageTests.cs, make-readme-images.ps1, docs/images/main-jobs.png.
- Commits a110610 and 970282d are ancestors of HEAD.

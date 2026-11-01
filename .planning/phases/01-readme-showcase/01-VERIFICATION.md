---
phase: 01-readme-showcase
verified: 2026-11-01T23:45:00Z
status: passed
score: 9/9 must-haves verified
covered_files:
  - ".github/workflows/ci.yml"
  - ".planning/phases/01-readme-showcase/01-01-PLAN.md"
  - ".planning/phases/01-readme-showcase/01-01-SUMMARY.md"
  - ".planning/phases/01-readme-showcase/01-02-PLAN.md"
  - ".planning/phases/01-readme-showcase/01-02-SUMMARY.md"
  - "AUDITING.md"
  - "README.md"
  - "docs/images/invoice-page-1.png"
  - "docs/images/job-editor.png"
  - "docs/images/main-jobs.png"
  - "make-readme-images.ps1"
  - "tests/PrintInvoicer.UiTests/ReadmeImageTests.cs"
  - "tests/PrintInvoicer.UiTests/SampleShop.cs"
  - "tests/PrintInvoicer.UiTests/ScreenTests.cs"
  - "tests/PrintInvoicer.UiTests/WpfHost.cs"
covered_digest: "v4:sha256:d8860c64470cebe22a45db51bb2ec81f41055ae255a51747029614bae1091357"
behavior_unverified: 0
overrides_applied: 0
---

# Phase 1: README Showcase Verification Report

**Phase Goal:** A visitor to the README can see what PrintInvoicer looks like and what it produces, and a maintainer can regenerate those images with one command after UI changes.
**Verified:** 2026-11-01
**Status:** passed
**Re-verification:** No, initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | README shows the job list picture with sample jobs in draft, invoiced and paid states (SHOW-01) | VERIFIED | README.md has `## What it looks like` between the intro and the Personal project notice, linking `docs/images/main-jobs.png`. The PNG (2168x1282) was viewed: four rows in order Regulator clips (Draft), Wire Brush Inserts (Draft), Tube caps (Invoiced Oct 30), Dive light mounts (Paid Oct 27), invoice numbers 2026-002, 001, 003, 004. |
| 2 | README shows the job editor with plates, filament use and price breakdown (SHOW-02) | VERIFIED | `docs/images/job-editor.png` (2608x1562) viewed: Wire Brush Inserts, plate picture, five slots with grams, customer supplied black, 15% discount, Total $23.37. Linked from README. |
| 3 | README shows page 1 of a sample invoice (SHOW-03) | VERIFIED | `docs/images/invoice-page-1.png` (935x1210) viewed: Sample Printing Co. to Harbor Dive Shop, 2026-001, October 31, 2026, Total $23.37, Page 1 of 1, no PAID line. README wraps it in a link to the full size file. |
| 4 | One command regenerates all pictures offscreen from seeded sample data (SHOW-04) | VERIFIED | I ran `make-readme-images.ps1` myself: test passed, exit 0, "Rebuilt the README images". All three PNGs byte-identical to before (sha256 check), so no git diff. A plain filtered `dotnet test` (variable unset) also passed and left docs/images untouched. Windows are shown only through `WpfHost.Offscreen`; dialogs are stubbed (`ShowNotes`, `IsDirty = false` before Close). |
| 5 | README explains how to regenerate (SHOW-05) | VERIFIED | `### Regenerating the README images` is in README.md between Development and `### Making a release`. It names `make-readme-images.ps1`, says no window appears, names `docs/images` and `SampleShop.cs`, says normal runs and CI never write to docs/images, and says to run `git status docs/images`. |
| 6 | Guards fail before writing anything on a bad render | VERIFIED | `ReadmeImageTests.cs`: row order, invoices, dates, statuses, PNG size, byte cap, `AssertFillsFrame` edge check, single invoice page, editor total equals invoice total, draft stays draft; all precede the three `Save` calls. The script throws "was not rebuilt" when a picture is stale. |
| 7 | Sample data only, no personal data | VERIFIED | Pictures and code show only Harbor Dive Shop, Sample Printing Co., 555.0100. No em or en dashes in added lines (grep on diff vs dd78801). |
| 8 | No change under src/; existing notices kept | VERIFIED | `git diff --stat dd78801 HEAD -- src` is empty. The README diff against dd78801 removes no lines, so the notices are intact. |
| 9 | Outside access is audited | VERIFIED | `// AUDIT:` comments sit above the file writes in `ReadmeImageTests.Save` and `WpfHost.Screenshot`, and `# AUDIT:` above the dotnet test and file read in the script. AUDITING.md has the `make-readme-images.ps1` bullet, a pointer from Files written or deleted, and still says "There are 26 tags". ci.yml comment says "all 10 pass there". See WR-02 for a wording inaccuracy. |

**Score:** 9/9 truths verified (0 behavior-unverified)

### Required Artifacts

| Artifact | Status | Details |
|----------|--------|---------|
| `tests/PrintInvoicer.UiTests/WpfHost.cs` (`RenderPng`) | VERIFIED | Present, used by `Screenshot` and ReadmeImageTests. |
| `tests/PrintInvoicer.UiTests/ReadmeImageTests.cs` | VERIFIED | `Trait("Category", "ReadmeImages")`, env-var gated copy. |
| `make-readme-images.ps1` | VERIFIED | Ran successfully; filter `Category=ReadmeImages`; env var cleared in `finally`. |
| `tests/PrintInvoicer.UiTests/SampleShop.cs` | VERIFIED | `PaidJob` and fixed dates produce the four rows in the picture. |
| `docs/images/*.png` (3) | VERIFIED | Exist, substantive, viewed, referenced in README. |
| `README.md`, `AUDITING.md`, `.github/workflows/ci.yml` | VERIFIED | As described above. |

### Key Link Verification

| From | To | Status |
|------|----|--------|
| make-readme-images.ps1 | ReadmeImageTests (Category filter) | WIRED (ran) |
| ReadmeImageTests | WpfHost.RenderPng(main/window, 2) | WIRED |
| ReadmeImageTests | InvoicePdf.RenderPages(..., Final, 110) | WIRED |
| README.md | the three images and the script | WIRED |

### Requirements Coverage

| Requirement | Source Plan | Status | Evidence |
|-------------|-------------|--------|----------|
| SHOW-01 | 01-01 | SATISFIED | Truth 1 |
| SHOW-02 | 01-02 | SATISFIED | Truth 2 |
| SHOW-03 | 01-02 | SATISFIED | Truth 3 |
| SHOW-04 | 01-01, 01-02 | SATISFIED | Truths 4, 6 |
| SHOW-05 | 01-02 | SATISFIED | Truth 5 |

All five IDs appear in plan frontmatter and are marked Complete in REQUIREMENTS.md. No orphaned requirements for Phase 1.

### Prohibitions

| Prohibition | Tier | Result |
|-------------|------|--------|
| No personal data in pictures/sample data/README | judgment | Checked the three pictures and code; only sample names. NON-AUTHORITATIVE judge verdict: no violation. A human glance is cheap but not required. |
| No window shown, activated or focused; no dialog | test | Enforced by `WpfHost.Offscreen` usage in the generator and the stubs; the script ran with no window. |
| No change under src/ | test | `git diff dd78801 HEAD -- src` is empty. |
| Personal project and AI disclosure notices unchanged | test | README diff removes no lines. |
| No hand-edited pictures (01-02) | judgment | Pictures are reproduced byte-for-byte by the generator. No violation. |

### Anti-Patterns Found

None blocking. No TBD/FIXME/XXX markers or stubs in the phase's files.

### Code Review (01-REVIEW.md) Disposition

Two warnings, two info items, all `open`. None block the goal.

- WR-01 (windows leaked if a picture precondition fails): the test-cleanup path only matters on a failing run. It does not affect the goal. Advisory.
- WR-02 (AUDITING.md says normal runs write only to `bin`): the sentence is inaccurate, because the UI tests already write a temp folder under `%TEMP%` and delete it. It does not affect the pictures or the pipeline. The audit row exists. Worth a one-line fix, advisory.
- IN-01, IN-02: robustness nits.

### Human Verification Required

None required. (The 01-02 summary left a rendered Markdown preview of the README as a human check; the Markdown uses plain image links and one HTML `<a><img></a>`, which GitHub renders, so I treat it as optional.)

### Gaps Summary

No gaps. The three pictures exist, show what the goal needs, are linked from the README, and one command rebuilt them identically offscreen when I ran it. Recommend fixing WR-02 wording and WR-01 cleanup when convenient.

---

_Verified: 2026-11-01_
_Verifier: Claude (gsd-verifier)_

---
phase: 01-readme-showcase
fixed_at: 2026-11-01T00:00:00Z
review_path: .planning/phases/01-readme-showcase/01-REVIEW.md
iteration: 1
findings_in_scope: 2
fixed: 2
skipped: 0
status: all_fixed
---

# Phase 1: Code Review Fix Report

**Fixed at:** 2026-11-01
**Source review:** .planning/phases/01-readme-showcase/01-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 2 (WR-01, WR-02; IN-01 and IN-02 were out of scope by request)
- Fixed: 2
- Skipped: 0

## Fixed Issues

### WR-01: Windows are leaked when a picture precondition fails

**Files modified:** `tests/PrintInvoicer.UiTests/ReadmeImageTests.cs`
**Commit:** 0ea89ff
**Applied fix:** In `RenderJobList` and `RenderJobEditor`, the sizing, row checks and `RenderPng` now run in a `try`, and `Close()` runs in a `finally`. The editor's `finally` sets `vm.IsDirty = false` first. The binding-error, size and edge checks still run after the window is closed, so behavior is otherwise the same. Windows stay offscreen through `WpfHost`.

### WR-02: AUDITING.md says a normal test run writes only to the bin folder

**Files modified:** `AUDITING.md`
**Commit:** 6e5f4ff
**Applied fix:** The bullet now says a normal test run, CI and `publish.ps1` write to the test's `bin` folder and to a temporary folder under `%TEMP%` that is deleted afterwards, never to `docs/images`. It also says the `// AUDIT:` tags under `tests` are in `WpfHost.cs` (screenshots) and `ReadmeImageTests.cs` (README pictures). "There are 26 tags" is unchanged and still correct (26 `// AUDIT:` lines in `src`).

## Verification

Run in the main checkout (no worktree; the caller asked for commits directly on `main`).
- `dotnet test tests/PrintInvoicer.UiTests --filter Category=ReadmeImages`: 1 passed. `git status docs/images` clean afterwards.
- `dotnet test PrintInvoicer.sln`: 372 unit tests passed, 10 UI tests passed, 0 failed.

---

_Fixed: 2026-11-01_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_

---
phase: 01-readme-showcase
reviewed: 2026-11-01T00:00:00Z
depth: standard
files_reviewed: 8
files_reviewed_list:
  - tests/PrintInvoicer.UiTests/WpfHost.cs
  - tests/PrintInvoicer.UiTests/ReadmeImageTests.cs
  - tests/PrintInvoicer.UiTests/SampleShop.cs
  - tests/PrintInvoicer.UiTests/ScreenTests.cs
  - make-readme-images.ps1
  - .github/workflows/ci.yml
  - AUDITING.md
  - README.md
findings:
  critical: 0
  warning: 2
  info: 2
  total: 4
status: issues_found
---

# Phase 1: Code Review Report

**Reviewed:** 2026-11-01
**Depth:** standard
**Files Reviewed:** 8

## Summary

Reviewed the phase diff (`git diff d610326`) for the eight files, and looked at the three committed pictures in `docs/images`.

Project rules check out:
- Offscreen: windows are only shown through `WpfHost.Offscreen` (far offscreen, no activation, no taskbar). No dialog is reachable: `ShowNotes` is stubbed and `IsDirty` is cleared before `Close()`.
- Sample names: only "Harbor Dive Shop" and "Sample Printing Co." appear, in the code and in all three pictures.
- Dashes: no em or en dashes in the changed files.
- Comments: the new classes and methods have purpose comments.
- Finalized invoices: `RenderInvoice` finalizes in memory only, and asserts the stored job stays a draft.

No security or data-loss defects. `PRINTINVOICER_README_IMAGES` is only read by the test project and the dev script, and the script resolves it to a full path.

The findings below are about test cleanup on failure and the accuracy of the audit document.

## Warnings

### WR-01: Windows are leaked when a picture precondition fails

**File:** `tests/PrintInvoicer.UiTests/ReadmeImageTests.cs:70-92` and `99-117`
**Issue:**
- `RenderJobList` creates the `MainWindow` and then runs several `Assert.Equal` calls (lines 78-84) before `main.Close()` (line 87).
- `RenderJobEditor` does the same: the `Assert.True` and `Assert.Equal` calls (lines 106-107) run before `vm.IsDirty = false; window.Close()`.
- When one of these asserts fails (for example the job order changes), the window stays open on the shared WPF thread for the rest of the run, and a dirty editor stays open too.
- The failure is then followed by confusing knock-on failures: leaked binding errors are picked up by the next test, and the window layout file is left in a changed state.
- The run-wide `finally` in `Readme_images_are_rebuilt_from_the_sample_shop` restores the culture only.
**Fix:**
```csharp
var main = WpfHost.Offscreen(new MainWindow());
try
{
    // ... sizing, row asserts, RenderPng ...
}
finally
{
    main.Close();
}
```
In the editor method also set `vm.IsDirty = false` inside the `finally`, before `window.Close()`.

### WR-02: AUDITING.md says a normal test run writes only to the bin folder, which is not true

**File:** `AUDITING.md:112-117`
**Issue:**
- The new bullet says "A normal test run, CI and `publish.ps1` write only to the `bin` folder".
- `WpfHost.TempFolder` (`%TEMP%\PrintInvoicer UI tests`) is created for the window layout file, and `SampleShop` creates a database, an invoices folder and sample files under it.
- `SampleShop.Dispose` then deletes that folder with `Directory.Delete`.
- Those writes and the delete are existing behavior, so the claim is wrong in a document whose purpose is accuracy.
- The sentence "the test's folder and file writes `// AUDIT:` under `tests`" also leaves out that `WpfHost.Screenshot` got two new `// AUDIT:` tags this phase. Those tags are in `WpfHost.cs`, not in the ReadmeImageTests code the bullet describes.
**Fix:** Reword the bullet, for example:

> A normal test run, CI and `publish.ps1` write to the test's `bin` folder and to a temporary folder under `%TEMP%` that is deleted afterwards, but never to `docs/images`. The `// AUDIT:` tags under `tests` are in `WpfHost.cs` (screenshots) and `ReadmeImageTests.cs` (README pictures).

## Info

### IN-01: RenderPng depends on the template's first visual child

**File:** `tests/PrintInvoicer.UiTests/WpfHost.cs:109`
**Issue:**
- `(FrameworkElement)VisualTreeHelper.GetChild(window, 0)` throws `ArgumentOutOfRangeException` if the window has no visual children (not yet templated or loaded), and `InvalidCastException` if the root is not a `FrameworkElement`.
- The old code cast `window.Content`, which has the same fragility.
- Using the root instead of `Content` also means the existing `ScreenTests` screenshots now include the window's own background and margins and are a different size from before. The ScreenTests diff does not mention this, so nobody should compare old and new pictures.
**Fix:** Give a clear failure instead:
```csharp
if (VisualTreeHelper.GetChildrenCount(window) == 0 || VisualTreeHelper.GetChild(window, 0) is not FrameworkElement root)
    throw new InvalidOperationException("The window has no rendered root yet; call Settle first.");
```

### IN-02: Dev script and test have small robustness gaps

**File:** `make-readme-images.ps1:12`, `tests/PrintInvoicer.UiTests/ReadmeImageTests.cs:185-199`
**Issue:**
- The script's `$images` list duplicates the three names written by `Save` in the test. Adding a fourth picture on one side only will not be caught until someone notices the rebuild check does not cover it.
- `Save` writes the three PNGs one at a time with no temporary file, so a failure part way (full disk, file locked by an image viewer) leaves `docs/images` with a mix of new and old pictures. The script's check does catch a missing write, but not a half-updated set where older pictures were skipped.
- The size bounds in the asserts are wide, for example 2000 to 2200 for the job list and 2400 to 2640 for the editor. They catch a wrong scale but not layout drift of several percent.
**Fix:**
- Have the test expose the names (a shared constant list, or the script reading them from a small manifest file).
- Optionally write each picture to `name + ".tmp"` and then `File.Move` it into place.
- Tighten the asserts to the observed sizes plus a small tolerance.

---

_Reviewed: 2026-11-01_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_

# Phase 1: README Showcase - Pattern Map

**Mapped:** 2026-11-01
**Files analyzed:** 8 (3 new code/script files + 3 docs-or-image outputs, 4 modified)
**Analogs found:** 8 / 8 (all analog paths are git-tracked)

No file under `src/` changes. All work is in the test project, a root script and docs.

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `tests/PrintInvoicer.UiTests/ReadmeImageTests.cs` (new) | test (generator) | file-I/O, transform | `tests/PrintInvoicer.UiTests/ScreenTests.cs` | exact |
| `tests/PrintInvoicer.UiTests/WpfHost.cs` (modify: add `RenderPng`) | utility (test host) | transform | `WpfHost.Screenshot` in the same file | exact |
| `tests/PrintInvoicer.UiTests/SampleShop.cs` (modify: 4th job, fixed dates) | test fixture | CRUD | its own `FinalizedJob` and resin "Hose clip" blocks | exact |
| `tests/PrintInvoicer.UiTests/ScreenTests.cs` (modify: 3 asserts) | test | request-response | itself | exact |
| `make-readme-images.ps1` (new, repo root) | config (script) | batch | `publish.ps1` | exact |
| `README.md`, `AUDITING.md`, `.github/workflows/ci.yml` (modify) | docs/config | n/a | their own existing sections | exact |
| `docs/images/*.png` (new) | generated asset | file-I/O | `bin/.../screens/*.png` from `WpfHost.Screenshot` | role-match |

## Pattern Assignments

### `tests/PrintInvoicer.UiTests/ReadmeImageTests.cs` (test, file-I/O)

**Analog:** `tests/PrintInvoicer.UiTests/ScreenTests.cs`

**Class shape and fixture lifetime** (ScreenTests.cs lines 16-26): one `SampleShop` per test class, disposed.
```csharp
public sealed class ScreenTests : IDisposable
{
    private readonly SampleShop _shop;

    public ScreenTests()
    {
        _shop = WpfHost.Run(() => new SampleShop());
        WpfHost.TakeBindingErrors();
    }

    public void Dispose() => _shop.Dispose();
```
Add `[Trait("Category", "ReadmeImages")]` on the class. Imports to copy: lines 1-8 (`System.IO`, `System.Windows`, `PrintInvoicer.App`, `PrintInvoicer.App.Pdf`, `PrintInvoicer.App.ViewModels`, `PrintInvoicer.Core.Models`, `PrintInvoicer.Core.Pricing`); add `System.Windows.Media.Imaging` for the size asserts.

**Test body wrapper** (lines 45-60): `[Fact] public void Name_as_sentence() => WpfHost.Run(() => { ... });`. Main window: `var main = WpfHost.Offscreen(new MainWindow());` then set `Width`/`Height` explicitly (RESEARCH Pitfall 3), `WpfHost.Settle(main)`, render, `main.Close()`.

**Open editor and close without dialog** (lines 82-93):
```csharp
private JobEditorWindow OpenEditor(Job job, out JobEditorViewModel vm)
{
    vm = new JobEditorViewModel(_shop.Db, _shop.Reload(job)) { ShowNotes = _ => { } };
    return WpfHost.Offscreen(new JobEditorWindow(vm));
}

private static void CloseQuietly(JobEditorWindow window, JobEditorViewModel vm)
{
    vm.IsDirty = false;
    window.Close();
}
```
Copy both helpers (private static in the new class). Set editor `Width = 1320; Height = 820` before rendering.

**Invoice page** (RESEARCH Pattern 3; calls in ScreenTests lines 100 and 74): reload the FDM job, `job.FinalizeInvoice(new DateTime(2026, 10, 30), redate: false)` in memory only (never `SaveJob`), `PriceSnapshot.For(job, book)`, `PricingCalculator.Calculate`, `InvoicePdf.RenderPages(job, quote, jobBook, InvoiceKind.Final, 110)[0]`.

**PNG signature assert** (same check ScreenTests uses): `Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);`. Assert invoice 935x1210 via `BitmapFrame.Create(new MemoryStream(bytes))`.

**Save helper:** always write to `Path.Combine(WpfHost.ScreenshotFolder, "readme", name)`; also copy to `Environment.GetEnvironmentVariable("PRINTINVOICER_README_IMAGES")` when non-empty (`Directory.CreateDirectory` first, fixed file names only: `main-jobs.png`, `job-editor.png`, `invoice-page-1.png`). Put `// AUDIT: file write (test only), copies the rendered README images to the folder named by PRINTINVOICER_README_IMAGES.` above the copy line. Tests have no existing AUDIT comments, so this is optional per RESEARCH; the AUDITING.md bullet is the minimum.

---

### `tests/PrintInvoicer.UiTests/WpfHost.cs` (utility, transform)

**Analog:** `WpfHost.Screenshot`, lines 102-117 (existing code to replace or delegate from):
```csharp
/// <summary>Saves what the window looks like to the screens folder.</summary>
public static void Screenshot(Window window, string name)
{
    Settle(window);
    var el = (FrameworkElement)window.Content;
    var rtb = new RenderTargetBitmap(Math.Max(1, (int)el.ActualWidth), Math.Max(1, (int)el.ActualHeight), 96, 96, PixelFormats.Pbgra32);
    var dv = new DrawingVisual();
    using (var dc = dv.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, el.ActualWidth, el.ActualHeight));
    rtb.Render(dv);
    rtb.Render(el);
    var enc = new PngBitmapEncoder();
    enc.Frames.Add(BitmapFrame.Create(rtb));
    Directory.CreateDirectory(ScreenshotFolder);
    using var fs = File.Create(Path.Combine(ScreenshotFolder, name + ".png"));
    enc.Save(fs);
}
```
Add `RenderPng(Window window, double scale = 1)` (RESEARCH Pattern 1, renders `VisualTreeHelper.GetChild(window, 0)` at `96 * scale` DPI, returns `byte[]`); optionally make `Screenshot` write `RenderPng(window)` bytes. All needed usings already present (lines 1-7: `System.Windows.Media`, `System.Windows.Media.Imaging`). Keep the `/// <summary>` style and the `public static` placement next to `Settle`.

---

### `tests/PrintInvoicer.UiTests/SampleShop.cs` (fixture, CRUD)

**Analog:** the file itself.

**4th job from a manual plate** (lines 56-61, resin version; use FDM): 
```csharp
var manual = JobBuilder.ManualPlate(resin);
manual.SlicedSeconds = 5400;
manual.Resin!.SlicedMl = 20m;
manual.Resin.ResinId = Db.GetResins().First().Id;
ResinJob.Parts.Add(new JobPart { Name = "Hose clip", Plates = { manual } });
Db.SaveJob(ResinJob);
```
For FDM set `manual.Filaments[0].SlicedGrams`, `.FilamentId = Db.GetFilaments().First().Id`, `PartsPerPlate`; part name must not contain "clips".

**Finalize pattern** (lines 63-67):
```csharp
FinalizedJob = new Job { Title = "Tube caps", Customer = "Harbor Dive Shop", InvoiceNumber = "2026-003" };
JobBuilder.AddFile(FinalizedJob, BambuFileReader.Read(TestFiles.Fixture("Cylindex_Tube_Cap.gcode.3mf")), Db.GetFilaments(),
    Db.GetMappings(), printerId: fdm.Id);
InvoiceActions.Finalize(Db, FinalizedJob, new DateTime(2026, 10, 30), redate: false);
Db.SaveJob(FinalizedJob);
```
New `PaidJob` (`InvoiceNumber = "2026-004"`, finalize 2026-10-22, then `Paid = true; PaidDate = new DateTime(2026, 10, 27)`, `Db.SaveJob`). Add `public Job PaidJob { get; }` beside line 21. Set fixed `CreatedDate` in each object initializer (lines 43, 53, 63) before first `SaveJob`: FDM 2026-10-31, resin 2026-10-31, tube caps 2026-10-29, paid 2026-10-20. Update the class `<summary>` (lines 11-14). Keep `App.App.Db = Db;` last (line 69).

---

### `tests/PrintInvoicer.UiTests/ScreenTests.cs` (modify)

Exact edits (current text):
- line 57: `Assert.Equal(3, main.ViewModel.Jobs.Count);` becomes `4`.
- line 72: `"Showing 1 of 3 jobs"` becomes `"Showing 2 of 4 jobs"` with a total summing both finalized quotes, or add `vm.PaidFilter = PaidFilter.Unpaid;` before the assert to keep the single-job logic (lines 73-74).
- line 78: `"Showing 1 of 3 jobs"` becomes `"Showing 1 of 4 jobs"`.

---

### `make-readme-images.ps1` (script, batch)

**Analog:** `publish.ps1` (git-tracked, repo root). Copy:
```powershell
# Builds PrintInvoicer as a single self-contained .exe, ...
# Works in Windows PowerShell 5.1. Close PrintInvoicer before running it.
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\PrintInvoicer')
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
...
& dotnet test (Join-Path $root 'PrintInvoicer.sln') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed; not publishing.' }
```
Use the same header comment, `param`, `$ErrorActionPreference = 'Stop'`, `$root = $PSScriptRoot`, `& dotnet test ... --nologo --filter 'Category=ReadmeImages'` and `$LASTEXITCODE` throw. Skeleton is in RESEARCH Pattern 4 (env var set in `try`, cleared in `finally`, post-run check each PNG exists and `LastWriteTime -ge $started`, since a zero-match filter exits 0). ASCII only, no em dashes.

---

### Docs

- `.github/workflows/ci.yml` line 54: comment `and all 9 pass there` becomes the new UI test count (9 + 1 new test).
- `AUDITING.md` "Building and releasing" list (lines 103-111): add a bullet in the same bold-name style, e.g. `- **`make-readme-images.ps1`** runs one UI test offscreen and writes three PNGs to `docs/images`. Developer only, never in the release zip.` Do not change the "26 tags" count (`src` only).
- `README.md`: new `## What it looks like` after the intro paragraph; `### Regenerating the README images` under `## Development` before `### Making a release`; add a `docs/images` bullet to the Development file list. Details in RESEARCH "README changes".

## Shared Patterns

### Offscreen-only rule
**Source:** `WpfHost.Offscreen` (WpfHost.cs lines 83-94) and `CloseQuietly` (ScreenTests.cs 89-93).
**Apply to:** every window the generator opens. Never call `ShowDialog`, `Activate`, `Focus`; set `vm.IsDirty = false` before closing editors.
```csharp
window.WindowStartupLocation = WindowStartupLocation.Manual;
window.Left = -20000; window.Top = -20000;
window.ShowActivated = false; window.ShowInTaskbar = false;
```

### Test isolation
**Source:** WpfHost.cs line 11 (`DisableTestParallelization = true`) and lines 46-49 (`CheckForUpdatesOnLaunch = false`, `WindowLayout.FilePath` in temp). Layout file can be stale, so set window `Width`/`Height` explicitly.

### Style
Allman braces, file-scoped namespace `PrintInvoicer.UiTests`, `/// <summary>` on every class and method, snake_case sentence test names, `_camelCase` private fields, no em dashes, sample names only ("Sample Printing Co.", "Harbor Dive Shop", 555.0100).

## No Analog Found

| File | Role | Reason |
|------|------|--------|
| `docs/images/*.png` | generated asset | No committed images exist yet; output format is defined by `RenderPng` and `InvoicePdf.RenderPages` |
| Env-var gated test output | pattern | No existing test reads an environment variable; use RESEARCH Pattern 4 |
| `[Trait]` filtering | pattern | No existing `[Trait]` in the repo; use `[Trait("Category","ReadmeImages")]` per RESEARCH (verified there) |

## Metadata

**Analog search scope:** `tests/PrintInvoicer.UiTests/`, repo root scripts, `.github/workflows/ci.yml`, `AUDITING.md`
**Files scanned:** 6 read directly, plus RESEARCH/CONTEXT
**Pattern extraction date:** 2026-11-01

# Phase 1: README Showcase - Research

**Researched:** 2026-11-01
**Domain:** Offscreen WPF screenshots, QuestPDF page rendering, xUnit trait filtering, PowerShell wrapper script, README layout
**Confidence:** HIGH (every key behavior below was measured in this session by running probe tests on this repo; the probe file was deleted afterwards and `git status` is clean)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Sample data**
- **D-01:** Extend the existing `SampleShop` (`tests/PrintInvoicer.UiTests/SampleShop.cs`) instead of creating a separate set of showcase data. The tests and the README images use the same data. Existing UI tests must still pass. Check `Job_list_totals_follow_the_filters` and the other `ScreenTests` asserts after any change to the data.
- **D-02:** The job list shows 3 to 4 jobs, covering draft, finalized and paid. Today SampleShop has an FDM draft, a resin draft and a finalized FDM job, but nothing paid. Add a paid state, either by marking a job paid or by adding a 4th job, whichever disturbs the existing tests less.
- **D-03:** The editor image and the invoice image both feature the FDM "Wire Brush Inserts" job (the Bambu file, the friends-and-family discount, customer-supplied filament), because it shows the most pricing features.
- **D-04:** The invoice image is a Final invoice that is not marked paid (no PAID line). It shows what a customer usually receives.
- **D-05:** Only sample names: "Sample Printing Co.", "Harbor Dive Shop", 555.0100 numbers. No personal data in the images.

**Image look and size**
- **D-06:** Render the window images at 2x (192 DPI) so they stay sharp when GitHub scales them. Today `WpfHost.Screenshot` renders at 96 DPI only, so it needs a scale or DPI option, or a sibling helper.
- **D-07:** Capture the main window at its default 1100x680. Size the job editor at its default size.
- **D-08:** Capture the window content only, as `RenderTargetBitmap` does today, in the app's dark Fluent theme. No drawn title bar, shadow or frame.
- **D-09:** The invoice image is the full page 1 at about 110 DPI (roughly 935x1210 px for letter size), from `InvoicePdf.RenderPages(job, quote, book, InvoiceKind.Final, dpi)`.

### Claude's Discretion
- **The regenerate command:** pick the form. Suggested: a PowerShell script at the repo root, next to `publish.ps1` (for example `make-readme-images.ps1`), that runs one tagged UI test (a trait or category filter). The test writes PNGs into a committed folder such as `docs/images/` only when the script asks for it, for example through an environment variable or an output path. A normal `dotnet test` run and CI must never rewrite the committed images. It must still follow the UI test rules: offscreen through `WpfHost`, no window shown, activated or focused, no dialogs.
- **Stable output:** keep regenerated images identical when nothing changed. Use fixed dates in the sample data (the finalized job already uses 2026-10-30), and avoid anything based on `DateTime.Today`, so a rerun doesn't churn git.
- **README placement and captions:** put the images near the top so a visitor sees them early, but keep the existing "Personal project" and "AI use disclosure" notices intact. Give each image a short caption. Link the invoice image so it opens at full size. Add a short "Regenerating the README images" note under "Development".
- **File size:** keep each PNG reasonably small (a few hundred KB at most). Plain PNG encoding is fine. No extra tools unless they're needed.

### Deferred Ideas (OUT OF SCOPE)
- A render-comparison test for old invoices (C18 / FIX2-06) could reuse this generator. It's v2, not this phase.
- Screenshots of the printers / filaments / settings screens: out of scope by the earlier decision.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| SHOW-01 | README image of the main job list with draft, finalized and paid jobs | Needs a 4th (finalized + paid) job and fixed created dates; `JobRowViewModel.Status` text shows "Draft" / "Invoiced Oct 30" / "Paid Oct 27". See "Sample data changes". |
| SHOW-02 | README image of the job editor (plates, filament use, price breakdown) | `JobEditorWindow` on the Wire Brush draft; capture the window's root visual, not `Window.Content` (cropping bug found). See "Pitfall 1". |
| SHOW-03 | README image of invoice page 1 | `InvoicePdf.RenderPages(...)` at 110 dpi gives 935x1210 px, byte-stable; needs an in-memory finalized copy of the Wire Brush job. See "Pattern 3". |
| SHOW-04 | One command rebuilds all images offscreen from sample data | `make-readme-images.ps1` runs `dotnet test --filter "Category=ReadmeImages"` with an env var naming the output folder. See "Pattern 4" and "Pitfall 4". |
| SHOW-05 | README explains how to regenerate | New "Regenerating the README images" subsection under Development. See "README changes". |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

- UI tests run offscreen only: never show, activate or focus a window, never trigger a dialog. Use `WpfHost`. Applies to this generator.
- No personal data in code, tests, docs or images: sample names only ("Harbor Dive Shop", "Sample Printing Co.").
- Writing style in docs, comments and messages: plain English, no em dashes (also no en dashes).
- Comments: purpose comments on classes, methods and non-obvious blocks; none on plain lines.
- Outside access: any new line that reads, writes or deletes a file gets an `// AUDIT:` comment and a row in `AUDITING.md`. Existing test code (for example `File.WriteAllBytes` in `ScreenTests.cs`) has no AUDIT comments, and the AUDITING.md count of 26 tags is `git grep -n "AUDIT:" -- src` (verified: 26 in `src`, 0 in `tests`). See "AUDITING.md treatment".
- Test names are snake_case sentences, one main type per file named for the type, file-scoped namespaces, Allman braces, `_camelCase` private fields.
- Finalized invoices never change: this phase must not change app behavior or pricing. Nothing in `src/` needs to change.
- Schema changes are not needed in this phase.
- GSD workflow: edits go through GSD commands (the executor handles this).

## Summary

The whole phase is test-project and docs work. No file under `src/` needs to change, so none of the "finalized invoices never change", schema or AUDIT-in-app rules are touched. The pieces already exist: `WpfHost` renders windows offscreen, `SampleShop` seeds sample data, and `InvoicePdf.RenderPages` returns PNG bytes. I ran all three through a throwaway probe test and confirmed they produce stable, reasonably sized output: main window at 2x is about 181 KB, the editor at 2x about 409 KB, and invoice page 1 at 110 DPI is 935x1210 px and 172 KB. Rendering the same thing twice gave identical bytes, and two separate `dotnet test` processes gave identical bytes for 18 of 19 existing screenshots (the one difference, `main-settings.png`, is the Settings tab, which is out of scope).

Four things in the existing code will bite the planner if not handled, and none are in CONTEXT.md:
1. **`WpfHost.Screenshot` crops the job editor.** It renders `window.Content`, which for `JobEditorWindow` is a `DockPanel` with `Margin="12"`. The image comes out 1280x757 (at 1x) instead of the 1304x781 client area, so the bottom row of buttons and the right edge are cut off. Render the window's visual root (`VisualTreeHelper.GetChild(window, 0)`, a `Border`) instead; the probe shows it gives a complete image.
2. **Dates leak in.** `Job.CreatedDate` defaults to `DateTime.Today`, and the list's Date column shows `invoice date ?? created date`. Today's draft rows show "2026-11-01", so the image would change every day. Set fixed `CreatedDate` values on every sample job.
3. **A stale layout file changes the main window size.** `WindowLayout.FilePath` points at `%TEMP%\PrintInvoicer UI tests\window-layout.json`, which is never cleaned. On this machine it holds `"Main"` at 1100x1150, so a fresh `MainWindow` opens 1150 tall (probe: `window 1100x1150`). The existing test hides this by setting `main.Height = 680` before its screenshot. The generator must set Width and Height explicitly.
4. **"Paid" needs a finalized job.** `JobRowViewModel.Status` shows "Draft" for any non-finalized job even if `Paid` is true, so a paid state requires a finalized job. Marking the existing finalized job paid would leave no "invoiced, unpaid" row. A 4th job is the better choice, and it only changes three existing asserts.

**Primary recommendation:** Add a 4th finalized-and-paid job and fixed dates to `SampleShop`; add `WpfHost.RenderPng(window, scale)` that renders the window's root visual; add one `[Trait("Category","ReadmeImages")]` test in a new `ReadmeImageTests.cs` that always renders into the bin `screens` folder and also copies into `$env:PRINTINVOICER_README_IMAGES` when set; add a root `make-readme-images.ps1` that sets that variable, runs `dotnet test --filter "Category=ReadmeImages"`, and then checks the three PNGs were rewritten (a zero-match filter exits 0).

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Sample data for images | Test project (`SampleShop`) | Core (`Database`, `JobBuilder`, `InvoiceActions.Finalize`) | Data is seeded through the same public Core/App calls the tests already use |
| Window rendering to PNG | Test project (`WpfHost`) | App (windows, theme) | Only the test host has the offscreen STA thread and loaded app resources |
| Invoice page rendering | App (`InvoicePdf.RenderPages`) | Test project (calls it) | Already public; no change needed |
| Choosing when to write into `docs/images` | Test (env var gate) | Script (sets the env var) | Keeps normal `dotnet test` and CI from touching committed files |
| One-command entry point | Root PowerShell script | `dotnet test` filter | Matches `publish.ps1` style |
| Documentation | `README.md`, `AUDITING.md` | `.github/workflows/ci.yml` comment | Visitor-facing and audit-facing text |

## Standard Stack

No new packages. Everything uses what is already referenced.

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| xUnit | 2.9.3 | Test runner and `[Trait]` | Already used; `[Trait("Category","ReadmeImages")]` is filterable with `--filter "Category=ReadmeImages"` (verified by running it) [VERIFIED: tests/PrintInvoicer.UiTests/PrintInvoicer.UiTests.csproj:13-15 `Include="xunit" Version="2.9.3"`] |
| WPF `RenderTargetBitmap` + `PngBitmapEncoder` | .NET 10 | Window screenshots | Already used by `WpfHost.Screenshot` |
| QuestPDF | 2026.9.1 | Invoice page PNG | `InvoicePdf.RenderPages` already wraps `GenerateImages` with `ImageFormat.Png` [VERIFIED: src/PrintInvoicer.App/Pdf/InvoicePdf.cs:60-63] |
| Windows PowerShell 5.1 | built in | Wrapper script | Same as `publish.ps1` [ASSUMED: powershell.exe is present on every Windows 10/11 install] |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Env var gate | `dotnet test -- RunSettings` parameters, or a custom MSBuild property | More plumbing; the env var passes through to the test host (verified: probe read `PROBE_ENV=hello`) |
| Always-run generator test that copies only when asked | A test skipped unless the env var is set | xUnit 2.9.3 has no built-in dynamic skip (no `Assert.Skip`; that is xUnit v3). Always rendering keeps the test meaningful in CI (it fails if sample data stops rendering) and needs no extra package [ASSUMED: no `Assert.Skip` in xUnit 2.9.3, from training knowledge; not needed by this design] |

**Installation:** none.

## Package Legitimacy Audit

No external packages are added in this phase, so the legitimacy gate does not apply.

| Package | Registry | Age | Downloads | Source Repo | Verdict | Disposition |
|---------|----------|-----|-----------|-------------|---------|-------------|
| (none added) | - | - | - | - | - | - |

**Packages removed due to [SLOP] verdict:** none
**Packages flagged as suspicious [SUS]:** none

## Architecture Patterns

### Data flow

```
make-readme-images.ps1
   |  sets PRINTINVOICER_README_IMAGES=<repo>\docs\images
   |  runs: dotnet test tests\PrintInvoicer.UiTests --filter "Category=ReadmeImages"
   v
ReadmeImageTests (one Fact, STA via WpfHost.Run)
   |  new SampleShop()  -> temp db: 4 jobs with fixed dates, App.Db set
   |
   +--> MainWindow (offscreen, Width=1100, Height=680) --RenderPng(scale 2)--> main-jobs bytes
   +--> JobEditorWindow(FDM draft, offscreen, 1320x820) --RenderPng(scale 2)--> job-editor bytes
   +--> in-memory finalized copy of FDM job --InvoicePdf.RenderPages(Final, 110)[0]--> invoice bytes
   |
   v
 always:   <bin>\screens\readme\*.png      (CI artifact, proves it renders)
 if env var set: copy the same bytes into docs\images\*.png
   |
script: checks each docs\images file exists and was rewritten after the script started
```

### Recommended file changes

```
tests/PrintInvoicer.UiTests/
  SampleShop.cs          # 4th job (finalized + paid), fixed CreatedDate / PrintedDate / PaidDate
  ScreenTests.cs         # update 3 asserts: job count 3 -> 4, "of 3 jobs" x2, finalized filter now 2 jobs
  WpfHost.cs             # add RenderPng(window, scale) returning bytes; Screenshot delegates to it
  ReadmeImageTests.cs    # NEW: the tagged generator test
make-readme-images.ps1   # NEW, repo root, next to publish.ps1
docs/images/             # NEW, committed: main-jobs.png, job-editor.png, invoice-page-1.png
README.md                # images section near top + "Regenerating the README images" under Development
AUDITING.md              # one bullet under "Building and releasing"
.github/workflows/ci.yml # comment says "all 9 pass there"; update the count after adding tests
```

### Sample data changes (SampleShop)

Verified current state, `SampleShop.cs` (read this session): FDM job `Title = "Wire Brush Inserts"`, `InvoiceNumber = "2026-001"`, `Notes = "Sample notes"`; resin job `Title = "Regulator clips"`, `InvoiceNumber = "2026-002"`; finalized job `Title = "Tube caps"`, `InvoiceNumber = "2026-003"`, finalized with `new DateTime(2026, 10, 30)`.

Verified `Job` members (src/PrintInvoicer.Core/Models/Job.cs): `public DateTime CreatedDate { get; set; } = DateTime.Today;`, `public bool Printed { get; set; }`, `public DateTime? PrintedDate { get; set; }`, `public bool Paid { get; set; }`, `public DateTime? PaidDate { get; set; }`.

Recommended changes:

| Job | Created | Invoice date | State | Why |
|-----|---------|--------------|-------|-----|
| Wire Brush Inserts (FDM) | 2026-10-31 | none | Draft | Used by editor and invoice images; tests need it editable |
| Regulator clips (resin) | 2026-10-31 | none | Draft | Resin editor test needs it editable |
| Tube caps (existing) | 2026-10-29 | 2026-10-30 (already) | Finalized, unpaid, printed | "Invoiced Oct 30" |
| NEW paid job, `InvoiceNumber = "2026-004"` | 2026-10-20 | 2026-10-22 | Finalized, `Paid = true`, `PaidDate = 2026-10-27`, printed | "Paid Oct 27" |

- The list is `ORDER BY created_date DESC, id DESC` [VERIFIED: src/PrintInvoicer.Core/Data/Database.cs:638 `... FROM jobs ORDER BY created_date DESC, id DESC`], so these dates give the order draft, draft, invoiced, paid (clips above Wire Brush on the tie, because of `id DESC`).
- Build the 4th job from a manual FDM plate so the Pieces column does not repeat "Wire Brush Insert" or "Cylindex Tube Cap": `JobBuilder.ManualPlate(fdm)` adds one `PlateFilament { SlotId = 1 }` [VERIFIED: src/PrintInvoicer.Core/Slicing/JobBuilder.cs:106-112]; set `SlicedSeconds`, `Filaments[0].SlicedGrams`, `Filaments[0].FilamentId = Db.GetFilaments().First().Id`, `PartsPerPlate`, and add `new JobPart { Name = "...", Plates = { manual } }` (same shape the resin "Hose clip" part already uses). Pick a part name that does not contain "clips" (the keyword test searches every column, including Pieces).
- Finalize with `InvoiceActions.Finalize(Db, job, new DateTime(2026, 10, 22), redate: false)` then set `Paid`/`PaidDate` and `Db.SaveJob(job)`. Hand-set `InvoiceNumber` so `AllocateInvoiceNumber` is not called.
- Set `CreatedDate` before the first `Db.SaveJob`. Expose `PaidJob` next to `FinalizedJob`.
- Optional: set `Printed = true` with a fixed `PrintedDate` on the two finalized jobs so the Printed column shows ticks. No existing test asserts on Printed [VERIFIED: grep of tests/PrintInvoicer.UiTests/*.cs, no match]. Only do it if the planner wants a more lived-in look; it changes the "N job(s) to print" summary text.
- Optional: change `Notes = "Sample notes"` to a more natural sample sentence; no test asserts it (it shows on the invoice and editor).

### Existing asserts that must be updated (ScreenTests.cs, read this session)

| Test | Current assert | Needed change |
|------|----------------|---------------|
| `Main_window_tabs_load_without_binding_errors` | `Assert.Equal(3, main.ViewModel.Jobs.Count);` | `4` |
| `Job_list_totals_follow_the_filters` | `vm.StageFilter = StageFilter.Finalized; Assert.Equal("Showing 1 of 3 jobs", vm.VisibleCount);` then expects the total of the one finalized job | Two finalized jobs now: `"Showing 2 of 4 jobs"` and sum both quotes. Or add `vm.PaidFilter = PaidFilter.Unpaid;` to keep the single-job expectation |
| same test | `vm.Keywords = "clips"; Assert.Equal("Showing 1 of 3 jobs", ...)` | `"Showing 1 of 4 jobs"` |
| `Every_invoice_kind_renders_for_every_job` | loops `_shop.Db.GetJobs()` | No change; it now covers the 4th job too (writes `invoice-2026-004-*.png` to the screens folder) |
| `Finalized_job_window_is_locked_at_its_prices` | uses `_shop.FinalizedJob` | No change |

The first line of the filter test, `decimal all = vm.Jobs.Sum(r => r.Total); Assert.Equal("Total " + Format.Money(all), vm.VisibleTotal);`, still holds.

### Pattern 1: Render the window's root visual at a scale

**What:** A `WpfHost.RenderPng(Window window, double scale = 1)` that renders the Window's first visual child (a `Border` covering the whole client area including element margins) and returns PNG bytes. `Screenshot` can call it, which also fixes the cropped editor in the existing CI screenshots (nothing compares against them).
**When to use:** Every image in this phase.
**Example (the probe code that produced the verified output):**
```csharp
// Probe result: for MainWindow the root Border is 1084x641 at Height=680; for JobEditorWindow it is 1304x781.
public static byte[] RenderPng(Window window, double scale = 1)
{
    Settle(window);
    var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
    int w = Math.Max(1, (int)(root.ActualWidth * scale));
    int h = Math.Max(1, (int)(root.ActualHeight * scale));
    var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
    rtb.Render(root);
    var enc = new PngBitmapEncoder();
    enc.Frames.Add(BitmapFrame.Create(rtb));
    using var ms = new MemoryStream();
    enc.Save(ms);
    return ms.ToArray();
}
```
Measured at scale 2: main window (680 tall) 2168x1282 px, 181,253 bytes; editor 2608x1562 px, 409,153 bytes. No title bar or frame is included (the root Border is the client area), which matches D-08. The window `Background` is applied by the root Border, so the old separate `DrawRectangle(window.Background...)` step is not needed. [VERIFIED: probe run this session; images viewed]

### Pattern 2: Open windows exactly like ScreenTests does

```csharp
// Source: tests/PrintInvoicer.UiTests/ScreenTests.cs (OpenEditor, CloseQuietly)
var vm = new JobEditorViewModel(_shop.Db, _shop.Reload(_shop.FdmJob)) { ShowNotes = _ => { } };
var window = WpfHost.Offscreen(new JobEditorWindow(vm));
...
vm.IsDirty = false;   // avoids the "Save changes?" MessageBox on close
window.Close();
```
For the main window: `var main = WpfHost.Offscreen(new MainWindow()); main.Width = 1100; main.Height = 680; WpfHost.Settle(main);` before rendering. Set the size explicitly (see Pitfall 3). For the editor set `Width = 1320; Height = 820` the same way (current XAML default: `Height="820" Width="1320"` [VERIFIED: src/PrintInvoicer.App/JobEditorWindow.xaml:4]).

### Pattern 3: Invoice page from an in-memory finalized copy

A Final invoice built from a job that is not `Finalized` prints "Date not finalized" (seen in the existing `invoice-2026-001-final.png`: the header code is `if (job.Finalized && job.InvoiceDate is DateTime d) t.Span(d.ToString("MMMM d, yyyy")); else t.Span("not finalized").Italic();` [VERIFIED: src/PrintInvoicer.App/Pdf/InvoicePdf.cs, Header]). The editor image needs the Wire Brush job to be a draft, so make the invoice from a reloaded copy that is finalized in memory only and never saved:

```csharp
var job = _shop.Reload(_shop.FdmJob);
job.FinalizeInvoice(new DateTime(2026, 10, 30), redate: false);   // in memory only, no Db.SaveJob
var book = _shop.Db.LoadPriceBook();
var jobBook = PriceSnapshot.For(job, book);     // no snapshot on this copy, so it returns the current book
var quote = PricingCalculator.Calculate(job, jobBook);
byte[] page1 = InvoicePdf.RenderPages(job, quote, jobBook, InvoiceKind.Final, 110)[0];
```
`PriceSnapshot.For` returns `current` unless `job.Finalized && job.PriceSnapshot is string s` [VERIFIED: src/PrintInvoicer.Core/Pricing/PriceSnapshot.cs `For`], so the total equals the editor's $23.37. Probe result: 1 page, 935x1210 px, 172,063 bytes, identical on two renders, shows "Date October 30, 2026", "Terms Net 30", no PAID line (D-04), the plate thumbnail, savings box and Notes box. `InvoiceKind` is `{ Preview, Final, Internal }` [VERIFIED: src/PrintInvoicer.App/Pdf/InvoicePdf.cs:10].

### Pattern 4: Env-var gated copy, tag, and script

Test skeleton (new file `ReadmeImageTests.cs`):
```csharp
[Trait("Category", "ReadmeImages")]
public sealed class ReadmeImageTests : IDisposable
{
    // Set by make-readme-images.ps1 only; a normal dotnet test run never has it, so committed images are never rewritten.
    private const string OutputVariable = "PRINTINVOICER_README_IMAGES";

    [Fact]
    public void Readme_images_are_rebuilt_from_the_sample_shop() => WpfHost.Run(() =>
    {
        // ... render three byte[] as above, assert PNG signature and expected pixel sizes ...
        Save("main-jobs.png", mainBytes);
        Save("job-editor.png", editorBytes);
        Save("invoice-page-1.png", invoiceBytes);
    });
}
```
`Save` always writes to `Path.Combine(WpfHost.ScreenshotFolder, "readme", name)` and, if `Environment.GetEnvironmentVariable(OutputVariable)` is non-empty, also to that folder (create it). Always write (do not skip unchanged files) so the script's freshness check works; git ignores identical bytes.

Script skeleton (`make-readme-images.ps1`, ASCII only, Windows PowerShell 5.1, same header/param/`$ErrorActionPreference`/`$PSScriptRoot`/`& dotnet` + `$LASTEXITCODE` style as `publish.ps1`):
```powershell
# Rebuilds the README images (docs\images) from the sample shop. Offscreen: no window appears.
# Works in Windows PowerShell 5.1.
param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'docs\images')
)
$ErrorActionPreference = 'Stop'
$started = Get-Date
$env:PRINTINVOICER_README_IMAGES = $OutDir
try {
    & dotnet test (Join-Path $PSScriptRoot 'tests\PrintInvoicer.UiTests') --nologo --filter 'Category=ReadmeImages'
    if ($LASTEXITCODE -ne 0) { throw 'The image test failed; images not updated.' }
}
finally { Remove-Item Env:\PRINTINVOICER_README_IMAGES -ErrorAction SilentlyContinue }
foreach ($name in 'main-jobs.png', 'job-editor.png', 'invoice-page-1.png') {
    $f = Join-Path $OutDir $name
    if (-not (Test-Path $f) -or (Get-Item $f).LastWriteTime -lt $started) { throw "$name was not rebuilt." }
}
Write-Host "Rebuilt the README images in $OutDir"
```
Use `git status docs/images` afterwards to see whether anything actually changed.

### Anti-Patterns to Avoid
- **Using `WpfHost.Screenshot` as it is for the editor:** it crops the right and bottom 12 px margin.
- **Relying on the default window size:** a stale `window-layout.json` can change it.
- **Calling `Db.SaveJob` on the finalized copy used for the invoice:** it would turn the editor job into a locked finalized job and break `Fdm_job_window_shows_the_same_total_as_the_invoice`.
- **Gating with `Skip`:** no dynamic skip in xUnit 2.9.3; use the copy-only-when-asked design.
- **Putting the generator in `src/`:** it would need an `// AUDIT:` and an `AUDITING.md` row for shipped code and adds nothing for users.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Invoice page raster | Own PDF-to-PNG step | `InvoicePdf.RenderPages(..., dpi)` | Already returns PNG bytes from the same layout as the PDF; verified byte-stable |
| Window raster | Windows screen capture, a title-bar compositor | `RenderTargetBitmap` of the root visual | Offscreen, no focus, no desktop dependency |
| Image compression | pngquant/optipng step | Plain WPF PNG | Sizes measured at 181 KB, 409 KB, 172 KB; CONTEXT says no extra tools |
| Test selection | Custom runner | xUnit `[Trait]` + `--filter` | Verified working |
| Fonts for the invoice | Installing fonts | Built-in Lato | `page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Lato"))` ships inside QuestPDF, per the code comment |

**Key insight:** this phase is wiring, not new rendering code.

## Runtime State Inventory

Not a rename or migration phase. Omitted. One piece of leaked runtime state is relevant though and is covered as Pitfall 3 (`%TEMP%\PrintInvoicer UI tests\window-layout.json`).

## Common Pitfalls

### Pitfall 1: Cropped editor image
**What goes wrong:** `WpfHost.Screenshot` renders `window.Content`. For `JobEditorWindow` that is a `DockPanel Margin="12"`, so the render is offset by the margin and sized to `ActualWidth x ActualHeight` (1280x757), cutting off the bottom button row and the right edge. The existing `editor-fdm.png` shows "Add sliced files..." half cut at the bottom and "Close" cut at the right.
**Why it happens:** `ActualWidth` excludes margin; rendering an element with a layout offset shifts it.
**How to avoid:** render `VisualTreeHelper.GetChild(window, 0)` (the client-area `Border`). Probe gave a full 1304x781 client image with all buttons intact.
**Warning signs:** image width equals window width minus frame minus 24.

### Pitfall 2: Dates that move with the calendar
**What goes wrong:** Draft rows show `CreatedDate`, which defaults to today.
**How to avoid:** assign fixed `CreatedDate`, `PrintedDate`, `PaidDate` and finalize dates in `SampleShop`. I grepped `src` for `DateTime.Today/Now/UtcNow`: the display paths that matter are only on user actions (finalize, tick Paid or Printed, date-range filters) and never run while rendering; nothing renders "days ago" text [VERIFIED: grep of src, results listed in this session]. `JobEditorViewModel.cs:458` and `MainViewModel.cs:372` are inside finalize commands, which the generator never runs.
**Warning signs:** a regenerated image differs only in a date string.

### Pitfall 3: Stale window layout file
**What goes wrong:** `WindowLayout.Track` (called in the `MainWindow` and `JobEditorWindow` constructors) applies saved size from `WindowLayout.FilePath`, which `WpfHost.Start` sets to `%TEMP%\PrintInvoicer UI tests\window-layout.json` and never deletes. On this machine that file contains `"Main": { ... "Width": 1100, "Height": 1150 ... }` [VERIFIED: file read this session], and the probe saw `Main dpi 1 window 1100x1150`. A CI runner has no such file, so results would differ between the maintainer's PC and CI.
**How to avoid:** set `Width`/`Height` explicitly on both windows before rendering (the existing tab test does the same for the main window with `main.Height = ... 680`). Alternatively point `WindowLayout.FilePath` at a nonexistent file for the generator and restore it afterwards.
**Warning signs:** main image is taller than 641 px at 1x.

### Pitfall 4: `dotnet test --filter` with zero matches succeeds
**What goes wrong:** a typo in the trait name, or a renamed test, prints "No test matches the given testcase filter" and exits 0 [VERIFIED: ran `--filter "Category=NoSuchThing"`, exit code 0]. The script would then report success with no images written.
**How to avoid:** the script's post-run check that each target file exists and was modified after the script started.

### Pitfall 5: The generator runs in CI and in `publish.ps1`
**What goes wrong:** both run all UI tests, so the new test runs there too. With the env var unset it only writes under `bin\...\screens\readme`, which is fine and gives CI an artifact; but it adds a few seconds and the ci.yml comment "all 9 pass there" becomes stale.
**How to avoid:** update that comment count. Do not add the env var to any workflow.

### Pitfall 6: Output is stable on one machine, not guaranteed across machines
**What goes wrong:** the window images depend on the machine's fonts, display scaling (window frame width in DIPs: this PC reports 16 px, so a 1100-wide window has a 1084 client) and OS language ("Select a date" in the date pickers, `MMM d` in the Status column use the current culture; `Format.Money` is fixed to en-US). This PC reports `dpi 1` (100%).
**How to avoid:** document in the README note that images should be regenerated on the same kind of setup, and that a rerun on the same machine gives no git diff. Optional hardening: set the UI thread culture to `en-US` inside the generator and restore it. [ASSUMED: behavior on other machines; only this machine was measured.]

### Pitfall 7: Parallel and shared state
**What goes wrong:** `SampleShop` sets the static `App.App.Db`. Tests are serialized (`DisableTestParallelization = true`, WpfHost.cs:11), so a second test class is safe, but the generator class must own its own `SampleShop` and `Dispose` it (mirror `ScreenTests` constructor/`Dispose`).

### Pitfall 8: Images contain no personal data only if the sample data is clean
**How to avoid:** the images show business "Sample Printing Co.", "555.0100", customer "Harbor Dive Shop", file names `Wire_Brush_Insert.gcode.3mf`, printer names from `Printer.NewDefault(...)`. File paths are not displayed on the customer invoice or in these views (only `Path.GetFileName` is used on the internal sheet). Add a manual look at the three final images in the human-verify step (config has `human_verify_mode: end-of-phase`).

## Code Examples

See Patterns 1 to 4 above. A size/shape assertion worth including in the generator test, so a broken render fails loudly (measured values in the sample environment, so assert ratios and signatures rather than exact byte counts):
```csharp
Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);   // same check ScreenTests uses
var frame = BitmapFrame.Create(new MemoryStream(invoiceBytes));
Assert.Equal(935, frame.PixelWidth);    // letter at 110 dpi; 8.5 x 110 = 935
Assert.Equal(1210, frame.PixelHeight);  // 11 x 110 = 1210
```

## README changes (SHOW-01 to SHOW-05)

README structure today (read this session): H1 "PrintInvoicer", one intro paragraph, `> [!IMPORTANT] **Personal project, shared as-is.**`, `> [!WARNING] **AI use disclosure.**`, then `## Install or update`, `## Setting up`, `## Using it`, `## How prices are calculated`, `## Data`, `## Development` (with `### Making a release` inside), `## License`.

- Add a `## What it looks like` section **immediately after the intro paragraph and before the two notices**, so the images appear within the first screen. This pushes the notices down about 20 lines but leaves their text untouched. (If the user prefers notices first, put the section directly after the WARNING block; flag this as a one-line choice for the planner.) Three images, each with a one-sentence caption, plain English, no em dashes.
- Use relative paths: `docs/images/main-jobs.png`, `docs/images/job-editor.png`, `docs/images/invoice-page-1.png`. For the 2x window images, an HTML `<img src="..." alt="..." width="...">` pins the displayed width; GitHub also shrinks wide images to the container by itself [ASSUMED: GitHub's maximum README content width, roughly 800 to 900 px; not measured]. Wrap the invoice image in a link to itself so it opens at full size: `[![Page 1 of a sample invoice](docs/images/invoice-page-1.png)](docs/images/invoice-page-1.png)`. Give every image real alt text.
- Under `## Development`, after the CI paragraph and before `### Making a release`, add `### Regenerating the README images` with: the command `.\make-readme-images.ps1` (Windows PowerShell, from the repo root, needs the .NET 10 SDK, app does not need to be closed), a sentence that it runs one UI test offscreen so no window appears, that it rewrites the three PNGs in `docs/images`, that normal `dotnet test` and CI never touch that folder, that the images come from the sample shop in `tests/PrintInvoicer.UiTests/SampleShop.cs` (so change the sample data there), and to check `git status docs/images` and commit the PNGs if they changed.
- Also add a bullet to the Development file list: `docs/images`: the README pictures, rebuilt by `make-readme-images.ps1`.
- The README says nothing yet about the UI test count except "Several hundred automated tests"; no number to update.

## AUDITING.md treatment

`AUDITING.md` describes shipped program behavior plus a "Building and releasing" section for developer and CI scripts (read this session): it lists `publish.ps1` and the two workflows, and says "The test projects use xUnit; tests are not part of the released program." The "Start here" text says "There are 26 tags", counted by `git grep -n "AUDIT:" -- src`. Existing test code writes files with no AUDIT comment and has no row.

Recommendation:
- Add one bullet under "Building and releasing" for `make-readme-images.ps1`: developer-only, runs one UI test offscreen, writes three PNGs to `docs/images`, never runs on users' machines and is not in the release zip.
- Put a short `// AUDIT: file write (test only), copies the rendered README images to the folder named by PRINTINVOICER_README_IMAGES.` comment above the single write/copy line in the generator, to follow the CLAUDE.md "any new line that writes a file" rule literally. It sits in `tests/`, so the `-- src` count of 26 stays correct and no "Files written" table row is required. If the planner would rather follow existing test practice (no comment), the bullet alone is enough.

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|------------------|--------|
| `WpfHost.Screenshot` at 96 DPI of `window.Content` | `RenderPng(window, scale)` of the window root | Complete client area, sharp at 2x |
| Hand-made screenshots for READMEs | Generated from seeded data | Images stay in step with the UI |

Nothing deprecated is involved.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | GitHub README renders images up to roughly 800 to 900 px wide and scales larger ones down | README changes | Images look smaller or larger than intended; fix with a `width` attribute |
| A2 | Output is not guaranteed byte-identical on a different PC (fonts, display scaling, language) | Pitfall 6 | A second maintainer sees a diff on first regeneration; harmless |
| A3 | `powershell.exe` 5.1 exists on every target Windows machine | Standard Stack | Script would not start; `pwsh` also works with this script (it uses nothing 5.1 specific) |
| A4 | xUnit 2.9.3 has no `Assert.Skip` / dynamic skip | Alternatives | None for this design, which avoids skipping |

## Open Questions (RESOLVED)

1. **Should the images go before or after the "Personal project" and "AI use disclosure" notices?**
   - What we know: CONTEXT says near the top, notices intact.
   - Recommendation: before the notices (right after the intro paragraph). Planner can flip it in one move if the user disagrees.
   - **RESOLVED:** before the notices, right after the intro paragraph, with the notice text byte-identical. Adopted by
     plan 01-01 Task 1 step 5 (the `## What it looks like` section and its placement check) and recorded as planner
     choice A-4 in plan 01-02.
2. **Should `WpfHost.Screenshot` itself be changed to render the root visual?**
   - What we know: this fixes the cropped `editor-*` CI screenshots; nothing compares against screenshots.
   - Recommendation: yes, delegate `Screenshot` to `RenderPng(window, 1)`. The alternative (leave it, add a sibling) is equally valid if the planner wants zero change to existing artifacts.
   - **RESOLVED:** yes. `Screenshot` writes the bytes of `RenderPng(window)` (scale 1), so the CI `editor-*` screenshots
     are no longer cropped. Adopted by plan 01-01 Task 1 step 1.
3. **Printed ticks and a nicer Notes sentence in the sample data.** Cosmetic; planner's call.
   - **RESOLVED:** both are added. The two finalized jobs are ticked Printed with fixed dates (Tube caps printed
     2026-10-30, Dive light mounts printed 2026-10-21), and the Wire Brush Inserts notes read "Black PLA supplied by
     Harbor Dive Shop." to match the customer-supplied slot. Adopted by plan 01-01 Task 2 step 1.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | build, test | yes | 10.0.403 (`dotnet --version`; matches `global.json`) | none needed |
| Windows desktop session for WPF | UI tests | yes | probes ran | none |
| Windows PowerShell | script | yes (Windows 11) | 5.1 [ASSUMED] | `pwsh` |

Missing dependencies: none.

## Security Domain

`security_enforcement` is on, ASVS level 1. This phase adds no app code, network access, authentication, sessions or user input handling.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | none |
| V3 Session Management | no | none |
| V4 Access Control | no | none |
| V5 Input Validation | minimal | The only input is the env var folder path, supplied by the maintainer's own script; create the folder and write fixed file names only (never names taken from input) |
| V6 Cryptography | no | none |
| V8 Data Protection | yes | No personal data in committed images (D-05); manual review of the three PNGs |

### Known Threat Patterns

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Personal data leaking into a committed image (paths, names, customers) | Information disclosure | Sample data only, fixed names, human review at end of phase |
| Test run overwriting committed files | Tampering | Env var gate set only by the script; CI never sets it |
| Network from the app during tests | Information disclosure | `MainWindow.CheckForUpdatesOnLaunch = false` already set in `WpfHost.Start` |
| Dialog or focus on the maintainer's screen | n/a (project rule) | Offscreen `WpfHost.Offscreen`, `IsDirty = false` before close, no `MessageBox` paths called |

## Sources

### Primary (HIGH confidence)
- Repository files read this session: `tests/PrintInvoicer.UiTests/{WpfHost,SampleShop,ScreenTests}.cs`, `PrintInvoicer.UiTests.csproj`, `src/PrintInvoicer.Core/Models/{Job,JobFilter}.cs`, `src/PrintInvoicer.Core/Pricing/PriceSnapshot.cs`, `src/PrintInvoicer.Core/Slicing/JobBuilder.cs` (ManualPlate), `src/PrintInvoicer.Core/Data/Database.cs` (ORDER BY, schema), `src/PrintInvoicer.App/{InvoiceActions,WindowLayout,MainWindow.xaml(.cs),JobEditorWindow.xaml(.cs)}`, `ViewModels/MainViewModel.cs` (`Status`, totals), `Pdf/InvoicePdf.cs`, `.github/workflows/ci.yml`, `publish.ps1`, `README.md`, `AUDITING.md`, `.gitignore`, `.planning/{REQUIREMENTS,STATE,ROADMAP}.md`, `.planning/codebase/TESTING.md`.
- Probe runs on this machine (temporary `ProbeTemp.cs`, removed; `git status` clean): window and client sizes, 2x render sizes, invoice 935x1210, in-run and cross-process byte stability, trait filter, env var pass-through, zero-match filter exit code 0.

### Secondary / Tertiary
- None. No external documentation was needed; behavior was verified directly in the repo. The config has no web/doc search providers enabled, and no libraries beyond the existing ones are involved.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH, nothing new.
- Architecture: HIGH, each piece executed in a probe.
- Pitfalls: HIGH for 1 to 5 and 7 (observed); MEDIUM for 6 and the README width (single machine).

**Research date:** 2026-11-01
**Valid until:** until `WpfHost`, `SampleShop` or the window XAML change (stable codebase; about 30 days)

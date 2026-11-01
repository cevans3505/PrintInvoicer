---
last_mapped_commit: 2df24f47743d36dde2022025fa255f942c9bc7ae
last_mapped_at: 2026-11-01
---
<!-- refreshed: 2026-11-01 -->
# Architecture

**Analysis Date:** 2026-11-01

## System Overview

```text
┌──────────────────────────────────────────────────────────────────────────┐
│                 WPF presentation  (src/PrintInvoicer.App)                 │
│  MainWindow.xaml(.cs)   JobEditorWindow.xaml(.cs)   Settings/Filaments... │
├──────────────────────────────────────────────────────────────────────────┤
│                 ViewModels  (src/PrintInvoicer.App/ViewModels)            │
│  MainViewModel  JobEditorViewModel  PartViewModel  PrintersViewModel     │
│  FilamentsViewModel  SettingsViewModel                                    │
├────────────────────────┬──────────────────────────┬──────────────────────┤
│  PDF output            │  App services            │  Update check        │
│  Pdf/InvoicePdf.cs     │  InvoiceActions.cs       │  UpdateService.cs    │
│  Pdf/InvoicePrinter.cs │  ErrorReport.cs          │                      │
│  Pdf/PlateImages.cs    │  WindowLayout.cs         │                      │
├────────────────────────┴──────────────────────────┴──────────────────────┤
│                 Core domain  (src/PrintInvoicer.Core, no UI)              │
│  Slicing/   SliceFileReader -> BambuFileReader | GooFileReader -> JobBuilder│
│  Pricing/   PricingCalculator, PriceBook, PriceSnapshot, *Matcher        │
│  Models/    Job, JobPart, JobPlate, PlateFilament, Printer, Filament, ...│
├──────────────────────────────────────────────────────────────────────────┤
│                 Persistence  (src/PrintInvoicer.Core/Data)                │
│  Database.cs (hand-written SQL, SQLite)   DatabaseBackup.cs   SeedData.cs │
└────────────────────────────┬─────────────────────────────────────────────┘
                             ▼
      Documents\PrintInvoicer\printinvoicer.db     Documents\PrintInvoicer\Invoices\*.pdf
      Documents\PrintInvoicer\Backups\*.db         %TEMP%\PrintInvoicer previews\*.pdf
```

The solution has three projects plus two test projects:

- `src/PrintInvoicer.Core` (net10.0, no UI, no WPF references): models, pricing, slicing-file readers and SQLite storage.
- `src/PrintInvoicer.App` (net10.0-windows, WPF, output `PrintInvoicer.exe`): windows, view models, PDF rendering, and app-level services. References Core.
- `tests/PrintInvoicer.Tests` (xUnit, references Core only in practice, runs on any OS in CI's unit job) and `tests/PrintInvoicer.UiTests` (xUnit, WPF, Windows only, references App).

## Pattern Overview

**Overall:** Layered desktop application, MVVM in the WPF layer (CommunityToolkit.Mvvm source generators), with a pure domain core that owns pricing, file reading and persistence. There is no dependency injection container and no repository interface: the database class is a concrete type passed by constructor.

**Key Characteristics:**
- The domain core is pure and deterministic. `PricingCalculator.Calculate(job, book)` takes a `PriceBook` (settings, materials, printers, extra costs) and returns a `JobQuote`; it reads and writes nothing.
- Persistence is one concrete class, `Database` (`src/PrintInvoicer.Core/Data/Database.cs`), using hand-written SQL through `Microsoft.Data.Sqlite`. Each operation opens its own connection (`Open()`); foreign keys are enabled per connection so deleting a job cascades to its parts, plates and slots.
- Schema is created and upgraded on every start by `Database.EnsureCreated()` using `CREATE TABLE IF NOT EXISTS` and `AddColumnIfMissing`. This is the only place schema changes happen (see `CLAUDE.md`).
- Finalized invoices are protected by frozen prices. `PriceSnapshot` stores the price inputs as text on the job at finalize time; later pricing for a finalized job restores that snapshot via `PriceSnapshot.For(job, current)` instead of using current rates. `PricingRules.Current` (`src/PrintInvoicer.Core/Pricing/PriceBook.cs`) versions the rules so older snapshots keep old behavior.
- The app is single-process and single-window-owner: `App.Db` is a static property holding the one `Database` instance, set in `App.OnStartup`.
- Slicing-file input is handled entirely in Core. File readers are the only code that parses untrusted files; they throw `InvalidDataException` with user-readable messages.

## Layers

**Core domain models:**
- Purpose: plain data for a job, its parts and plates, materials, printers and fees.
- Location: `src/PrintInvoicer.Core/Models/`
- Contains: `Job.cs` (`Job`, `JobPart`, `JobPlate`, `PlateFilament`, `PlateResin`), `Filament.cs`, `Resin.cs`, `Printer.cs` (with `PrintTechnology`), `ExtraCost.cs` (with `CostBasis`), `AppSettings.cs`, `JobFilter.cs`.
- Depends on: nothing in the solution.
- Used by: every other layer.

**Pricing:**
- Purpose: turn a job and a price book into money, and freeze prices for finalized invoices.
- Location: `src/PrintInvoicer.Core/Pricing/`
- Contains: `PricingCalculator.cs` (quote records `SlotQuote`, `ResinQuote`, `PlateQuote`, `PartQuote`, `JobQuote`, and the static `Calculate` overloads), `PriceBook.cs` (`PriceBook` and `PricingRules`), `PriceSnapshot.cs` (`Capture`, `Restore`, `For`, `Freeze`), `FilamentMatcher.cs` and `ResinMatcher.cs` (guess a material from a slicer profile name).
- Depends on: Models.
- Used by: `MainViewModel`, `JobEditorViewModel`, `InvoiceActions`, `Database.FreezeFinalizedJobs`.

**Slicing (file input):**
- Purpose: read sliced-print files and turn them into job parts and plates.
- Location: `src/PrintInvoicer.Core/Slicing/`
- Contains: `SliceFileReader.cs` (dispatch by extension), `BambuFileReader.cs` (`.gcode.3mf` and `.gcode` from Bambu Studio), `GooFileReader.cs` (ELEGOO `.goo` resin), `SlicedFile.cs` (neutral records `SlicedFile`, `SlicedPlate`, `SlicedFilament`, `SlicedResin`), `SliceLimits.cs` (size and range caps, internal), `JobBuilder.cs` (adds plates to a job, groups them into parts, creates hand-typed plates), `PngWriter.cs` (thumbnail output for `.goo`).
- Depends on: Models, Pricing (matchers).
- Used by: `JobEditorViewModel.AddFiles`.

**Persistence:**
- Purpose: store and load everything, and back it up.
- Location: `src/PrintInvoicer.Core/Data/`
- Contains: `Database.cs` (CRUD for filaments, mappings, printers, resins, extra costs, jobs, settings and invoice numbering), `DatabaseBackup.cs` (daily and manual copies, pruning, restore), `SeedData.cs` (starter materials for a new database).
- Depends on: Models, Pricing (`LoadPriceBook`, `PriceSnapshot`).
- Used by: `App` (startup and backup), all ViewModels, `InvoiceActions`.

**App services:**
- Purpose: cross-cutting WPF-side helpers.
- Location: `src/PrintInvoicer.App/` root files.
- Contains: `InvoiceActions.cs` (finalize, redate prompt, business-details check), `ErrorReport.cs` (shows a plain message and writes details for the developer), `UpdateService.cs` (checks GitHub releases through a shared `HttpClient`), `WindowLayout.cs` (remembers window size and position), `Format.cs` and `Converters.cs` (display helpers and XAML value converters).
- Depends on: Core, WPF.
- Used by: ViewModels and code-behind.

**PDF output:**
- Purpose: build invoices with QuestPDF and print or save them.
- Location: `src/PrintInvoicer.App/Pdf/`
- Contains: `InvoicePdf.cs` (`InvoiceKind` enum with `Preview`, `Final`, `Internal`; `Write` saves a PDF; `RenderPages` returns page images), `InvoicePrinter.cs` (sends pages to the Windows print dialog at `Dpi = 300`), `PlateImages.cs` (plate thumbnails for the PDF).
- Depends on: Core models and pricing, QuestPDF.
- Used by: `JobEditorViewModel` (preview, final, internal buttons) and `MainViewModel.GenerateInvoices`.

**ViewModels:**
- Purpose: state and commands for each window, with no code-behind logic beyond wiring.
- Location: `src/PrintInvoicer.App/ViewModels/`
- Contains: `MainViewModel` (job list, filters, totals, invoice actions), `JobEditorViewModel` (one job: parts, plates, fees, save, invoice buttons), `PartViewModel` (`PartViewModel`, `PlateViewModel`, `SlotViewModel`), `PrintersViewModel`, `FilamentsViewModel`, `SettingsViewModel`.
- Depends on: Core, App services, `App.Db`.
- Used by: the corresponding windows through `DataContext`.

**Views:**
- Purpose: XAML layout and minimal code-behind.
- Location: `src/PrintInvoicer.App/*.xaml`, `*.xaml.cs`; `App.xaml` holds styles and the Fluent dark theme.
- Contains: `MainWindow`, `JobEditorWindow`, `App`.

## Data Flow

### Primary Request Path: Build a job from a sliced file

1. The user picks files in `JobEditorWindow`, which calls `JobEditorViewModel.AddFiles` (`src/PrintInvoicer.App/ViewModels/JobEditorViewModel.cs`, around line 308).
2. Each path goes to `SliceFileReader.Read` (`src/PrintInvoicer.Core/Slicing/SliceFileReader.cs`), which picks `GooFileReader` or `BambuFileReader` by extension and returns a `SlicedFile`.
3. `JobBuilder.AddFile` (`src/PrintInvoicer.Core/Slicing/JobBuilder.cs`) groups plates into `JobPart`s by object name, creates `JobPlate`s, and guesses materials through `FilamentMatcher` or `ResinMatcher` using the remembered mappings from `Database.GetMappings`.
4. The editor recalculates: `PricingCalculator.Calculate(Job, Book)` (`JobEditorViewModel.cs` around line 257) fills `Quote` for the on-screen totals.
5. Save calls `JobEditorViewModel.TrySave` (around line 400), then `Database.SaveJob` (`src/PrintInvoicer.Core/Data/Database.cs`, around line 752), which rewrites the job's parts, plates and slots inside one transaction.

### Primary Request Path: Finalize and produce an invoice

1. `MainViewModel.FinalizeInvoices` (`src/PrintInvoicer.App/ViewModels/MainViewModel.cs`, around line 290) calls `FinalizeAll`, which checks business details (`InvoiceActions.ConfirmBusinessDetails`) and warnings, then asks about re-dating.
2. Each job goes through `InvoiceActions.Finalize` (`src/PrintInvoicer.App/InvoiceActions.cs`): it allocates an invoice number from `Database.AllocateInvoiceNumber`, calls `Job.FinalizeInvoice`, and calls `PriceSnapshot.Freeze`, which stores the price snapshot and `FinalizedTotal`.
3. `Database.SaveJob` writes the job.
4. For the PDF, `MainViewModel.GenerateInvoices` calls `PriceSnapshot.For(job, book)` (so the invoice uses its frozen prices), then `PricingCalculator.Calculate`, then `InvoicePdf.Write` (`src/PrintInvoicer.App/Pdf/InvoicePdf.cs`).
5. The PDF goes to `Documents\PrintInvoicer\Invoices` (or the folder set in Settings). The app opens it with `Process.Start`, or sends `InvoicePdf.RenderPages` output to `InvoicePrinter.Print` when printing.

### Primary Request Path: Startup

1. `App.OnStartup` (`src/PrintInvoicer.App/App.xaml.cs`) sets the QuestPDF Community license, registers unhandled exception handlers that call `ErrorReport.Show`, and creates `App.Db = new Database(Database.DefaultPath)`. The constructor creates the folder, runs `EnsureCreated` and `FreezeFinalizedJobs`.
2. `App.BackUpTodayAsync` runs `DatabaseBackup.DailyIfDue` on a thread-pool task so a slow network folder never delays the window.
3. `new MainWindow()` builds `MainViewModel(App.Db)`. On `Loaded`, it calls `UpdateService.CheckOnStartupAsync` if the setting is on.

### Secondary Flow: Price book for the main list

1. `MainViewModel.Refresh` (around line 155) loads jobs with `Database.GetJobs`, loads one `PriceBook` with `Database.LoadPriceBook`, then builds a `JobRowViewModel` per job with `PricingCalculator.Calculate(job, PriceSnapshot.For(job, book))`.
2. Filters from `JobFilter` (`src/PrintInvoicer.Core/Models/JobFilter.cs`) are applied in `ApplyFilter`, and totals are recalculated in `UpdateVisibleTotals`.

**State Management:**
- Application state lives in ViewModels (`ObservableObject` with `[ObservableProperty]`), and the database is the source of truth. Windows reload from the database after saves (`MainViewModel.AfterEditor`).
- Jobs are edited in memory in `JobEditorViewModel.Job` and only written on save. Unsaved edits are tracked with `IsDirty`.
- There is no in-memory cache of jobs or materials between operations. Each `Database` call opens a fresh SQLite connection.

## Key Abstractions

**`Database`:**
- Purpose: the one persistence gateway.
- Examples: `src/PrintInvoicer.Core/Data/Database.cs`
- Pattern: concrete class, one method per operation, `Exec`, `Scalar<T>` and `Command` helpers for parameters. Money is stored as invariant-culture text.

**`PriceBook`:**
- Purpose: everything needed to price a job, indexed by id, with no I/O.
- Examples: `src/PrintInvoicer.Core/Pricing/PriceBook.cs`
- Pattern: immutable snapshot built by `Database.LoadPriceBook()`; `RulesVersion` selects the rule set.

**`PriceSnapshot`:**
- Purpose: freeze a job's prices at finalize and restore them later, so finalized invoices never change.
- Examples: `src/PrintInvoicer.Core/Pricing/PriceSnapshot.cs`
- Pattern: `Capture` serializes the price inputs to text on `Job.PriceSnapshot`; `Restore` rebuilds a `PriceBook` from it; `For` returns the right book for a job (snapshot if finalized, current otherwise).

**`Job` aggregate:**
- Purpose: one invoice's data in a single tree: `Job` -> `JobPart` -> `JobPlate` -> `PlateFilament` / `PlateResin`.
- Examples: `src/PrintInvoicer.Core/Models/Job.cs`
- Pattern: plain mutable classes with `Id` set by the database. `Job.FinalizeInvoice` owns the finalize state change.

**Quote records:**
- Purpose: pricing results for display and PDF, kept separate from models.
- Examples: `JobQuote`, `PartQuote`, `PlateQuote`, `SlotQuote`, `ResinQuote`, `ExtraCostQuote` in `src/PrintInvoicer.Core/Pricing/PricingCalculator.cs`.
- Pattern: C# `record` types with computed totals.

**Sliced file records:**
- Purpose: a format-neutral view of what a slicer file contains.
- Examples: `src/PrintInvoicer.Core/Slicing/SlicedFile.cs`
- Pattern: `SlicedFile` -> `SlicedPlate` -> `SlicedFilament` / `SlicedResin`. Readers produce these; `JobBuilder` consumes them.

**ViewModel row types:**
- Purpose: display rows for lists.
- Examples: `JobRowViewModel` (`MainViewModel.cs`), `PrinterRowViewModel` and `ExtraCostRowViewModel` (`PrintersViewModel.cs`), `FilamentRowViewModel` and `ResinRowViewModel` (`FilamentsViewModel.cs`).

## Entry Points

**Application entry:**
- Location: `src/PrintInvoicer.App/App.xaml.cs` (`App.OnStartup`). `App.xaml` has no `StartupUri`; the main window is created and shown in code.
- Triggers: launching `PrintInvoicer.exe`, installed through `publish.ps1` or the release zip.
- Responsibilities: license setup, error handlers, database open, backup, main window.

**Main window:**
- Location: `src/PrintInvoicer.App/MainWindow.xaml.cs` (`MainWindow()`).
- Triggers: app start.
- Responsibilities: create `MainViewModel`, open `JobEditorWindow` through the `OpenEditor` delegate, start the update check.

**Job editor:**
- Location: `src/PrintInvoicer.App/JobEditorWindow.xaml.cs`, `JobEditorViewModel.cs`.
- Triggers: New job, Open job (`MainViewModel.NewJob` / `OpenJob`), or double-clicking a row.

**Database creation and upgrade:**
- Location: `Database` constructor and `EnsureCreated` in `src/PrintInvoicer.Core/Data/Database.cs`.
- Triggers: every app start, and every `new Database(...)` in tests.

**Test entry points:**
- Unit tests: `tests/PrintInvoicer.Tests/` (e.g. `PricingRulesTests.cs`, `PriceSnapshotTests.cs`, `DatabaseTests.cs`, `FinalizeTests.cs`).
- Screen tests: `tests/PrintInvoicer.UiTests/ScreenTests.cs`, using `WpfHost.cs` to create windows offscreen.

## Architectural Constraints

- **Threading:** WPF single UI thread. Background work is limited to `App.BackUpTodayAsync` (`Task.Run`) and the update check (`UpdateService`, async `HttpClient`). Both report back through `Dispatcher.BeginInvoke` (`App.xaml.cs`, `MainWindow` `Loaded`). The database is accessed from the UI thread except for the backup task, which uses its own connection.
- **Global state:** `App.Db` (static, `src/PrintInvoicer.App/App.xaml.cs`) and `App.BackupProblem` (static). `WindowLayout` and `UpdateService` also keep static state (`UpdateService.Repository`, the static `HttpClient`). `MainWindow` and `JobEditorWindow` reach `App.Db` directly; ViewModels receive `Database` by constructor.
- **Circular imports:** None. Project references are one-way: App -> Core. Core never references System.Windows or App.
- **Schema rule:** Schema changes only through `EnsureCreated` (`CREATE TABLE IF NOT EXISTS`, `AddColumnIfMissing`, `MovePrintingRatesToPrinter` for one-time data moves).
- **Pricing rule:** Changes to how totals are computed go behind a new `PricingRules` version. Finalized jobs must use their snapshot.
- **Outside access:** Network, process start, printing and file deletes carry an `// AUDIT:` comment and an entry in `AUDITING.md`. Known sites include `Database.Open` (file access), `App.BackUpTodayAsync` (backup write), `MainViewModel.GenerateInvoices` (`Process.Start`), `InvoicePdf` (PDF write), and `UpdateService` (GitHub request).
- **Config location:** Settings are stored in the `settings` table (`Database.LoadSettings` / `SaveSettings`), not in a config file.

## Anti-Patterns

### Static access to the database from windows

**What happens:** `MainWindow.xaml.cs` and `JobEditorWindow` read `App.Db` directly, and about 26 call sites across `src/PrintInvoicer.App` use `App.Db`.
**Why it's wrong:** Windows and their logic cannot be given a different database for tests without changing a global. It also hides dependencies from the constructor signature.
**Do this instead:** Pass `Database` into the view model or window constructor, as `MainViewModel(Database db)` and `JobEditorViewModel(Database db, Job job)` already do. New code should take `Database` as a parameter and not read `App.Db`.

### Fresh connection per call

**What happens:** `Database.Open()` creates a new `SqliteConnection` for each operation (for example in `GetJobs`, `SaveJob`, and each `Save*` method).
**Why it's wrong:** A job list refresh opens several connections, and any multi-step change that spans two public methods is not one transaction.
**Do this instead:** Keep multi-table writes inside one `Database` method with a single connection and transaction (as `SaveJob` does). Do not split a job save across two public calls.

### Business logic in ViewModels that calls the file system or shell

**What happens:** `MainViewModel.GenerateInvoices` writes PDFs and calls `Process.Start` (`MainViewModel.cs`, around line 386 onward); `JobEditorViewModel` also calls `InvoicePdf.Write` and opens files.
**Why it's wrong:** Output and shell calls cannot be tested without a UI host. The AUDIT markers make these sites easy to find, but the logic is not in Core.
**Do this instead:** Keep the decision logic (which jobs, which kind, which book) in ViewModels, and move pure parts such as totals and file names into Core. Keep new file or process calls behind an `// AUDIT:` comment and a row in `AUDITING.md`.

## Error Handling

**Strategy:** Fail with a message the user can act on, log details for the developer, and keep saved data untouched.

**Patterns:**
- Readers throw `InvalidDataException` with user-readable messages for bad input (`SliceFileReader.Read`, `BambuFileReader`, `GooFileReader`). Other exception types are not allowed from readers (see `CLAUDE.md`).
- ViewModels catch `IOException` around PDF writes and report a "PDF is open in another program" message (`MainViewModel.GenerateInvoices`).
- Startup failures are shown by `ErrorReport.Show` and the app calls `Shutdown(1)` (`App.OnStartup`).
- Unhandled UI exceptions are caught by `DispatcherUnhandledException` and shown with `ErrorReport.Show`; the app keeps running.
- Backup failures are stored in `App.BackupProblem` and shown in Settings (`SettingsViewModel.RefreshBackupStatus`).

## Cross-Cutting Concerns

**Logging:** No logging framework. `ErrorReport` (`src/PrintInvoicer.App/ErrorReport.cs`) writes one text file per error to `%LocalAppData%/PrintInvoicer/Errors` and can open the GitHub issues page and that folder.
**Validation:** Input ranges are checked in the readers (`SliceLimits.cs`) and in the models or ViewModels for user entry. Invoice number duplicates are checked by `Database.InvoiceNumberInUse` before save.
**Authentication:** Not applicable. The app is a single-user local program with no accounts.
**Updates:** `UpdateService` fetches the latest GitHub release for `cevans3505/PrintInvoicer` when the setting is on. It opens the release page in the browser with `Process.Start` (`UpdateService.cs`, around line 121) and writes a small state file with `File.WriteAllText` (around line 196). It does not download or install anything.

---

*Architecture analysis: 2026-11-01*

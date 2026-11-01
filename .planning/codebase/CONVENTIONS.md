---
last_mapped_commit: 2df24f47743d36dde2022025fa255f942c9bc7ae
last_mapped_at: 2026-11-01
---
# Coding Conventions

**Analysis Date:** 2026-11-01

## Naming Patterns

**Files:**
- One main type per file, and the file name is the type name: `src/PrintInvoicer.Core/Pricing/PricingCalculator.cs`, `src/PrintInvoicer.App/ViewModels/JobEditorViewModel.cs`, `src/PrintInvoicer.Core/Slicing/GooFileReader.cs`.
- WPF windows are a pair: `src/PrintInvoicer.App/JobEditorWindow.xaml` and `src/PrintInvoicer.App/JobEditorWindow.xaml.cs`.
- Small helper types can share a file when they belong together, e.g. the result records at the top of `src/PrintInvoicer.Core/Pricing/PricingCalculator.cs`.
- Test files are named for the area they cover and end in `Tests.cs`: `PricingTests.cs`, `DatabaseTests.cs`, `FinalizeTests.cs`. Hostile-input suites end in `RobustnessTests.cs`: `BambuRobustnessTests.cs`, `GooRobustnessTests.cs`.
- Shared test helpers have no `Tests` suffix: `tests/PrintInvoicer.Tests/TestFiles.cs`, `tests/PrintInvoicer.Tests/ReaderContract.cs`, `tests/PrintInvoicer.UiTests/WpfHost.cs`, `tests/PrintInvoicer.UiTests/SampleShop.cs`.

**Folders and namespaces:**
- Folders are PascalCase and match the namespace: `src/PrintInvoicer.Core/Pricing/` is `PrintInvoicer.Core.Pricing`, `src/PrintInvoicer.App/ViewModels/` is `PrintInvoicer.App.ViewModels`.
- Use file-scoped namespaces everywhere: `namespace PrintInvoicer.Core.Models;` with no braces.
- Test namespaces are `PrintInvoicer.Tests` and `PrintInvoicer.UiTests`.

**Functions and methods:**
- PascalCase: `Calculate`, `AllocateInvoiceNumber`, `SaveJob`, `DailyIfDue`.
- Async methods end in `Async`: `UpdateService.CheckOnStartupAsync`, `UpdateService.FetchLatestAsync`, `App.BackUpTodayAsync`.
- MVVM commands are private methods with `[RelayCommand]`. The toolkit generates the `...Command` property, so the method `Save()` is used from XAML and tests as `SaveCommand`. Examples: `ClearFilters()` becomes `ClearFiltersCommand` in `src/PrintInvoicer.App/ViewModels/MainViewModel.cs`; `LaterCommand` and `BackUpNowCommand` are used in the UI tests.
- Test methods are named with underscores and read as sentences: `Seeds_filaments_once`, `Invoice_numbers_skip_ones_already_used`, `Half_cent_unit_price_rounds_up_when_hours_are_not_exact_decimals`. Do not use the `ShouldX` or `Given_When_Then` forms.

**Variables and fields:**
- Private instance fields use a leading underscore and camelCase: `_db`, `_connectionString`, `_book`, `_loading`, `_fetch`.
- A field with `[ObservableProperty]` is written as a private `_camelCase` field, and the toolkit generates the public PascalCase property. Example in `src/PrintInvoicer.App/ViewModels/MainViewModel.cs`: `[ObservableProperty] private string _keywords = "";` gives `Keywords`.
- Static readonly fields and constants are PascalCase, including private ones: `private static readonly PropertyInfo[] SettingProps` in `src/PrintInvoicer.Core/Data/Database.cs`, `private const int MaxXmlDepth` in `src/PrintInvoicer.Core/Slicing/BambuFileReader.cs`. `private static readonly CultureInfo Us` in `src/PrintInvoicer.App/Format.cs` follows the same rule. There is no `s_` or `k` prefix.
- Locals and parameters are camelCase.
- Parameter names are used as named arguments where a bare value would be unclear: `db.DeleteFilament(...)`, `InvoiceActions.Finalize(Db, FinalizedJob, date, redate: false)`, `JobBuilder.AddFile(..., printerId: fdm.Id)`.

**Types:**
- Use `record` for immutable data and results: `SlotQuote`, `PlateQuote`, `PartQuote` and `JobQuote` in `src/PrintInvoicer.Core/Pricing/PricingCalculator.cs`; `ReleaseInfo` in `src/PrintInvoicer.App/UpdateService.cs`; `Option<T>` in `src/PrintInvoicer.App/ViewModels/MainViewModel.cs`.
- Use `class` with get/set properties for editable models: `Job`, `JobPart`, `JobPlate`, `Filament`, `Printer`, `Resin`, `AppSettings` under `src/PrintInvoicer.Core/Models/`.
- Enums are PascalCase and their members too: `PrintTechnology.Fdm`, `PrintTechnology.Resin`, `PaidFilter.Unpaid`, `CostBasis.PerPrint`, `InvoiceKind.Final`.
- There are no interfaces in the code base. Test seams are constructor parameters with defaults or delegates, not interfaces. See "Module Design" below.
- Abstract test base classes are used when several suites share builders: `BambuReaderTests` in `tests/PrintInvoicer.Tests/BambuRobustnessTests.cs`.

## Code Style

**Formatting:**
- No `.editorconfig`, `.prettierrc` or `stylecop.json` is present, and no analyzers are configured. Formatting is not enforced by a tool; follow the patterns in the files around you.
- Four-space indentation.
- Allman braces: the opening brace goes on its own line for types, methods, and control blocks.
- Expression-bodied members are used for one-line members and lambdas: `public static string DefaultPath => ...`, `public Job Reload(Job job) => ...`.
- Trailing commas are used in multi-line collection and object initializers.
- Raw string literals (`"""`) are used for multi-line SQL and XML in `src/PrintInvoicer.Core/Data/Database.cs` and the test builders.
- Modern syntax is preferred: target-typed `new()`, collection expressions (`[..]`, `[]`), `is not` patterns, range patterns (`is >= 0 and <= 999`), `switch` expressions, `Enum.GetValues<T>()`.

**Nullability:**
- `<Nullable>enable</Nullable>` is set in every project, including tests.
- Use `?` for values that may be absent: `string? InvoiceNumber`, `DateTime? InvoiceDate`, `JobRowViewModel? _selectedJob`.
- The null-forgiving operator `!` appears often (over 100 places). Use it only after a check or where the value is known to be set, such as `Path.GetDirectoryName(Path.GetFullPath(filePath))!` in `src/PrintInvoicer.Core/Data/Database.cs` or `FinalizedTotal!.Value` in `tests/PrintInvoicer.UiTests/ScreenTests.cs` after the test has set up a finalized job.
- Return `null` to mean "not valid" or "not found" for parsing helpers: `ReleaseVersion.Parse(...)` returns `null` for non-versions (see `tests/PrintInvoicer.Tests/ReleaseVersionTests.cs`).

**Money, numbers and dates:**
- Money, unit rates, grams, mL and layer heights are always `decimal`, never `double` or `float`. Readers return `decimal` values (see `SlicedFile.LayerHeightMm` in `src/PrintInvoicer.Core/Slicing/SlicedFile.cs`).
- Round a billed amount to cents with `PricingCalculator.Money(...)` (`src/PrintInvoicer.Core/Pricing/PricingCalculator.cs`), which rounds half away from zero. Unit prices are rounded to four decimals in some rules versions (see `Math.Round(printCost / qty, 4, ...)` in the same file). Read the `PricingRules` branch before changing any rounding.
- Display money and rates through `src/PrintInvoicer.App/Format.cs`: `Format.Money` (en-US, `C2`), `Format.Rate` (keeps extra digits on small per-gram rates), `Format.Ml`, `Format.Duration`. Invoices and the UI both use these, so totals match.
- Persist and parse with `CultureInfo.InvariantCulture`. `src/PrintInvoicer.Core/Data/Database.cs` stores decimals as text with its `Str(...)` and `Dec(...)` helpers and dates as `yyyy-MM-dd` with `DateStr(...)` and `Date(...)`.
- Times and durations: seconds as `int`, displayed as `h:mm` and accepted as `5:56`, `5:56:30` or decimal hours by `Format.ParseDuration`.

**Usings:**
- `ImplicitUsings` is enabled in all projects, so `System`, `System.IO`, `System.Linq`, `System.Collections.Generic` and `System.Threading.Tasks` need no `using`. Some files still list them explicitly; that is accepted.
- When `using` lines are written, sort them with `System.*` first, then the other packages, then `PrintInvoicer.*`. See `src/PrintInvoicer.App/ViewModels/MainViewModel.cs`.
- There are no path aliases and no barrel files. Import the namespace that holds the type.

## Import Organization

**Order (as written in the code):**
1. `System.*` namespaces
2. Third-party packages: `CommunityToolkit.Mvvm.*`, `Microsoft.Data.Sqlite`, `QuestPDF.*`, `Microsoft.Win32`
3. Project namespaces: `PrintInvoicer.Core.*`, `PrintInvoicer.App.*`

**Project references:**
- `PrintInvoicer.Core` must not reference WPF or anything from `PrintInvoicer.App`. It targets `net10.0` and runs on Linux (see `src/PrintInvoicer.Core/PrintInvoicer.Core.csproj`).
- `PrintInvoicer.App` targets `net10.0-windows` with `UseWPF`, and references Core.

## Error Handling

**Readers (untrusted input) in `src/PrintInvoicer.Core/Slicing/`:**
- Every file read is treated as untrusted. Range-check every value against the limits in `src/PrintInvoicer.Core/Slicing/SliceLimits.cs` (`MaxSeconds`, `MaxLayers`, `MaxGrams`, `MaxMl`, `MaxPlateIndex` and so on) and cap sizes before reading (`MaxMetadataBytes`, `MaxImageBytes`, `MaxGcodeLineChars`).
- Failures caused by the file's content throw `InvalidDataException` with a message a user can read. The message names the file type and the fix. Example from `src/PrintInvoicer.Core/Slicing/BambuFileReader.cs`:
  ```csharp
  private static InvalidDataException Damaged(string what, Exception? inner = null) =>
      new($"This .gcode.3mf file looks damaged: {what}. Export the plate sliced file from Bambu Studio again.", inner);
  ```
  Callers use it as `throw Damaged($"it lists {plateElements.Count:N0} plates");`.
- Do not let a parser's or the zip library's exception escape. Wrap it: catch the library exception and throw the reader's own `InvalidDataException`. `ReaderContract.AssertOwnMessage` in `tests/PrintInvoicer.Tests/ReaderContract.cs` fails the tests when a foreign message reaches the caller.
- Never throw `NullReferenceException`, `IndexOutOfRangeException`, `OverflowException` or other generic types from a reader. The test suites `*RobustnessTests.cs` enforce this with truncated, bit-flipped and nonsense inputs.

**Catching in the app and the database:**
- Catch narrowly, with an exception filter: `catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)`. This pattern is the norm in `src/PrintInvoicer.App/App.xaml.cs`, `src/PrintInvoicer.App/UpdateService.cs`, `src/PrintInvoicer.App/WindowLayout.cs`, `src/PrintInvoicer.App/ViewModels/SettingsViewModel.cs` and `src/PrintInvoicer.App/ViewModels/JobEditorViewModel.cs`.
- Use the filter list that matches what the call can really throw. For file access that is `IOException or UnauthorizedAccessException`; add `InvalidDataException` when a reader is called and `SqliteException` when the database is touched.
- A broad `catch (Exception)` is allowed only at the outermost boundary of the program:
  - `App.OnStartup` in `src/PrintInvoicer.App/App.xaml.cs` for startup failure, which then shuts down with an exit code of 1.
  - The `DispatcherUnhandledException` and `UnhandledException` handlers in the same file.
  - `ErrorReport.Show` in `src/PrintInvoicer.App/ErrorReport.cs`, where the comment explains that reporting must never be what crashes the app.
- Background network work fails silently by design. `UpdateService` catches `HttpRequestException`, `TaskCanceledException`, `JsonException` and `IOException`, and the bar simply does not appear.
- Database: `src/PrintInvoicer.Core/Data/Database.cs` catches only the specific parse exceptions it expects (`catch (Exception ex) when (ex is FormatException or OverflowException)` in `LoadSettings`).

**Showing errors to the user:**
- Unexpected errors go through `ErrorReport.Show(what, ex)` in `src/PrintInvoicer.App/ErrorReport.cs`. It writes the details to `%LOCALAPPDATA%\PrintInvoicer\Errors` and asks, through a dialog, whether to open the issue page.
- Expected, recoverable problems are shown in place. Settings and backup code put the message into a status property (`BackupStatus = ex.Message` in `src/PrintInvoicer.App/ViewModels/SettingsViewModel.cs`) that the window binds to.
- Confirmations and "this failed" dialogs use `MessageBox.Show` directly, from view models (`MainViewModel.cs`, `JobEditorViewModel.cs`, `FilamentsViewModel.cs`, `PrintersViewModel.cs`, `SettingsViewModel.cs`) and from `InvoiceActions.cs`. Keep new dialog code in short methods so the logic underneath can be tested without a dialog. Dialogs must never run in the UI tests (see TESTING.md).
- Do not show raw exception text to the user when a plain sentence is possible. Put the plain sentence first and the technical details in the saved error file.

**Returning failure:**
- Deletes and lookups return `bool` (`DeleteFilament` returns `false` when the filament is used and was deactivated instead, see `src/PrintInvoicer.Core/Data/Database.cs`).
- Dialogs that can be cancelled return `bool?`: `InvoiceActions.AskRedate` returns `null` when the user cancels.
- Parsers return `null` for invalid input (see `ReleaseVersion.Parse`) or a `TryParse`-style result.

## Logging

**Framework:** None. There is no `ILogger`, `Trace` or `Console` output in the app or the core library.

**Patterns:**
- Diagnostic information is written to the error file by `ErrorReport.Save` (app version, Windows version, time, and the exception). Nothing is sent over the network.
- Do not add logging calls to the console. If a new diagnostic is needed, pass the exception to `ErrorReport.Show` or `ErrorReport.Save`.

## Comments

**Required by project rules (`CLAUDE.md`):**
- Purpose comments on every class and on methods, and on non-obvious blocks. Do not comment plain lines.
- Any new line that touches the network, starts a program, prints, or reads, writes or deletes a file gets an `// AUDIT:` comment directly above it, and a matching row in `AUDITING.md`. The comment names the kind of access in a few words. The forms used in the code are:
  - `// AUDIT: file read. Opens the .gcode.3mf the user chose, read-only.` (`src/PrintInvoicer.Core/Slicing/BambuFileReader.cs`)
  - `// AUDIT: file write. Creates the data folder if it does not exist ...` (`src/PrintInvoicer.Core/Data/Database.cs`)
  - `// AUDIT: opens a program. ...` (`src/PrintInvoicer.App/ErrorReport.cs`)
  - `// AUDIT: file read/write. ...` (`src/PrintInvoicer.Core/Data/Database.cs`, `Open()`)

**XML documentation:**
- `/// <summary>` on public and most internal types, and on public members that a caller needs to understand. Use `<see cref="..."/>` to link types and members: `<see cref="Data.Database.SaveJob"/>` in `src/PrintInvoicer.Core/Models/Job.cs`.
- Use `<param>`, `<returns>` and `<remarks>` only when the summary cannot say it in a sentence. `ReaderContract` and `BambuReaderTests` use `<summary>` for the contract being enforced.

**Line comments:**
- `//` above a method when the attribute or signature needs a note. In `src/PrintInvoicer.App/ViewModels/MainViewModel.cs` the comment sits between `[RelayCommand]` and the method (`// The Clear filters button.`). This is the style to copy.
- Section banners divide long files: `// ---------------- Filters ----------------` in `MainViewModel.cs`, `// ---- Results of pricing a job. ... ----` in `PricingCalculator.cs`.
- Explain the reason, not the mechanics. Good example, from `tests/PrintInvoicer.Tests/PricingTests.cs`: `// 1,000 pieces costing $4.00 in all: cents would make it $0.00 each.`

**Writing style:**
- Plain English. Do not use em dashes or en dashes in comments, docs, messages or UI text. Use a colon, a comma, parentheses or a new sentence.
- Refer to the user as "you" and to the app as "PrintInvoicer".

**Sample data:**
- Do not put real customer names, personal details or real file names in code, tests or docs. Use sample names such as "Harbor Dive Shop" and "Sample Printing Co." (see `tests/PrintInvoicer.UiTests/SampleShop.cs`).

## Function Design

**Size:**
- Most methods are short. The large files are the exceptions, and they are organised by region of responsibility rather than split by size: `src/PrintInvoicer.Core/Data/Database.cs` (about 890 lines), `src/PrintInvoicer.App/Pdf/InvoicePdf.cs` (about 650 lines), `src/PrintInvoicer.App/ViewModels/JobEditorViewModel.cs` (about 530 lines).
- When adding to a large file, grep for the member you need and add the new code next to its neighbours.

**Parameters:**
- Pass the data a function needs. Do not reach into static state for it; the database and price book are passed in.
- Optional parameters have defaults: `UpdateService(Version? installed = null, Func<...>? fetch = null, string? statePath = null)` in `src/PrintInvoicer.App/UpdateService.cs`.
- Pass time as a parameter instead of calling `DateTime.Now` inside the logic, so tests can use fixed dates. Examples: `DatabaseBackup.DailyIfDue(path, folder, day)`, `db.AllocateInvoiceNumber(date)`, `job.FinalizeInvoice(Delivered, redate: false)`.

**Return values:**
- Return the value directly. Use a named tuple for two results: `(string Number, int Sequence) NextInvoiceNumber(DateTime date)` in `src/PrintInvoicer.Core/Data/Database.cs`.
- Use `record` results for pricing output (see Naming Patterns).
- See "Returning failure" under Error Handling.

## Module Design

**Layers:**
- `PrintInvoicer.Core`: models, pricing, slicing readers, database access, backups, version parsing. No UI code and no network code.
- `PrintInvoicer.App`: WPF windows, view models, PDF rendering (QuestPDF), the update check, error reporting, and the entry point in `src/PrintInvoicer.App/App.xaml.cs`.
- Keep pricing rules in `PrintInvoicer.Core.Pricing`. The PDF layer reads the results; it must not compute totals on its own.

**MVVM (App):**
- View models derive from `ObservableObject` (CommunityToolkit.Mvvm 8.4.2) and are `partial` classes so the source generator can run.
- Change notifications: `[ObservableProperty]` for plain properties; `partial void OnXChanged(...)` for side effects (see `OnPaidFilterChanged` in `MainViewModel.cs`).
- Code-behind is kept thin. Drag-and-drop, key handling and window closing live in `src/PrintInvoicer.App/JobEditorWindow.xaml.cs`; the logic lives in `JobEditorViewModel.cs`.
- Views reach the view model with `DataContext`. A window that needs to open another window does so through a delegate on the view model, not through a direct reference: `MainViewModel.OpenEditor` is a `Func<Job, bool>` set by the window.
- Converters are in `src/PrintInvoicer.App/Converters.cs` (`BytesToImageConverter`, `HexToBrushConverter`). Shared formatting is in `src/PrintInvoicer.App/Format.cs`. Shared styles are in the XAML resources, referenced as `{StaticResource ...}`, e.g. `AccentButton`.
- Name XAML elements with `x:Name` only when the code-behind must reach them (`SearchBox` in `MainWindow.xaml`).
- Test seams are delegates and properties: `MainViewModel.Updates` is replaceable so tests check the update bar without the network; `JobEditorViewModel.ShowNotes` is an `Action<string>` so tests run without a dialog.

**Visibility:**
- Public by default for types used across projects. Use `internal` for helpers that only the project or its tests need: `internal static class SliceLimits`, `internal static class TestFiles`, `App.HideClearButton`, and the `App.Db` setter.
- `src/PrintInvoicer.App/AssemblyInfo.cs` grants `InternalsVisibleTo("PrintInvoicer.UiTests")`. Do not add other `InternalsVisibleTo` entries without a reason.

**Barrel files:** Not used.

**Dependency injection:** None. Objects are created with `new`, and their dependencies are constructor parameters (`new MainViewModel(db)`, `new JobEditorViewModel(db, job)`).

## Data and Schema Conventions

These rules come from `CLAUDE.md` and shape how new code should touch storage and pricing.

- **Finalized invoices never change.** Pricing for a finalized job reads its frozen `PriceSnapshot` (`src/PrintInvoicer.Core/Pricing/PriceSnapshot.cs`). Any change to how totals are computed goes behind a new `PricingRules` version, and the old behaviour stays for older versions. Add a test that shows an old total still matches (see `tests/PrintInvoicer.Tests/PricingRulesTests.cs`).
- **Schema changes** go only through `AddColumnIfMissing(...)` or `CREATE TABLE IF NOT EXISTS` inside `Database.EnsureCreated` in `src/PrintInvoicer.Core/Data/Database.cs`. The default must keep existing rows meaning the same thing.
- **Database access** is hand-written SQL with parameters. Use the `Exec`, `Scalar` and `Command` helpers with named parameters (`"$id"`). Open a new connection per operation through `Open()`. Foreign keys are on. Saving a job rewrites its parts, plates and slots inside one transaction.
- **Settings** are stored as key and value rows, one per public read-write property of `AppSettings` (`SettingProps` in `src/PrintInvoicer.Core/Data/Database.cs`). A new setting needs no schema change. `LoadSettings` parses only `decimal`, `int`, `string` and `bool` properties, so a new setting must use one of those types. A saved value that cannot be parsed throws `InvalidDataException` and is never replaced with a default, because a wrong rate would silently change invoice totals.

## Adding Code: Quick Checklist

- File and namespace match the folder. File-scoped namespace.
- Private fields `_camelCase`; constants and static readonly PascalCase.
- Money in `decimal`; display through `Format`; persist with invariant culture.
- Readers: range-check, cap sizes, throw `InvalidDataException` with a user-readable message only.
- Catch with `when` filters; broad catches only at the program boundary.
- `// AUDIT:` comment and an `AUDITING.md` row for any network, process, print, or file access.
- Purpose comments on classes and methods; no comments on plain lines; no em dashes.
- Use sample names only.
- A new behaviour that changes totals goes behind a `PricingRules` version.

---

*Convention analysis: 2026-11-01*

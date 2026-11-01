<!-- GSD:project-start source:PROJECT.md -->

## Project

**PrintInvoicer**

PrintInvoicer is a Windows desktop app (.NET 10, WPF, SQLite, QuestPDF) that prices 3D print jobs (FDM and resin) from sliced files and makes PDF invoices. It is a public, MIT-licensed personal project shared as-is at https://github.com/cevans3505/PrintInvoicer (current version 3.6.0). This milestone makes the project easier to understand at a glance, pays down known concerns in the order that gives the most value per unit of effort, and opens a channel for people to send slice files so support and accuracy can improve.

**Core Value:** Prices and invoices stay correct and trustworthy: a finalized invoice never changes, and totals match what the slice file and price book say.

### Constraints

- **Finalized invoices never change**: pricing changes go behind a new `PricingRules` version; old totals must still match.
- **Schema changes**: only via `AddColumnIfMissing` / `CREATE TABLE IF NOT EXISTS` in `Database.EnsureCreated`, with defaults that keep existing data meaning the same.
- **Outside access**: any new network, process start, print or file read/write/delete gets an `// AUDIT:` comment and a row in `AUDITING.md`.
- **UI tests offscreen only**: never show, activate or focus a window, never trigger a dialog; use `WpfHost`. Applies to the screenshot generator too.
- **No personal data**: sample names like "Harbor Dive Shop", "Sample Printing Co." in code, tests, docs and README images.
- **Writing style**: plain English, no em dashes, in docs, comments and messages.
- **File readers**: treat files as untrusted, range-check, cap sizes, throw only `InvalidDataException` with a readable message.

<!-- GSD:project-end -->

<!-- GSD:stack-start source:codebase/STACK.md -->

## Technology Stack

## Languages

- C# (language version from the .NET 10 SDK, nullable reference types and implicit usings enabled in every project) - all code in `src/PrintInvoicer.Core/`, `src/PrintInvoicer.App/`, `tests/PrintInvoicer.Tests/`, `tests/PrintInvoicer.UiTests/`
- XAML - WPF windows and resources: `src/PrintInvoicer.App/App.xaml`, `src/PrintInvoicer.App/MainWindow.xaml`, `src/PrintInvoicer.App/JobEditorWindow.xaml`, `src/PrintInvoicer.App/Assets/Icon.xaml` (drawing source for the icon, not app UI)
- PowerShell - build, install and icon tooling: `publish.ps1`, `tools/Make-Icon.ps1`, and the inline scripts in `.github/workflows/release.yml`
- YAML - GitHub Actions workflows: `.github/workflows/ci.yml`, `.github/workflows/release.yml`
- Markdown and plain text - user docs and packaging: `README.md`, `AUDITING.md`, `CLAUDE.md`, `packaging/Read me.txt` (shipped inside the release zip)

## Runtime

- .NET 10 (`net10.0` for the Core library and tests, `net10.0-windows` for the WPF app and the UI tests)
- Windows only for the app and UI tests. The Core library and unit tests have no Windows-only API and run on Linux (`dotnet test tests/PrintInvoicer.Tests`).
- SDK pinned in `global.json`: `10.0.403`, with `"rollForward": "latestPatch"`.
- Release target: `win-x64`, self-contained, single-file executable (`PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, `EnableCompressionInSingleFile=true` in `src/PrintInvoicer.App/PrintInvoicer.App.csproj`). Users do not need a .NET install.
- NuGet via `dotnet restore` (driven by the `.csproj` files; no central package management file was found)
- Lockfile: missing. No `packages.lock.json` or `Directory.Packages.props` is tracked in the repository, so package versions are pinned only by the explicit `Version=` attributes in each `.csproj`.
- Solution file: `PrintInvoicer.sln`

## Frameworks

- WPF (Windows Presentation Foundation) - the desktop UI, `src/PrintInvoicer.App/`. Enabled with `<UseWPF>true</UseWPF>`. Dark theme set with `ThemeMode="Dark"` and the `PresentationFramework.Fluent` dark dictionary in `src/PrintInvoicer.App/App.xaml`. The `WPF0001` warning (experimental `ThemeMode`) is suppressed in the app and UI test projects.
- MVVM via CommunityToolkit.Mvvm 8.4.2 - view models in `src/PrintInvoicer.App/ViewModels/` use `ObservableObject`, `RelayCommand` and source generators (`[ObservableProperty]`, `[RelayCommand]`).
- QuestPDF 2026.9.1 - PDF layout and rendering for invoices and detail sheets, `src/PrintInvoicer.App/Pdf/InvoicePdf.cs`. The license is set to Community at startup in `src/PrintInvoicer.App/App.xaml.cs` (`QuestPDF.Settings.License = LicenseType.Community`).
- Microsoft.Data.Sqlite 10.0.14 - SQLite access, `src/PrintInvoicer.Core/Data/Database.cs` and `src/PrintInvoicer.Core/Data/DatabaseBackup.cs`. Hand-written SQL, no ORM. The database is a plain file; the native SQLite engine comes in through SQLitePCLRaw.
- Standard .NET libraries used directly: `System.IO.Compression` (opens `.gcode.3mf` zip archives in `src/PrintInvoicer.Core/Slicing/BambuFileReader.cs`), `System.Xml` (`XmlReader` with DTDs prohibited and `XmlResolver = null`), `System.Text.Json` (update check state and GitHub API reply, `src/PrintInvoicer.App/UpdateService.cs`), `System.Net.Http.HttpClient` (single GitHub request, `UpdateService.cs`), and a hand-written PNG encoder in `src/PrintInvoicer.Core/Slicing/PngWriter.cs`.
- Printing uses WPF's own `System.Windows.Controls.PrintDialog` (`src/PrintInvoicer.App/Pdf/InvoicePrinter.cs`). No Windows Forms dependency is referenced.
- xUnit 2.9.3 with `xunit.runner.visualstudio` 3.1.4 and `Microsoft.NET.Test.Sdk` 17.14.1 - both test projects, `tests/PrintInvoicer.Tests/PrintInvoicer.Tests.csproj` and `tests/PrintInvoicer.UiTests/PrintInvoicer.UiTests.csproj`. `Xunit` is globally imported in both.
- coverlet.collector 6.0.4 - code coverage collector, referenced by `tests/PrintInvoicer.Tests/PrintInvoicer.Tests.csproj` only.
- UI tests (`tests/PrintInvoicer.UiTests/`) open the real WPF windows offscreen through `WpfHost.cs`. They need an interactive Windows desktop session.
- Test fixtures: sample sliced files in `tests/PrintInvoicer.Tests/Fixtures/` (`Cylindex_Tube_Cap.gcode.3mf`, `Wire_Brush_Insert.gcode.3mf`), copied to output by the test `.csproj` files.
- .NET SDK 10.0.403 (`global.json`) - build, test, publish.
- `dotnet publish` with the release switches above; the same arguments appear in `publish.ps1` and `.github/workflows/release.yml`.
- `publish.ps1` - runs all tests, publishes, installs to `%LOCALAPPDATA%\Programs\PrintInvoicer`, creates a Start menu shortcut. Runs in Windows PowerShell 5.1.
- `tools/Make-Icon.ps1` - renders `src/PrintInvoicer.App/Assets/Icon.xaml` into `src/PrintInvoicer.App/Assets/PrintInvoicer.ico`. Developer tool only.
- GitHub Actions (`actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/upload-artifact@v7`, `actions/download-artifact@v8`), running on `windows-latest`.

## Key Dependencies

- `QuestPDF` 2026.9.1 (App) - the whole invoice and detail-sheet layout. Community license. Ships native Skia and qpdf libraries (`QuestPdfSkia.dll`, `qpdf.dll`) that are bundled into the single-file exe.
- `Microsoft.Data.Sqlite` 10.0.14 (Core) - the only persistence layer. Ships the native `e_sqlite3.dll` via SQLitePCLRaw.
- `CommunityToolkit.Mvvm` 8.4.2 (App) - MVVM base classes and source generators. No runtime I/O.
- `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4, `coverlet.collector` 6.0.4 - test-only; not shipped in the release.
- Project references: `PrintInvoicer.App` references `PrintInvoicer.Core`. Both test projects reference the projects they test. `PrintInvoicer.UiTests` also links `tests/PrintInvoicer.Tests/TestFiles.cs` and the fixtures.
- The app version is `<Version>3.6.0</Version>` in `src/PrintInvoicer.App/PrintInvoicer.App.csproj`. The release workflow requires the git tag to be exactly `v<Version>`. `packaging/Read me.txt` carries a `PrintInvoicer 3.6` heading that is checked (with a warning only) against the major and minor version.
- Version comparison helpers: `src/PrintInvoicer.Core/Updates/ReleaseVersion.cs`.

## Configuration

- No environment variables are read by the application code. Data location comes from Windows special folders: `Documents\PrintInvoicer\printinvoicer.db` (`Database.DefaultPath`) and `%LOCALAPPDATA%\PrintInvoicer\` for small state files.
- User settings are stored in the SQLite `settings` table (key/value text, `src/PrintInvoicer.Core/Data/Database.cs`) and mapped to `src/PrintInvoicer.Core/Models/AppSettings.cs`. Notable keys include `CheckForUpdates` (default `true`) and `BackupFolder` (empty means `Backups` next to the database).
- Build-time configuration lives only in the `.csproj` files and `global.json`. No `appsettings.json`, `app.config` or user secrets file is tracked.
- Environment files: none tracked. The `.gitignore` covers build output; no `.env` file is present in the repository.
- Slice-file size and count limits: `src/PrintInvoicer.Core/Slicing/SliceLimits.cs`.
- `global.json` - pins the SDK.
- `PrintInvoicer.sln` - solution with the two `src` projects and two `tests` projects.
- `src/PrintInvoicer.Core/PrintInvoicer.Core.csproj` - net10.0, Microsoft.Data.Sqlite.
- `src/PrintInvoicer.App/PrintInvoicer.App.csproj` - net10.0-windows WinExe, WPF, QuestPDF, CommunityToolkit.Mvvm, icon and version.
- `tests/PrintInvoicer.Tests/PrintInvoicer.Tests.csproj` and `tests/PrintInvoicer.UiTests/PrintInvoicer.UiTests.csproj` - test projects.
- `.github/workflows/ci.yml` - restore, build (Release), unit tests on `windows-latest`, and a separate screen-test job for the UI tests.
- `.github/workflows/release.yml` - on a `v*` tag: checks the tag against `<Version>`, runs unit tests, publishes the single-file exe, zips it with `packaging/Read me.txt`, and creates a draft GitHub release.

## Platform Requirements

- Windows 10 or 11 with the .NET 10 SDK (10.0.403 or a later patch).
- Windows PowerShell 5.1 or PowerShell 7 for `publish.ps1`. The app is closed before publishing (the script refuses to run while `PrintInvoicer` is running).
- Core library and `tests/PrintInvoicer.Tests` can build and test on Linux or macOS. The App and UI tests need Windows and WPF.
- An interactive desktop session for `tests/PrintInvoicer.UiTests`.
- Windows 10 or 11, 64-bit (`win-x64`). The exe is self-contained, so no .NET runtime install is needed.
- Distributed as `PrintInvoicer-<version>.zip` (exe plus `Read me.txt`) on GitHub Releases. The exe is not code-signed; the read-me warns that Windows may show an unknown-publisher prompt.
- Local data: `Documents\PrintInvoicer\` (database, backups, invoices) and `%LOCALAPPDATA%\PrintInvoicer\` (update check state, window layout, error details). See `AUDITING.md` for the full file list.
- Printing uses the Windows print dialog through WPF `PrintDialog` (`src/PrintInvoicer.App/Pdf/InvoicePrinter.cs`).

<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->

## Conventions

## Naming Patterns

- One main type per file, and the file name is the type name: `src/PrintInvoicer.Core/Pricing/PricingCalculator.cs`, `src/PrintInvoicer.App/ViewModels/JobEditorViewModel.cs`, `src/PrintInvoicer.Core/Slicing/GooFileReader.cs`.
- WPF windows are a pair: `src/PrintInvoicer.App/JobEditorWindow.xaml` and `src/PrintInvoicer.App/JobEditorWindow.xaml.cs`.
- Small helper types can share a file when they belong together, e.g. the result records at the top of `src/PrintInvoicer.Core/Pricing/PricingCalculator.cs`.
- Test files are named for the area they cover and end in `Tests.cs`: `PricingTests.cs`, `DatabaseTests.cs`, `FinalizeTests.cs`. Hostile-input suites end in `RobustnessTests.cs`: `BambuRobustnessTests.cs`, `GooRobustnessTests.cs`.
- Shared test helpers have no `Tests` suffix: `tests/PrintInvoicer.Tests/TestFiles.cs`, `tests/PrintInvoicer.Tests/ReaderContract.cs`, `tests/PrintInvoicer.UiTests/WpfHost.cs`, `tests/PrintInvoicer.UiTests/SampleShop.cs`.
- Folders are PascalCase and match the namespace: `src/PrintInvoicer.Core/Pricing/` is `PrintInvoicer.Core.Pricing`, `src/PrintInvoicer.App/ViewModels/` is `PrintInvoicer.App.ViewModels`.
- Use file-scoped namespaces everywhere: `namespace PrintInvoicer.Core.Models;` with no braces.
- Test namespaces are `PrintInvoicer.Tests` and `PrintInvoicer.UiTests`.
- PascalCase: `Calculate`, `AllocateInvoiceNumber`, `SaveJob`, `DailyIfDue`.
- Async methods end in `Async`: `UpdateService.CheckOnStartupAsync`, `UpdateService.FetchLatestAsync`, `App.BackUpTodayAsync`.
- MVVM commands are private methods with `[RelayCommand]`. The toolkit generates the `...Command` property, so the method `Save()` is used from XAML and tests as `SaveCommand`. Examples: `ClearFilters()` becomes `ClearFiltersCommand` in `src/PrintInvoicer.App/ViewModels/MainViewModel.cs`; `LaterCommand` and `BackUpNowCommand` are used in the UI tests.
- Test methods are named with underscores and read as sentences: `Seeds_filaments_once`, `Invoice_numbers_skip_ones_already_used`, `Half_cent_unit_price_rounds_up_when_hours_are_not_exact_decimals`. Do not use the `ShouldX` or `Given_When_Then` forms.
- Private instance fields use a leading underscore and camelCase: `_db`, `_connectionString`, `_book`, `_loading`, `_fetch`.
- A field with `[ObservableProperty]` is written as a private `_camelCase` field, and the toolkit generates the public PascalCase property. Example in `src/PrintInvoicer.App/ViewModels/MainViewModel.cs`: `[ObservableProperty] private string _keywords = "";` gives `Keywords`.
- Static readonly fields and constants are PascalCase, including private ones: `private static readonly PropertyInfo[] SettingProps` in `src/PrintInvoicer.Core/Data/Database.cs`, `private const int MaxXmlDepth` in `src/PrintInvoicer.Core/Slicing/BambuFileReader.cs`. `private static readonly CultureInfo Us` in `src/PrintInvoicer.App/Format.cs` follows the same rule. There is no `s_` or `k` prefix.
- Locals and parameters are camelCase.
- Parameter names are used as named arguments where a bare value would be unclear: `db.DeleteFilament(...)`, `InvoiceActions.Finalize(Db, FinalizedJob, date, redate: false)`, `JobBuilder.AddFile(..., printerId: fdm.Id)`.
- Use `record` for immutable data and results: `SlotQuote`, `PlateQuote`, `PartQuote` and `JobQuote` in `src/PrintInvoicer.Core/Pricing/PricingCalculator.cs`; `ReleaseInfo` in `src/PrintInvoicer.App/UpdateService.cs`; `Option<T>` in `src/PrintInvoicer.App/ViewModels/MainViewModel.cs`.
- Use `class` with get/set properties for editable models: `Job`, `JobPart`, `JobPlate`, `Filament`, `Printer`, `Resin`, `AppSettings` under `src/PrintInvoicer.Core/Models/`.
- Enums are PascalCase and their members too: `PrintTechnology.Fdm`, `PrintTechnology.Resin`, `PaidFilter.Unpaid`, `CostBasis.PerPrint`, `InvoiceKind.Final`.
- There are no interfaces in the code base. Test seams are constructor parameters with defaults or delegates, not interfaces. See "Module Design" below.
- Abstract test base classes are used when several suites share builders: `BambuReaderTests` in `tests/PrintInvoicer.Tests/BambuRobustnessTests.cs`.

## Code Style

- No `.editorconfig`, `.prettierrc` or `stylecop.json` is present, and no analyzers are configured. Formatting is not enforced by a tool; follow the patterns in the files around you.
- Four-space indentation.
- Allman braces: the opening brace goes on its own line for types, methods, and control blocks.
- Expression-bodied members are used for one-line members and lambdas: `public static string DefaultPath => ...`, `public Job Reload(Job job) => ...`.
- Trailing commas are used in multi-line collection and object initializers.
- Raw string literals (`"""`) are used for multi-line SQL and XML in `src/PrintInvoicer.Core/Data/Database.cs` and the test builders.
- Modern syntax is preferred: target-typed `new()`, collection expressions (`[..]`, `[]`), `is not` patterns, range patterns (`is >= 0 and <= 999`), `switch` expressions, `Enum.GetValues<T>()`.
- `<Nullable>enable</Nullable>` is set in every project, including tests.
- Use `?` for values that may be absent: `string? InvoiceNumber`, `DateTime? InvoiceDate`, `JobRowViewModel? _selectedJob`.
- The null-forgiving operator `!` appears often (over 100 places). Use it only after a check or where the value is known to be set, such as `Path.GetDirectoryName(Path.GetFullPath(filePath))!` in `src/PrintInvoicer.Core/Data/Database.cs` or `FinalizedTotal!.Value` in `tests/PrintInvoicer.UiTests/ScreenTests.cs` after the test has set up a finalized job.
- Return `null` to mean "not valid" or "not found" for parsing helpers: `ReleaseVersion.Parse(...)` returns `null` for non-versions (see `tests/PrintInvoicer.Tests/ReleaseVersionTests.cs`).
- Money, unit rates, grams, mL and layer heights are always `decimal`, never `double` or `float`. Readers return `decimal` values (see `SlicedFile.LayerHeightMm` in `src/PrintInvoicer.Core/Slicing/SlicedFile.cs`).
- Round a billed amount to cents with `PricingCalculator.Money(...)` (`src/PrintInvoicer.Core/Pricing/PricingCalculator.cs`), which rounds half away from zero. Unit prices are rounded to four decimals in some rules versions (see `Math.Round(printCost / qty, 4, ...)` in the same file). Read the `PricingRules` branch before changing any rounding.
- Display money and rates through `src/PrintInvoicer.App/Format.cs`: `Format.Money` (en-US, `C2`), `Format.Rate` (keeps extra digits on small per-gram rates), `Format.Ml`, `Format.Duration`. Invoices and the UI both use these, so totals match.
- Persist and parse with `CultureInfo.InvariantCulture`. `src/PrintInvoicer.Core/Data/Database.cs` stores decimals as text with its `Str(...)` and `Dec(...)` helpers and dates as `yyyy-MM-dd` with `DateStr(...)` and `Date(...)`.
- Times and durations: seconds as `int`, displayed as `h:mm` and accepted as `5:56`, `5:56:30` or decimal hours by `Format.ParseDuration`.
- `ImplicitUsings` is enabled in all projects, so `System`, `System.IO`, `System.Linq`, `System.Collections.Generic` and `System.Threading.Tasks` need no `using`. Some files still list them explicitly; that is accepted.
- When `using` lines are written, sort them with `System.*` first, then the other packages, then `PrintInvoicer.*`. See `src/PrintInvoicer.App/ViewModels/MainViewModel.cs`.
- There are no path aliases and no barrel files. Import the namespace that holds the type.

## Import Organization

- `PrintInvoicer.Core` must not reference WPF or anything from `PrintInvoicer.App`. It targets `net10.0` and runs on Linux (see `src/PrintInvoicer.Core/PrintInvoicer.Core.csproj`).
- `PrintInvoicer.App` targets `net10.0-windows` with `UseWPF`, and references Core.

## Error Handling

- Every file read is treated as untrusted. Range-check every value against the limits in `src/PrintInvoicer.Core/Slicing/SliceLimits.cs` (`MaxSeconds`, `MaxLayers`, `MaxGrams`, `MaxMl`, `MaxPlateIndex` and so on) and cap sizes before reading (`MaxMetadataBytes`, `MaxImageBytes`, `MaxGcodeLineChars`).
- Failures caused by the file's content throw `InvalidDataException` with a message a user can read. The message names the file type and the fix. Example from `src/PrintInvoicer.Core/Slicing/BambuFileReader.cs`:
- Do not let a parser's or the zip library's exception escape. Wrap it: catch the library exception and throw the reader's own `InvalidDataException`. `ReaderContract.AssertOwnMessage` in `tests/PrintInvoicer.Tests/ReaderContract.cs` fails the tests when a foreign message reaches the caller.
- Never throw `NullReferenceException`, `IndexOutOfRangeException`, `OverflowException` or other generic types from a reader. The test suites `*RobustnessTests.cs` enforce this with truncated, bit-flipped and nonsense inputs.
- Catch narrowly, with an exception filter: `catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)`. This pattern is the norm in `src/PrintInvoicer.App/App.xaml.cs`, `src/PrintInvoicer.App/UpdateService.cs`, `src/PrintInvoicer.App/WindowLayout.cs`, `src/PrintInvoicer.App/ViewModels/SettingsViewModel.cs` and `src/PrintInvoicer.App/ViewModels/JobEditorViewModel.cs`.
- Use the filter list that matches what the call can really throw. For file access that is `IOException or UnauthorizedAccessException`; add `InvalidDataException` when a reader is called and `SqliteException` when the database is touched.
- A broad `catch (Exception)` is allowed only at the outermost boundary of the program:
- Background network work fails silently by design. `UpdateService` catches `HttpRequestException`, `TaskCanceledException`, `JsonException` and `IOException`, and the bar simply does not appear.
- Database: `src/PrintInvoicer.Core/Data/Database.cs` catches only the specific parse exceptions it expects (`catch (Exception ex) when (ex is FormatException or OverflowException)` in `LoadSettings`).
- Unexpected errors go through `ErrorReport.Show(what, ex)` in `src/PrintInvoicer.App/ErrorReport.cs`. It writes the details to `%LOCALAPPDATA%\PrintInvoicer\Errors` and asks, through a dialog, whether to open the issue page.
- Expected, recoverable problems are shown in place. Settings and backup code put the message into a status property (`BackupStatus = ex.Message` in `src/PrintInvoicer.App/ViewModels/SettingsViewModel.cs`) that the window binds to.
- Confirmations and "this failed" dialogs use `MessageBox.Show` directly, from view models (`MainViewModel.cs`, `JobEditorViewModel.cs`, `FilamentsViewModel.cs`, `PrintersViewModel.cs`, `SettingsViewModel.cs`) and from `InvoiceActions.cs`. Keep new dialog code in short methods so the logic underneath can be tested without a dialog. Dialogs must never run in the UI tests (see TESTING.md).
- Do not show raw exception text to the user when a plain sentence is possible. Put the plain sentence first and the technical details in the saved error file.
- Deletes and lookups return `bool` (`DeleteFilament` returns `false` when the filament is used and was deactivated instead, see `src/PrintInvoicer.Core/Data/Database.cs`).
- Dialogs that can be cancelled return `bool?`: `InvoiceActions.AskRedate` returns `null` when the user cancels.
- Parsers return `null` for invalid input (see `ReleaseVersion.Parse`) or a `TryParse`-style result.

## Logging

- Diagnostic information is written to the error file by `ErrorReport.Save` (app version, Windows version, time, and the exception). Nothing is sent over the network.
- Do not add logging calls to the console. If a new diagnostic is needed, pass the exception to `ErrorReport.Show` or `ErrorReport.Save`.

## Comments

- Purpose comments on every class and on methods, and on non-obvious blocks. Do not comment plain lines.
- Any new line that touches the network, starts a program, prints, or reads, writes or deletes a file gets an `// AUDIT:` comment directly above it, and a matching row in `AUDITING.md`. The comment names the kind of access in a few words. The forms used in the code are:
- `/// <summary>` on public and most internal types, and on public members that a caller needs to understand. Use `<see cref="..."/>` to link types and members: `<see cref="Data.Database.SaveJob"/>` in `src/PrintInvoicer.Core/Models/Job.cs`.
- Use `<param>`, `<returns>` and `<remarks>` only when the summary cannot say it in a sentence. `ReaderContract` and `BambuReaderTests` use `<summary>` for the contract being enforced.
- `//` above a method when the attribute or signature needs a note. In `src/PrintInvoicer.App/ViewModels/MainViewModel.cs` the comment sits between `[RelayCommand]` and the method (`// The Clear filters button.`). This is the style to copy.
- Section banners divide long files: `// ---------------- Filters ----------------` in `MainViewModel.cs`, `// ---- Results of pricing a job. ... ----` in `PricingCalculator.cs`.
- Explain the reason, not the mechanics. Good example, from `tests/PrintInvoicer.Tests/PricingTests.cs`: `// 1,000 pieces costing $4.00 in all: cents would make it $0.00 each.`
- Plain English. Do not use em dashes or en dashes in comments, docs, messages or UI text. Use a colon, a comma, parentheses or a new sentence.
- Refer to the user as "you" and to the app as "PrintInvoicer".
- Do not put real customer names, personal details or real file names in code, tests or docs. Use sample names such as "Harbor Dive Shop" and "Sample Printing Co." (see `tests/PrintInvoicer.UiTests/SampleShop.cs`).

## Function Design

- Most methods are short. The large files are the exceptions, and they are organised by region of responsibility rather than split by size: `src/PrintInvoicer.Core/Data/Database.cs` (about 890 lines), `src/PrintInvoicer.App/Pdf/InvoicePdf.cs` (about 650 lines), `src/PrintInvoicer.App/ViewModels/JobEditorViewModel.cs` (about 530 lines).
- When adding to a large file, grep for the member you need and add the new code next to its neighbours.
- Pass the data a function needs. Do not reach into static state for it; the database and price book are passed in.
- Optional parameters have defaults: `UpdateService(Version? installed = null, Func<...>? fetch = null, string? statePath = null)` in `src/PrintInvoicer.App/UpdateService.cs`.
- Pass time as a parameter instead of calling `DateTime.Now` inside the logic, so tests can use fixed dates. Examples: `DatabaseBackup.DailyIfDue(path, folder, day)`, `db.AllocateInvoiceNumber(date)`, `job.FinalizeInvoice(Delivered, redate: false)`.
- Return the value directly. Use a named tuple for two results: `(string Number, int Sequence) NextInvoiceNumber(DateTime date)` in `src/PrintInvoicer.Core/Data/Database.cs`.
- Use `record` results for pricing output (see Naming Patterns).
- See "Returning failure" under Error Handling.

## Module Design

- `PrintInvoicer.Core`: models, pricing, slicing readers, database access, backups, version parsing. No UI code and no network code.
- `PrintInvoicer.App`: WPF windows, view models, PDF rendering (QuestPDF), the update check, error reporting, and the entry point in `src/PrintInvoicer.App/App.xaml.cs`.
- Keep pricing rules in `PrintInvoicer.Core.Pricing`. The PDF layer reads the results; it must not compute totals on its own.
- View models derive from `ObservableObject` (CommunityToolkit.Mvvm 8.4.2) and are `partial` classes so the source generator can run.
- Change notifications: `[ObservableProperty]` for plain properties; `partial void OnXChanged(...)` for side effects (see `OnPaidFilterChanged` in `MainViewModel.cs`).
- Code-behind is kept thin. Drag-and-drop, key handling and window closing live in `src/PrintInvoicer.App/JobEditorWindow.xaml.cs`; the logic lives in `JobEditorViewModel.cs`.
- Views reach the view model with `DataContext`. A window that needs to open another window does so through a delegate on the view model, not through a direct reference: `MainViewModel.OpenEditor` is a `Func<Job, bool>` set by the window.
- Converters are in `src/PrintInvoicer.App/Converters.cs` (`BytesToImageConverter`, `HexToBrushConverter`). Shared formatting is in `src/PrintInvoicer.App/Format.cs`. Shared styles are in the XAML resources, referenced as `{StaticResource ...}`, e.g. `AccentButton`.
- Name XAML elements with `x:Name` only when the code-behind must reach them (`SearchBox` in `MainWindow.xaml`).
- Test seams are delegates and properties: `MainViewModel.Updates` is replaceable so tests check the update bar without the network; `JobEditorViewModel.ShowNotes` is an `Action<string>` so tests run without a dialog.
- Public by default for types used across projects. Use `internal` for helpers that only the project or its tests need: `internal static class SliceLimits`, `internal static class TestFiles`, `App.HideClearButton`, and the `App.Db` setter.
- `src/PrintInvoicer.App/AssemblyInfo.cs` grants `InternalsVisibleTo("PrintInvoicer.UiTests")`. Do not add other `InternalsVisibleTo` entries without a reason.

## Data and Schema Conventions

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

<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->

## Architecture

## System Overview

```text

```
- `src/PrintInvoicer.Core` (net10.0, no UI, no WPF references): models, pricing, slicing-file readers and SQLite storage.
- `src/PrintInvoicer.App` (net10.0-windows, WPF, output `PrintInvoicer.exe`): windows, view models, PDF rendering, and app-level services. References Core.
- `tests/PrintInvoicer.Tests` (xUnit, references Core only in practice, runs on any OS in CI's unit job) and `tests/PrintInvoicer.UiTests` (xUnit, WPF, Windows only, references App).

## Pattern Overview

- The domain core is pure and deterministic. `PricingCalculator.Calculate(job, book)` takes a `PriceBook` (settings, materials, printers, extra costs) and returns a `JobQuote`; it reads and writes nothing.
- Persistence is one concrete class, `Database` (`src/PrintInvoicer.Core/Data/Database.cs`), using hand-written SQL through `Microsoft.Data.Sqlite`. Each operation opens its own connection (`Open()`); foreign keys are enabled per connection so deleting a job cascades to its parts, plates and slots.
- Schema is created and upgraded on every start by `Database.EnsureCreated()` using `CREATE TABLE IF NOT EXISTS` and `AddColumnIfMissing`. This is the only place schema changes happen (see `CLAUDE.md`).
- Finalized invoices are protected by frozen prices. `PriceSnapshot` stores the price inputs as text on the job at finalize time; later pricing for a finalized job restores that snapshot via `PriceSnapshot.For(job, current)` instead of using current rates. `PricingRules.Current` (`src/PrintInvoicer.Core/Pricing/PriceBook.cs`) versions the rules so older snapshots keep old behavior.
- The app is single-process and single-window-owner: `App.Db` is a static property holding the one `Database` instance, set in `App.OnStartup`.
- Slicing-file input is handled entirely in Core. File readers are the only code that parses untrusted files; they throw `InvalidDataException` with user-readable messages.

## Layers

- Purpose: plain data for a job, its parts and plates, materials, printers and fees.
- Location: `src/PrintInvoicer.Core/Models/`
- Contains: `Job.cs` (`Job`, `JobPart`, `JobPlate`, `PlateFilament`, `PlateResin`), `Filament.cs`, `Resin.cs`, `Printer.cs` (with `PrintTechnology`), `ExtraCost.cs` (with `CostBasis`), `AppSettings.cs`, `JobFilter.cs`.
- Depends on: nothing in the solution.
- Used by: every other layer.
- Purpose: turn a job and a price book into money, and freeze prices for finalized invoices.
- Location: `src/PrintInvoicer.Core/Pricing/`
- Contains: `PricingCalculator.cs` (quote records `SlotQuote`, `ResinQuote`, `PlateQuote`, `PartQuote`, `JobQuote`, and the static `Calculate` overloads), `PriceBook.cs` (`PriceBook` and `PricingRules`), `PriceSnapshot.cs` (`Capture`, `Restore`, `For`, `Freeze`), `FilamentMatcher.cs` and `ResinMatcher.cs` (guess a material from a slicer profile name).
- Depends on: Models.
- Used by: `MainViewModel`, `JobEditorViewModel`, `InvoiceActions`, `Database.FreezeFinalizedJobs`.
- Purpose: read sliced-print files and turn them into job parts and plates.
- Location: `src/PrintInvoicer.Core/Slicing/`
- Contains: `SliceFileReader.cs` (dispatch by extension), `BambuFileReader.cs` (`.gcode.3mf` and `.gcode` from Bambu Studio), `GooFileReader.cs` (ELEGOO `.goo` resin), `SlicedFile.cs` (neutral records `SlicedFile`, `SlicedPlate`, `SlicedFilament`, `SlicedResin`), `SliceLimits.cs` (size and range caps, internal), `JobBuilder.cs` (adds plates to a job, groups them into parts, creates hand-typed plates), `PngWriter.cs` (thumbnail output for `.goo`).
- Depends on: Models, Pricing (matchers).
- Used by: `JobEditorViewModel.AddFiles`.
- Purpose: store and load everything, and back it up.
- Location: `src/PrintInvoicer.Core/Data/`
- Contains: `Database.cs` (CRUD for filaments, mappings, printers, resins, extra costs, jobs, settings and invoice numbering), `DatabaseBackup.cs` (daily and manual copies, pruning, restore), `SeedData.cs` (starter materials for a new database).
- Depends on: Models, Pricing (`LoadPriceBook`, `PriceSnapshot`).
- Used by: `App` (startup and backup), all ViewModels, `InvoiceActions`.
- Purpose: cross-cutting WPF-side helpers.
- Location: `src/PrintInvoicer.App/` root files.
- Contains: `InvoiceActions.cs` (finalize, redate prompt, business-details check), `ErrorReport.cs` (shows a plain message and writes details for the developer), `UpdateService.cs` (checks GitHub releases through a shared `HttpClient`), `WindowLayout.cs` (remembers window size and position), `Format.cs` and `Converters.cs` (display helpers and XAML value converters).
- Depends on: Core, WPF.
- Used by: ViewModels and code-behind.
- Purpose: build invoices with QuestPDF and print or save them.
- Location: `src/PrintInvoicer.App/Pdf/`
- Contains: `InvoicePdf.cs` (`InvoiceKind` enum with `Preview`, `Final`, `Internal`; `Write` saves a PDF; `RenderPages` returns page images), `InvoicePrinter.cs` (sends pages to the Windows print dialog at `Dpi = 300`), `PlateImages.cs` (plate thumbnails for the PDF).
- Depends on: Core models and pricing, QuestPDF.
- Used by: `JobEditorViewModel` (preview, final, internal buttons) and `MainViewModel.GenerateInvoices`.
- Purpose: state and commands for each window, with no code-behind logic beyond wiring.
- Location: `src/PrintInvoicer.App/ViewModels/`
- Contains: `MainViewModel` (job list, filters, totals, invoice actions), `JobEditorViewModel` (one job: parts, plates, fees, save, invoice buttons), `PartViewModel` (`PartViewModel`, `PlateViewModel`, `SlotViewModel`), `PrintersViewModel`, `FilamentsViewModel`, `SettingsViewModel`.
- Depends on: Core, App services, `App.Db`.
- Used by: the corresponding windows through `DataContext`.
- Purpose: XAML layout and minimal code-behind.
- Location: `src/PrintInvoicer.App/*.xaml`, `*.xaml.cs`; `App.xaml` holds styles and the Fluent dark theme.
- Contains: `MainWindow`, `JobEditorWindow`, `App`.

## Data Flow

### Primary Request Path: Build a job from a sliced file

### Primary Request Path: Finalize and produce an invoice

### Primary Request Path: Startup

### Secondary Flow: Price book for the main list

- Application state lives in ViewModels (`ObservableObject` with `[ObservableProperty]`), and the database is the source of truth. Windows reload from the database after saves (`MainViewModel.AfterEditor`).
- Jobs are edited in memory in `JobEditorViewModel.Job` and only written on save. Unsaved edits are tracked with `IsDirty`.
- There is no in-memory cache of jobs or materials between operations. Each `Database` call opens a fresh SQLite connection.

## Key Abstractions

- Purpose: the one persistence gateway.
- Examples: `src/PrintInvoicer.Core/Data/Database.cs`
- Pattern: concrete class, one method per operation, `Exec`, `Scalar<T>` and `Command` helpers for parameters. Money is stored as invariant-culture text.
- Purpose: everything needed to price a job, indexed by id, with no I/O.
- Examples: `src/PrintInvoicer.Core/Pricing/PriceBook.cs`
- Pattern: immutable snapshot built by `Database.LoadPriceBook()`; `RulesVersion` selects the rule set.
- Purpose: freeze a job's prices at finalize and restore them later, so finalized invoices never change.
- Examples: `src/PrintInvoicer.Core/Pricing/PriceSnapshot.cs`
- Pattern: `Capture` serializes the price inputs to text on `Job.PriceSnapshot`; `Restore` rebuilds a `PriceBook` from it; `For` returns the right book for a job (snapshot if finalized, current otherwise).
- Purpose: one invoice's data in a single tree: `Job` -> `JobPart` -> `JobPlate` -> `PlateFilament` / `PlateResin`.
- Examples: `src/PrintInvoicer.Core/Models/Job.cs`
- Pattern: plain mutable classes with `Id` set by the database. `Job.FinalizeInvoice` owns the finalize state change.
- Purpose: pricing results for display and PDF, kept separate from models.
- Examples: `JobQuote`, `PartQuote`, `PlateQuote`, `SlotQuote`, `ResinQuote`, `ExtraCostQuote` in `src/PrintInvoicer.Core/Pricing/PricingCalculator.cs`.
- Pattern: C# `record` types with computed totals.
- Purpose: a format-neutral view of what a slicer file contains.
- Examples: `src/PrintInvoicer.Core/Slicing/SlicedFile.cs`
- Pattern: `SlicedFile` -> `SlicedPlate` -> `SlicedFilament` / `SlicedResin`. Readers produce these; `JobBuilder` consumes them.
- Purpose: display rows for lists.
- Examples: `JobRowViewModel` (`MainViewModel.cs`), `PrinterRowViewModel` and `ExtraCostRowViewModel` (`PrintersViewModel.cs`), `FilamentRowViewModel` and `ResinRowViewModel` (`FilamentsViewModel.cs`).

## Entry Points

- Location: `src/PrintInvoicer.App/App.xaml.cs` (`App.OnStartup`). `App.xaml` has no `StartupUri`; the main window is created and shown in code.
- Triggers: launching `PrintInvoicer.exe`, installed through `publish.ps1` or the release zip.
- Responsibilities: license setup, error handlers, database open, backup, main window.
- Location: `src/PrintInvoicer.App/MainWindow.xaml.cs` (`MainWindow()`).
- Triggers: app start.
- Responsibilities: create `MainViewModel`, open `JobEditorWindow` through the `OpenEditor` delegate, start the update check.
- Location: `src/PrintInvoicer.App/JobEditorWindow.xaml.cs`, `JobEditorViewModel.cs`.
- Triggers: New job, Open job (`MainViewModel.NewJob` / `OpenJob`), or double-clicking a row.
- Location: `Database` constructor and `EnsureCreated` in `src/PrintInvoicer.Core/Data/Database.cs`.
- Triggers: every app start, and every `new Database(...)` in tests.
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

### Fresh connection per call

### Business logic in ViewModels that calls the file system or shell

## Error Handling

- Readers throw `InvalidDataException` with user-readable messages for bad input (`SliceFileReader.Read`, `BambuFileReader`, `GooFileReader`). Other exception types are not allowed from readers (see `CLAUDE.md`).
- ViewModels catch `IOException` around PDF writes and report a "PDF is open in another program" message (`MainViewModel.GenerateInvoices`).
- Startup failures are shown by `ErrorReport.Show` and the app calls `Shutdown(1)` (`App.OnStartup`).
- Unhandled UI exceptions are caught by `DispatcherUnhandledException` and shown with `ErrorReport.Show`; the app keeps running.
- Backup failures are stored in `App.BackupProblem` and shown in Settings (`SettingsViewModel.RefreshBackupStatus`).

## Cross-Cutting Concerns

<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->

## Project Skills

No project skills found. Add skills to any of: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, `.github/skills/`, or `.codex/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->

## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd-fast` for a trivial task inline, with no subagents and no PLAN.md
- `/gsd-quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd-debug` for investigation and bug fixing
- `/gsd-execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->

<!-- GSD:profile-start -->

## Developer Profile

> Profile not yet configured. Run `/gsd-profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->

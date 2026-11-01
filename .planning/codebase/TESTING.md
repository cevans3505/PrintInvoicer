---
last_mapped_commit: 2df24f47743d36dde2022025fa255f942c9bc7ae
last_mapped_at: 2026-11-01
---
# Testing Patterns

**Analysis Date:** 2026-11-01

## Test Framework

**Runner:**
- xUnit 2.9.3 (`xunit`) with `xunit.runner.visualstudio` 3.1.4 and `Microsoft.NET.Test.Sdk` 17.14.1, in both test projects.
- Config: no `xunit.runner.json` is present. Defaults apply. The UI test project sets `[assembly: CollectionBehavior(DisableTestParallelization = true)]` in `tests/PrintInvoicer.UiTests/WpfHost.cs`.
- Project files: `tests/PrintInvoicer.Tests/PrintInvoicer.Tests.csproj` (targets `net10.0`, runs anywhere) and `tests/PrintInvoicer.UiTests/PrintInvoicer.UiTests.csproj` (targets `net10.0-windows`, `UseWPF`, Windows only).
- `Xunit` is imported globally in both projects (`<Using Include="Xunit" />`), so test files do not need `using Xunit;`.
- `NoWarn` includes `WPF0001` in the UI test project because the Fluent theme API is marked experimental.

**Assertion Library:**
- xUnit's built-in `Assert` class only. No FluentAssertions or Shouldly.
- Common calls: `Assert.Equal`, `Assert.True`, `Assert.Single`, `Assert.Contains`, `Assert.DoesNotContain`, `Assert.All`, `Assert.Empty`, `Assert.NotNull`, `Assert.Throws`, `Assert.Fail`, `Assert.InRange`, `Assert.StartsWith`, `Assert.EndsWith`.
- `Assert.Single(...)` is used to get and check the only element at the same time: `var plate = Assert.Single(Assert.Single(back.Parts).Plates);` in `tests/PrintInvoicer.Tests/DatabaseTests.cs`.

**Coverage tooling:**
- `coverlet.collector` 6.0.4 is referenced by the unit test project, so `dotnet test --collect:"XPlat Code Coverage"` works there.
- No coverage threshold is enforced anywhere in the repo.

**Run Commands:**
```bash
dotnet test PrintInvoicer.sln                          # Everything: unit and screen tests (Windows)
dotnet test tests/PrintInvoicer.Tests                  # Unit tests only (Windows or Linux)
dotnet test tests/PrintInvoicer.UiTests                # Screen tests only (Windows desktop session)
dotnet test tests/PrintInvoicer.Tests -c Release --no-build \
  --logger "trx;LogFileName=unit-tests.trx" --results-directory TestResults   # The CI form
dotnet test PrintInvoicer.sln --collect:"XPlat Code Coverage"                 # Coverage (unit tests)
```
- `publish.ps1` runs `dotnet test PrintInvoicer.sln --nologo` before it publishes, and stops the publish when a test fails.
- CI (`.github/workflows/ci.yml`) runs two jobs on `windows-latest`: "Build and unit tests" and "Screen tests (WPF, offscreen)". Each uploads its `.trx` file. The screen test job also uploads `tests/PrintInvoicer.UiTests/bin/Release/net10.0-windows/screens/`.
- Watch mode: not configured. Use `dotnet watch test --project tests/PrintInvoicer.Tests` if needed; it is not part of the repo's scripts.

## Test File Organization

**Location:**
- Tests live in two separate projects, not beside the source:
  - `tests/PrintInvoicer.Tests/` for pure logic: pricing, database, readers, backups, filters, versions. It references `src/PrintInvoicer.Core` only.
  - `tests/PrintInvoicer.UiTests/` for WPF windows and view models. It references `src/PrintInvoicer.App`.
- The UI test project links two shared files from the unit test project rather than copying them: `<Compile Include="..\PrintInvoicer.Tests\TestFiles.cs" Link="TestFiles.cs" />` and the `Fixtures` folder. See `tests/PrintInvoicer.UiTests/PrintInvoicer.UiTests.csproj`.

**Naming:**
- Test classes: `<Area>Tests` (`PricingTests`, `DatabaseTests`, `BackupTests`, `FinalizeTests`, `JobFilterTests`).
- Hostile-input suites: `<Format>RobustnessTests` (`BambuRobustnessTests`, `GooRobustnessTests`).
- Reader format suites: `BambuFileReaderTests`, `ResinTests` (the `.goo` reader's happy-path tests).
- Test methods: snake_case sentences, e.g. `Matches_workbook_job_CS001`, `Printed_flag_round_trips`, `Used_filament_is_deactivated_not_deleted`.

**Structure:**
```
tests/PrintInvoicer.Tests/
├── PricingTests.cs              # Matches the costing workbook; fees; overrides
├── PricingRulesTests.cs         # Rules versions; sub-cent unit prices
├── PriceSnapshotTests.cs        # Frozen prices for finalized invoices
├── DatabaseTests.cs             # Settings, numbering, jobs, mappings; uses a temp .db
├── BackupTests.cs               # Daily and manual backups; restore; pruning
├── FinalizeTests.cs             # Dating and re-dating finalized invoices
├── JobFilterTests.cs            # Search columns, date ranges, paid/printed filters
├── FilamentMatcherTests.cs      # Matching sliced filaments to the materials list
├── ReleaseVersionTests.cs       # Version tag parsing and the daily update check
├── BambuFileReaderTests.cs      # Reads the sample .gcode.3mf files
├── BambuRobustnessTests.cs      # Hostile .gcode.3mf archives (abstract base class)
├── ResinTests.cs                # .goo header reading, preview transparency, resin pricing
├── GooRobustnessTests.cs        # Truncated, corrupted and nonsense .goo headers
├── ReaderContract.cs            # Shared checks every reader must pass (helper, not a test)
├── TestFiles.cs                 # Fixture paths and a .goo header builder (helper)
└── Fixtures/                    # Sample Bambu files (copied to output)
```
- `tests/PrintInvoicer.UiTests/` contains `ScreenTests.cs` (9 tests), `WpfHost.cs` (the offscreen WPF host), `SampleShop.cs` (sample database and jobs), and `PrintInvoicer.UiTests.csproj`.

## Test Structure

**Suite organization:**
Plain test classes, no base class except where builders are shared. A class is either a constructor-and-Dispose lifecycle (for temp files) or static.

```csharp
// tests/PrintInvoicer.Tests/DatabaseTests.cs
public sealed class DatabaseTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pi-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    [Fact]
    public void Invoice_numbers_advance()
    {
        var db = new Database(_path);
        var date = new DateTime(2026, 10, 28);
        Assert.Equal("2026-001", db.AllocateInvoiceNumber(date));
        Assert.Equal("2026-002", db.AllocateInvoiceNumber(date));
    }
}
```

**Patterns:**
- **Setup:** A constructor or field initializer creates the temp folder or database file. xUnit makes a new instance for every test method, so each test gets its own path.
- **Teardown:** `Dispose()` clears SQLite pools first (`Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()`), then deletes the file or folder. Without the pool clear, the file is locked and the delete fails on Windows.
- **Assertion:** Plain `Assert.*` calls, one behaviour per test. Several related assertions in one test are fine when they describe one scenario.
- **Comments in tests:** Tests explain the business rule they check, with the numbers from the source. Example from `tests/PrintInvoicer.Tests/PricingTests.cs`: `// CS-001 in the workbook: Wire_Brush_Insert plate printed twice, electricity at $0.196/kWh.`
- **Shared sample data:** The fixtures are real sliced files with sample names in them (`Wire_Brush_Insert.gcode.3mf`, `Cylindex_Tube_Cap.gcode.3mf`). Load them with `TestFiles.Fixture("...")` or `Path.Combine(AppContext.BaseDirectory, "Fixtures", name)`.

**Temp files, by convention:**
- Database tests: `Path.Combine(Path.GetTempPath(), $"pi-<area>-{Guid.NewGuid():N}.db")`, deleted in `Dispose`.
- Folder tests: `Path.Combine(Path.GetTempPath(), $"pi-<area>-{Guid.NewGuid():N}")`, removed with `Directory.Delete(_dir, recursive: true)`.
- Never write to a fixed path. Never write to the user's `Documents\PrintInvoicer` folder from a test. `Database.DefaultPath` must not be used in tests.

**Parameterised tests:**
- `[Theory]` with `[InlineData]` for short lists (`ReleaseVersionTests.Parses_tags`).
- `[Theory]` with `[MemberData]` for generated cases. `GooRobustnessTests` has `TruncatedLengths()` and `BadLayerHeights()` returning `TheoryData<T>`, so every truncation point becomes its own test case.

## Mocking

**Framework:** None. There is no Moq, NSubstitute or other mocking library in the repo.

**Patterns: fakes passed as delegates or replaceable properties.** The code is written so tests can swap a dependency for a lambda:

```csharp
// tests/PrintInvoicer.UiTests/ScreenTests.cs
var updates = new UpdateService(new Version(3, 3, 0), _ => Task.FromResult<ReleaseInfo?>(
    new(new Version(3, 4, 0), $"https://github.com/{UpdateService.Repository}/releases/tag/v3.4.0")), state);
main.ViewModel.Updates = updates;
```
- `UpdateService` takes a `Func<CancellationToken, Task<ReleaseInfo?>>` fetch delegate and a state-file path, so no test touches the network. See `src/PrintInvoicer.App/UpdateService.cs`.
- `JobEditorViewModel.ShowNotes` is an `Action<string>`; a test sets it to `_ => { }` so no notes dialog opens.
- `MainViewModel.Updates` and `MainViewModel.OpenEditor` are replaceable for the same reason.
- Pricing tests build the inputs directly as objects (`new Job`, `new Printer`, `new Filament`) instead of building a fake database: see `CrealityFilaments()` in `tests/PrintInvoicer.Tests/PricingTests.cs`.

**What to Mock (in this codebase's style):**
- Anything that reaches the network: pass a delegate like `UpdateService` does. Do not add a real HTTP call to a test.
- Anything that shows a dialog: use a delegate or property, default it to a no-op in tests, and never call `MessageBox.Show` from a test.
- Clock: pass the date in as a parameter (`DateTime day`), as `DatabaseBackup.DailyIfDue(path, folder, day)` and `job.FinalizeInvoice(date, redate: false)` do.

**What NOT to Mock:**
- The database. Tests use a real SQLite file in a temp folder, so schema upgrades, transactions and foreign keys are tested for real (see `Older_database_gets_printed_column` in `DatabaseTests.cs`, which creates an old-schema table by hand with raw SQL).
- The readers. Tests feed real bytes or real sample files through `BambuFileReader`, `GooFileReader` and `SliceFileReader`.
- The pricing engine. Tests call `PricingCalculator.Calculate` on real objects.

**Test builders instead of mocks:**
- `TestFiles.GooHeader(...)` in `tests/PrintInvoicer.Tests/TestFiles.cs` builds a `.goo` header with optional parameters (printer, profile, layers, seconds, volume, background colour). Call it with only the values a test changes.
- `BambuReaderTests` in `tests/PrintInvoicer.Tests/BambuRobustnessTests.cs` (an abstract base class) provides `Zip(...)`, `ThreeMf(...)`, `Plate(...)` and `Write(...)` to build `.gcode.3mf` archives in memory.
- `SampleShop` in `tests/PrintInvoicer.UiTests/SampleShop.cs` builds a full throwaway database with an FDM job, a resin job and a finalized invoice.

## Fixtures and Factories

**Test Data:**
- Real sliced files from the costing workbook, stored under `tests/PrintInvoicer.Tests/Fixtures/`:
  - `Wire_Brush_Insert.gcode.3mf` (FDM, used for the workbook match in `PricingTests.Matches_workbook_job_CS001`)
  - `Cylindex_Tube_Cap.gcode.3mf` (FDM, two plates; used in `DatabaseTests` and `PricingTests`)
- Object builders for in-memory models:
  ```csharp
  // tests/PrintInvoicer.Tests/PricingRulesTests.cs
  private static readonly Printer Fdm = new() { Id = 1, Name = "FDM", Technology = PrintTechnology.Fdm, MachineRatePerHour = 1m };

  private static Job BulkJob(int pieces, int seconds) => new()
  {
      Parts = { new JobPart { Name = "Spacer", ChargeSlicing = false,
          Plates = { new JobPlate { PrinterId = 1, PartsPerPlate = pieces, SlicedSeconds = seconds } } } },
  };
  ```
- Seeded database contents: a new database gets 13 starter filaments and one resin, which tests rely on (`Seeds_filaments_once` asserts 13).

**Location:**
- Fixture files: `tests/PrintInvoicer.Tests/Fixtures/`, copied to the output folder by the `<None Include="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />` item in both test projects.
- Builder helpers: `tests/PrintInvoicer.Tests/TestFiles.cs` (shared with the UI tests through a linked `Compile` item).
- Per-suite builders: private static methods at the top of each test class.

**Sample names:** Use the sample names already in use: "Harbor Dive Shop" (customer), "Sample Printing Co." (business name), "Wire Brush Insert", "Mini Thread Viewers", "Regulator clips". Do not add real customer or personal data (see `CLAUDE.md`).

## Coverage

**Requirements:** None enforced. There is no threshold in the csproj files, the CI workflow or `publish.ps1`.

**View Coverage:**
```bash
dotnet test tests/PrintInvoicer.Tests --collect:"XPlat Code Coverage" --results-directory TestResults
```
The coverage report is written as a Cobertura XML file under `TestResults/`.

**Where coverage is strongest:** pricing (`PricingCalculator`, `PriceSnapshot`, `PricingRules`), the database and its migrations, backups, the three slicing readers, job filters, and version parsing. The UI is covered by the screen tests at the level of "loads, binds, totals up, locks", not by pixel or interaction tests.

## Test Types

**Unit Tests (`tests/PrintInvoicer.Tests`):**
- Scope: pure logic in `PrintInvoicer.Core` and the data layer, run on the real SQLite engine in temp files.
- Approach: arrange real objects, call one method, assert on the result. Rules are checked against expected numbers from the costing workbook.
- Runs on Linux too (CI runs the unit tests on `windows-latest`, but the project itself has no Windows-only API).

**Robustness and Contract Tests (in the unit project):**
- Scope: every reader in `src/PrintInvoicer.Core/Slicing/` on hostile input: truncated files, flipped magic bytes, nonsense floats, oversized plate counts, broken zip entries, bad PNGs.
- Approach: `ReaderContract` (`tests/PrintInvoicer.Tests/ReaderContract.cs`) runs a reader on a background task with a 15-second timeout and checks that it either returns sensible values or throws `InvalidDataException` with the reader's own message. Assertions:
  - `ReaderContract.Check(read, label)` returns the file or `null` when it was rejected properly.
  - `ReaderContract.Rejects(read, label)` requires a rejection and returns the exception.
  - `ReaderContract.Reads(read, label)` requires a successful read.
  - `ReaderContract.AssertSensible(file, label)` checks the value ranges against the limits in `SliceLimits`.
- Every new reader or new field must pass through this contract. A new hostile case goes in the `*RobustnessTests.cs` suite for that format.

**Screen Tests (`tests/PrintInvoicer.UiTests`, Windows only):**
- Scope: the real WPF windows (`MainWindow`, `JobEditorWindow`) and view models, on sample data. They check that the windows load, bindings resolve, totals match the invoice, finalized jobs are locked, and every invoice kind renders to a PNG.
- Runs offscreen: see the WPF rules below.
- CI runs them in a separate job so a desktop-session problem is easy to spot.

**Integration Tests:**
- No separate integration project. The database tests (`DatabaseTests`, `BackupTests`, `FinalizeTests`, `PricingRulesTests`) cover the storage and pricing path end to end with a real SQLite file.

**E2E Tests:** Not used.

## WPF Screen Tests: the Offscreen Rule

These rules come from `CLAUDE.md` and are enforced by the design of `WpfHost`. Follow them for every new UI test.

- **Never show, activate or focus a window on the screen.** Create windows through `WpfHost.Offscreen(window)`, which sets:
  - `WindowStartupLocation = Manual`, `Left = -20000`, `Top = -20000`
  - `ShowActivated = false`, `ShowInTaskbar = false`
  - then `Show()` and `Settle()`.
- **Never trigger a dialog.** Do not call anything that reaches `MessageBox.Show`, `OpenFileDialog`, `SaveFileDialog`, `Process.Start`, or a method that opens a folder. Before a test can close a job window, clear `IsDirty` so no save prompt appears: `CloseQuietly` in `ScreenTests.cs` does this.
- **Use `WpfHost`.** Every test body runs as `WpfHost.Run(() => { ... })` (or `WpfHost.Run(() => expression)` for a return value). WPF objects must be created and used on the one STA thread that `WpfHost` runs. Creating a window on the xUnit thread fails.
- **Do not run in parallel.** `[assembly: CollectionBehavior(DisableTestParallelization = true)]` is set in `WpfHost.cs`, so every test uses the one WPF thread without overlap.
- **Keep the app's startup state.** `WpfHost` starts the app's resources (theme, converters, the clear-button hook), sets `QuestPDF.Settings.License`, turns off the update check on launch (`MainWindow.CheckForUpdatesOnLaunch = false`), and points `WindowLayout.FilePath` at a temp file. Do not change those in a test.
- **Keep the network out.** Pass a fake `UpdateService` fetch (see Mocking above).

**What `WpfHost` provides:**
| Member | Use |
|---|---|
| `WpfHost.Run(Action)` / `Run<T>(Func<T>)` | Run code on the WPF thread and wait for it |
| `WpfHost.Offscreen(window)` | Show a window where no one can see it, and settle layout |
| `WpfHost.Settle(window)` | `UpdateLayout()` and wait for the application-idle queue |
| `WpfHost.Screenshot(window, name)` | Save a PNG to the `screens` folder for a person to review. Nothing is compared against it |
| `WpfHost.Descendants<T>(root)` | Find controls in the visual tree, e.g. `WpfHost.Descendants<TabControl>(main).First()` |
| `WpfHost.TakeBindingErrors()` | Binding errors logged since the last call (WPF only logs them with tracing on, which `WpfHost` turns on) |
| `WpfHost.ScreenshotFolder`, `WpfHost.TempFolder` | Output and temp locations (`bin/.../screens` and `%TEMP%\PrintInvoicer UI tests`) |

**Binding errors:** `ScreenTests.NoBindingErrors(where)` fails a test when any binding error was logged. Call it at the end of every window test. `A_broken_binding_is_caught` proves the check works by binding to a missing property and expecting the error to be captured. Keep that canary test; it is what makes a pass meaningful.

**Sample database for UI tests:** `SampleShop` creates a temp database and sets `App.Db` to it, as `App.OnStartup` would for the real one. Create one per test class instance (the test class constructor does this) and call `Dispose()` at the end. `SampleShop.Reload(job)` returns a fresh copy of a saved job when the test needs the saved totals.

**Example:**
```csharp
[Fact]
public void Finalized_job_window_is_locked_at_its_prices() => WpfHost.Run(() =>
{
    var sent = _shop.Reload(_shop.FinalizedJob).FinalizedTotal!.Value;
    // ... change printer rates, then open the window
    var window = OpenEditor(_shop.FinalizedJob, out var vm);
    WpfHost.Screenshot(window, "editor-finalized");
    Assert.False(vm.IsEditable);
    Assert.Equal(Format.Money(sent), vm.TotalText);
    CloseQuietly(window, vm);
    NoBindingErrors("The finalized job window");
});
```

**Screen test helpers in `ScreenTests.cs`:**
- `OpenEditor(job, out vm)`: builds a `JobEditorViewModel` with `ShowNotes` set to a no-op and shows the window offscreen.
- `CloseQuietly(window, vm)`: clears `IsDirty` and closes.
- `NoBindingErrors(where)`: fails with the first ten binding errors.
- `TestFilesPath(name)`: fixture path for file-based tests.

## Common Patterns

**Async testing:**
- Tests are synchronous. When a method is async, block on it with `GetAwaiter().GetResult()` (see `Update_bar_shows_only_for_a_newer_release`, which awaits `CheckOnStartupAsync(true)` with a fake that returns at once).
- Do not add `Thread.Sleep` or fixed delays. Use the fake that answers immediately, or `WpfHost.Settle(...)` for WPF work.
- The reader contract wraps a reader in `Task.Run` and waits up to 15 seconds with `task.Wait(Timeout)`, so a hang is a failure with a message rather than a stuck test run.

**Error testing:**
```csharp
// Expected rejection with the reader's own message (tests/PrintInvoicer.Tests/BambuRobustnessTests.cs pattern)
var ex = ReaderContract.Rejects(() => SliceFileReader.Read(path), "label");
Assert.Contains("damaged", ex.Message);
```
- For the database and other plain code, `Assert.Throws<T>(() => ...)` or a `try/catch` with `Assert.Fail` in the `catch` is fine.
- Check the user-facing message, not only the type. Messages are part of the contract.

**Floating-point and decimal rules:**
- Money is `decimal`, so compare with `Assert.Equal(0.91m, value)`, not a tolerance. Use `Math.Round(value, 4)` only when the test is about a rounding step.
- Compare dates as `DateTime` values with an explicit `new DateTime(2026, 10, 28)`, not `DateTime.Now`.

**Round trips:**
- Save then reload through the database, then assert the reloaded object (see `Job_round_trips_with_plates_and_slots`). Save a second time to prove that rewriting children does not duplicate them.

**Migrations:**
- Build an "old" schema by hand with raw SQL in the test, then open it with `new Database(path)` and assert the upgrade (see `Older_database_gets_printed_column` in `DatabaseTests.cs`). Every schema change needs a test like this, because `CLAUDE.md` requires existing data to keep its meaning.

**Pricing rules:**
- A change to totals needs a test that pins the old behaviour under the old `PricingRules` version and the new behaviour under the new one (see `PricingRulesTests.Older_rules_round_to_cents_and_warn_when_a_part_bills_nothing`). Keep the old assertions when you add a version.

## Adding a Test: Checklist

- Put a pure-logic test in `tests/PrintInvoicer.Tests/` under the matching `<Area>Tests.cs`. Create the file if the area has none.
- Put a window or view-model test in `tests/PrintInvoicer.UiTests/ScreenTests.cs`, inside `WpfHost.Run(...)`, with `WpfHost.Offscreen` and `CloseQuietly`.
- Use a temp path with a GUID name, and clean it up in `Dispose()`. Clear SQLite pools first.
- No network, no dialogs, no `DateTime.Now` in the logic under test.
- Name the test as a sentence with underscores. Put a short comment with the business rule or the source number.
- For a new reader or format field, add a hostile case to the `*RobustnessTests.cs` suite so `ReaderContract` checks it.
- Run `dotnet test tests/PrintInvoicer.Tests` before you finish. Run the UI tests only through the solution command on a Windows machine, and never by hand while you are only reading code.

---

*Testing analysis: 2026-11-01*

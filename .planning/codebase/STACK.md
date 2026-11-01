---
last_mapped_commit: 2df24f47743d36dde2022025fa255f942c9bc7ae
last_mapped_at: 2026-11-01
---
# Technology Stack

**Analysis Date:** 2026-11-01

## Languages

**Primary:**
- C# (language version from the .NET 10 SDK, nullable reference types and implicit usings enabled in every project) - all code in `src/PrintInvoicer.Core/`, `src/PrintInvoicer.App/`, `tests/PrintInvoicer.Tests/`, `tests/PrintInvoicer.UiTests/`
- XAML - WPF windows and resources: `src/PrintInvoicer.App/App.xaml`, `src/PrintInvoicer.App/MainWindow.xaml`, `src/PrintInvoicer.App/JobEditorWindow.xaml`, `src/PrintInvoicer.App/Assets/Icon.xaml` (drawing source for the icon, not app UI)

**Secondary:**
- PowerShell - build, install and icon tooling: `publish.ps1`, `tools/Make-Icon.ps1`, and the inline scripts in `.github/workflows/release.yml`
- YAML - GitHub Actions workflows: `.github/workflows/ci.yml`, `.github/workflows/release.yml`
- Markdown and plain text - user docs and packaging: `README.md`, `AUDITING.md`, `CLAUDE.md`, `packaging/Read me.txt` (shipped inside the release zip)

## Runtime

**Environment:**
- .NET 10 (`net10.0` for the Core library and tests, `net10.0-windows` for the WPF app and the UI tests)
- Windows only for the app and UI tests. The Core library and unit tests have no Windows-only API and run on Linux (`dotnet test tests/PrintInvoicer.Tests`).
- SDK pinned in `global.json`: `10.0.403`, with `"rollForward": "latestPatch"`.
- Release target: `win-x64`, self-contained, single-file executable (`PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, `EnableCompressionInSingleFile=true` in `src/PrintInvoicer.App/PrintInvoicer.App.csproj`). Users do not need a .NET install.

**Package Manager:**
- NuGet via `dotnet restore` (driven by the `.csproj` files; no central package management file was found)
- Lockfile: missing. No `packages.lock.json` or `Directory.Packages.props` is tracked in the repository, so package versions are pinned only by the explicit `Version=` attributes in each `.csproj`.
- Solution file: `PrintInvoicer.sln`

## Frameworks

**Core:**
- WPF (Windows Presentation Foundation) - the desktop UI, `src/PrintInvoicer.App/`. Enabled with `<UseWPF>true</UseWPF>`. Dark theme set with `ThemeMode="Dark"` and the `PresentationFramework.Fluent` dark dictionary in `src/PrintInvoicer.App/App.xaml`. The `WPF0001` warning (experimental `ThemeMode`) is suppressed in the app and UI test projects.
- MVVM via CommunityToolkit.Mvvm 8.4.2 - view models in `src/PrintInvoicer.App/ViewModels/` use `ObservableObject`, `RelayCommand` and source generators (`[ObservableProperty]`, `[RelayCommand]`).
- QuestPDF 2026.9.1 - PDF layout and rendering for invoices and detail sheets, `src/PrintInvoicer.App/Pdf/InvoicePdf.cs`. The license is set to Community at startup in `src/PrintInvoicer.App/App.xaml.cs` (`QuestPDF.Settings.License = LicenseType.Community`).
- Microsoft.Data.Sqlite 10.0.14 - SQLite access, `src/PrintInvoicer.Core/Data/Database.cs` and `src/PrintInvoicer.Core/Data/DatabaseBackup.cs`. Hand-written SQL, no ORM. The database is a plain file; the native SQLite engine comes in through SQLitePCLRaw.
- Standard .NET libraries used directly: `System.IO.Compression` (opens `.gcode.3mf` zip archives in `src/PrintInvoicer.Core/Slicing/BambuFileReader.cs`), `System.Xml` (`XmlReader` with DTDs prohibited and `XmlResolver = null`), `System.Text.Json` (update check state and GitHub API reply, `src/PrintInvoicer.App/UpdateService.cs`), `System.Net.Http.HttpClient` (single GitHub request, `UpdateService.cs`), and a hand-written PNG encoder in `src/PrintInvoicer.Core/Slicing/PngWriter.cs`.
- Printing uses WPF's own `System.Windows.Controls.PrintDialog` (`src/PrintInvoicer.App/Pdf/InvoicePrinter.cs`). No Windows Forms dependency is referenced.

**Testing:**
- xUnit 2.9.3 with `xunit.runner.visualstudio` 3.1.4 and `Microsoft.NET.Test.Sdk` 17.14.1 - both test projects, `tests/PrintInvoicer.Tests/PrintInvoicer.Tests.csproj` and `tests/PrintInvoicer.UiTests/PrintInvoicer.UiTests.csproj`. `Xunit` is globally imported in both.
- coverlet.collector 6.0.4 - code coverage collector, referenced by `tests/PrintInvoicer.Tests/PrintInvoicer.Tests.csproj` only.
- UI tests (`tests/PrintInvoicer.UiTests/`) open the real WPF windows offscreen through `WpfHost.cs`. They need an interactive Windows desktop session.
- Test fixtures: sample sliced files in `tests/PrintInvoicer.Tests/Fixtures/` (`Cylindex_Tube_Cap.gcode.3mf`, `Wire_Brush_Insert.gcode.3mf`), copied to output by the test `.csproj` files.

**Build/Dev:**
- .NET SDK 10.0.403 (`global.json`) - build, test, publish.
- `dotnet publish` with the release switches above; the same arguments appear in `publish.ps1` and `.github/workflows/release.yml`.
- `publish.ps1` - runs all tests, publishes, installs to `%LOCALAPPDATA%\Programs\PrintInvoicer`, creates a Start menu shortcut. Runs in Windows PowerShell 5.1.
- `tools/Make-Icon.ps1` - renders `src/PrintInvoicer.App/Assets/Icon.xaml` into `src/PrintInvoicer.App/Assets/PrintInvoicer.ico`. Developer tool only.
- GitHub Actions (`actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/upload-artifact@v7`, `actions/download-artifact@v8`), running on `windows-latest`.

## Key Dependencies

**Critical:**
- `QuestPDF` 2026.9.1 (App) - the whole invoice and detail-sheet layout. Community license. Ships native Skia and qpdf libraries (`QuestPdfSkia.dll`, `qpdf.dll`) that are bundled into the single-file exe.
- `Microsoft.Data.Sqlite` 10.0.14 (Core) - the only persistence layer. Ships the native `e_sqlite3.dll` via SQLitePCLRaw.
- `CommunityToolkit.Mvvm` 8.4.2 (App) - MVVM base classes and source generators. No runtime I/O.

**Infrastructure:**
- `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4, `coverlet.collector` 6.0.4 - test-only; not shipped in the release.
- Project references: `PrintInvoicer.App` references `PrintInvoicer.Core`. Both test projects reference the projects they test. `PrintInvoicer.UiTests` also links `tests/PrintInvoicer.Tests/TestFiles.cs` and the fixtures.

**Versioning:**
- The app version is `<Version>3.6.0</Version>` in `src/PrintInvoicer.App/PrintInvoicer.App.csproj`. The release workflow requires the git tag to be exactly `v<Version>`. `packaging/Read me.txt` carries a `PrintInvoicer 3.6` heading that is checked (with a warning only) against the major and minor version.
- Version comparison helpers: `src/PrintInvoicer.Core/Updates/ReleaseVersion.cs`.

## Configuration

**Environment:**
- No environment variables are read by the application code. Data location comes from Windows special folders: `Documents\PrintInvoicer\printinvoicer.db` (`Database.DefaultPath`) and `%LOCALAPPDATA%\PrintInvoicer\` for small state files.
- User settings are stored in the SQLite `settings` table (key/value text, `src/PrintInvoicer.Core/Data/Database.cs`) and mapped to `src/PrintInvoicer.Core/Models/AppSettings.cs`. Notable keys include `CheckForUpdates` (default `true`) and `BackupFolder` (empty means `Backups` next to the database).
- Build-time configuration lives only in the `.csproj` files and `global.json`. No `appsettings.json`, `app.config` or user secrets file is tracked.
- Environment files: none tracked. The `.gitignore` covers build output; no `.env` file is present in the repository.
- Slice-file size and count limits: `src/PrintInvoicer.Core/Slicing/SliceLimits.cs`.

**Build:**
- `global.json` - pins the SDK.
- `PrintInvoicer.sln` - solution with the two `src` projects and two `tests` projects.
- `src/PrintInvoicer.Core/PrintInvoicer.Core.csproj` - net10.0, Microsoft.Data.Sqlite.
- `src/PrintInvoicer.App/PrintInvoicer.App.csproj` - net10.0-windows WinExe, WPF, QuestPDF, CommunityToolkit.Mvvm, icon and version.
- `tests/PrintInvoicer.Tests/PrintInvoicer.Tests.csproj` and `tests/PrintInvoicer.UiTests/PrintInvoicer.UiTests.csproj` - test projects.
- `.github/workflows/ci.yml` - restore, build (Release), unit tests on `windows-latest`, and a separate screen-test job for the UI tests.
- `.github/workflows/release.yml` - on a `v*` tag: checks the tag against `<Version>`, runs unit tests, publishes the single-file exe, zips it with `packaging/Read me.txt`, and creates a draft GitHub release.

## Platform Requirements

**Development:**
- Windows 10 or 11 with the .NET 10 SDK (10.0.403 or a later patch).
- Windows PowerShell 5.1 or PowerShell 7 for `publish.ps1`. The app is closed before publishing (the script refuses to run while `PrintInvoicer` is running).
- Core library and `tests/PrintInvoicer.Tests` can build and test on Linux or macOS. The App and UI tests need Windows and WPF.
- An interactive desktop session for `tests/PrintInvoicer.UiTests`.

**Production:**
- Windows 10 or 11, 64-bit (`win-x64`). The exe is self-contained, so no .NET runtime install is needed.
- Distributed as `PrintInvoicer-<version>.zip` (exe plus `Read me.txt`) on GitHub Releases. The exe is not code-signed; the read-me warns that Windows may show an unknown-publisher prompt.
- Local data: `Documents\PrintInvoicer\` (database, backups, invoices) and `%LOCALAPPDATA%\PrintInvoicer\` (update check state, window layout, error details). See `AUDITING.md` for the full file list.
- Printing uses the Windows print dialog through WPF `PrintDialog` (`src/PrintInvoicer.App/Pdf/InvoicePrinter.cs`).

---

*Stack analysis: 2026-11-01*

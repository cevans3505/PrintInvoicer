---
last_mapped_commit: 2df24f47743d36dde2022025fa255f942c9bc7ae
last_mapped_at: 2026-11-01
---
# External Integrations

**Analysis Date:** 2026-11-01

PrintInvoicer is a local desktop program. It has one network request and no accounts, no hosted services and no
telemetry. `AUDITING.md` at the repository root lists every point where the program reaches outside itself (26 places,
each tagged with an `AUDIT:` comment). This document describes the same integrations from the code's side. Where the
two disagree, read the code.

## APIs & External Services

**GitHub REST API (the only network call in the shipped program):**
- Service: GitHub releases API, read-only, public, no authentication
- Endpoint: `GET https://api.github.com/repos/cevans3505/PrintInvoicer/releases/latest`
- Code: `FetchLatestAsync` in `src/PrintInvoicer.App/UpdateService.cs`. The repository name is the constant `UpdateService.Repository`.
- HTTP client: a single `HttpClient` built in `UpdateService.cs` with a `User-Agent` of `PrintInvoicer/1` and an `Accept` of `application/vnd.github+json`.
- Data sent: only those standard headers. Nothing about the user, the job data or the database.
- Data received: only the release tag (`tag_name`) and page address (`html_url`), deserialized with `System.Text.Json` into a private `LatestRelease` record.
- When: at startup at most once a day, unless `CheckForUpdates` is off in Settings (the default is on, `src/PrintInvoicer.Core/Models/AppSettings.cs`). Also when the user clicks **Check now**. Network failures are silent.
- Auth: none. No token is used, and the release workflow's `GITHUB_TOKEN` is used only by CI, never by the program.

**Browser and shell hand-offs (not API calls, but outside access):**
- GitHub release page: `UpdateService.Download` opens the release page in the default browser with `Process.Start` and `UseShellExecute`, and refuses any address outside this repository.
- GitHub new-issue page: `ErrorReport.IssuesPage` (`https://github.com/cevans3505/PrintInvoicer/issues/new`), opened only when the user clicks **Yes** in the error dialog (`src/PrintInvoicer.App/ErrorReport.cs`).
- Each of these is tagged `AUDIT: opens a program` in the code.

## Slicer File Inputs

The program reads files written by other programs. It never runs them and never changes them.

- **Bambu Studio** (`.gcode.3mf`, and plain `.gcode` exports)
  - Reader: `src/PrintInvoicer.Core/Slicing/BambuFileReader.cs`
  - Format: a `.gcode.3mf` is a ZIP archive. The reader opens it with `System.IO.Compression.ZipArchive` and reads only `Metadata/slice_info.config`, the project settings, and the plate picture (`Metadata/plate_N.png`, or the `_small` variant). The G-code inside is not read.
  - XML: parsed with `XmlReader` using `DtdProcessing.Prohibit` and `XmlResolver = null`, so no external entities are fetched.
  - Plain `.gcode`: only the comment header at the start of the file is read, capped by `SliceLimits.MaxGcodeHeaderChars`.
  - The original file is re-read later to get a sharper plate image for invoices (`BambuFileReader.ReadPlateImage`, tagged `AUDIT: file read`).
- **ELEGOO resin files** (`.goo`, Saturn and Mars printers; the format is also written by CHITUBOX and VoxelDance Tango)
  - Reader: `src/PrintInvoicer.Core/Slicing/GooFileReader.cs`
  - Format: fixed-width, big-endian header following ELEGOO's published GOO format spec V1.2. The reader checks the 8-byte magic tag at byte 4 and two delimiters before trusting any field.
  - Shared limits: `src/PrintInvoicer.Core/Slicing/SliceLimits.cs`. Every size is capped and every number is range-checked. Damaged input throws `InvalidDataException` with a user-readable message.
- **Other slicers** (CHITUBOX `.ctb`, Anycubic, Lychee and others) are not read. Users enter print time and resin volume by hand (see `packaging/Read me.txt`).
- Tests for these readers: `tests/PrintInvoicer.Tests/BambuFileReaderTests.cs`, `BambuRobustnessTests.cs`, `GooRobustnessTests.cs`, with sample files in `tests/PrintInvoicer.Tests/Fixtures/`.

## Operating System and Desktop Integrations

- **Windows print system**: `src/PrintInvoicer.App/Pdf/InvoicePrinter.cs` shows the WPF `PrintDialog` (`UserPageRangeEnabled = true`). The chosen printer receives the invoice pages through the Windows print spooler. Tagged `AUDIT: prints`.
- **Default PDF viewer**: each PDF created (preview, finalize, detail sheet) is opened with `Process.Start` and `UseShellExecute` in `src/PrintInvoicer.App/ViewModels/JobEditorViewModel.cs` and `MainViewModel.cs`.
- **Explorer**: the **Open** buttons in Settings open folders (`SettingsViewModel.OpenFolder`, `OpenBackupFolder`). The error dialog can select the saved error file in Explorer (`explorer.exe /select,...`).
- **Self-restart**: after a backup is restored, `SettingsViewModel.RestoreBackup` starts the same executable (`Environment.ProcessPath`) and this copy closes.
- **Windows special folders**: the database and invoices go under `Documents`, and small state files go under `%LOCALAPPDATA%\PrintInvoicer\`. Resolved with `Environment.GetFolderPath` (`Database.DefaultPath`, `UpdateService`, `WindowLayout`, `ErrorReport`).
- **Temporary files**: preview PDFs are written under `%TEMP%\PrintInvoicer previews\` (`src/PrintInvoicer.App/Pdf/InvoicePdf.cs`).
- **Start menu and icon cache**: only the developer's `publish.ps1` creates a Start menu shortcut (via `WScript.Shell`) and runs `ie4uinit.exe -show`. The shipped program does not.

## Data Storage

**Database:**
- Engine: SQLite, a single file, `Documents\PrintInvoicer\printinvoicer.db` (`Database.DefaultPath`, `src/PrintInvoicer.Core/Data/Database.cs`).
- Client: Microsoft.Data.Sqlite 10.0.14. Hand-written SQL with parameters for all values. Foreign keys are on for each connection.
- Connections: a new `SqliteConnection` per operation, pooled by the provider.
- Schema: created and upgraded only in `Database.EnsureCreated` with `CREATE TABLE IF NOT EXISTS` and `AddColumnIfMissing`. Tables include `settings`, `filaments`, `filament_mappings`, `jobs`, `parts`, `plates`, `plate_filaments`, `printers`, `resins` and `extra_costs`.
- No remote database, no connection string from configuration, and no credentials.

**Backups:**
- Module: `src/PrintInvoicer.Core/Data/DatabaseBackup.cs`, started from `App.BackUpTodayAsync` in `src/PrintInvoicer.App/App.xaml.cs`.
- Method: SQLite's online backup API, so a copy is consistent while the app is running.
- Location: `Documents\PrintInvoicer\Backups\` by default, or any folder set in Settings (`AppSettings.BackupFolder`). The app treats the folder as a plain path.
- Retention: the newest 14 daily copies (`printinvoicer-YYYY-MM-DD.db`) are kept; older daily copies are deleted.
- Cloud sync: none built in. The read-me tells users to point the backup folder at a OneDrive, Dropbox or network folder. The app does not call those services; the sync is done by the operating system or the vendor's client.
- Restore: `RestoreBackup` runs SQLite's integrity check on the chosen copy and checks for PrintInvoicer's tables before replacing data. The current data is backed up first.

**File storage:**
- Invoices and detail sheets: PDF files in `Documents\PrintInvoicer\Invoices\` or the folder set in Settings (`InvoicePdf.Write`). Local filesystem only.
- Plate pictures: stored inside the database as PNG bytes. `src/PrintInvoicer.Core/Slicing/PngWriter.cs` encodes slicer previews.

**Caching:**
- None. No Redis, memcached or similar.

## PDF Rendering

- QuestPDF 2026.9.1 renders all invoice and detail-sheet PDFs locally in `src/PrintInvoicer.App/Pdf/InvoicePdf.cs`. No cloud rendering or font service is used.
- License: Community, set at startup in `src/PrintInvoicer.App/App.xaml.cs`. Native Skia and qpdf libraries are bundled.

## Authentication & Identity

- **Auth provider**: none. The program has no users, logins, passwords, tokens, keys or OAuth flow.
- **Credentials**: none are stored. `AUDITING.md` lists this as a deliberate property.
- **Windows identity**: the program runs as the current Windows user and writes only under that user's profile.
- **Code signing**: not used. Release zips are not signed; `packaging/Read me.txt` tells users that Windows may show an unknown-publisher warning.

## Monitoring & Observability

**Error tracking:**
- None remote. `src/PrintInvoicer.App/ErrorReport.cs` writes a local text file per unexpected error to `%LOCALAPPDATA%\PrintInvoicer\Errors\` with the app version, Windows version, time and exception text. Nothing is uploaded.
- The user may choose to open the GitHub issue page and select the file themselves. The dialog warns that the file lists folder names.

**Logs:**
- No logging framework. Problems are shown in dialogs, or saved as the error files above, or recorded in `BackupProblem` for the Settings tab.

**Analytics:**
- None.

## CI/CD & Deployment

**Source hosting:** GitHub, repository `cevans3505/PrintInvoicer`, branch `main`.

**CI pipeline:** GitHub Actions, `.github/workflows/ci.yml`.
- Triggers: pushes to `main` and pull requests into `main`.
- Permissions: `contents: read` only.
- Jobs: `build-and-test` (restore, Release build, unit tests) and `screen-tests` (WPF UI tests offscreen), both on `windows-latest`.
- Actions used: `actions/checkout@v7`, `actions/setup-dotnet@v6` (reads `global.json`), `actions/upload-artifact@v7`.

**Release pipeline:** GitHub Actions, `.github/workflows/release.yml`.
- Trigger: a tag matching `v*`. The job fails if the tag is not exactly `v<Version>` from `src/PrintInvoicer.App/PrintInvoicer.App.csproj`.
- Build: runs unit tests, then `dotnet publish` for `win-x64`, single-file and self-contained, then zips `PrintInvoicer.exe` with `packaging/Read me.txt`.
- Publish: a second job with `contents: write` runs `gh release create --draft` with the zip attached. A person reviews and publishes the draft by hand.
- Actions used: `actions/download-artifact@v8`, plus the `gh` CLI preinstalled on the runner. Authentication is the job-scoped `secrets.GITHUB_TOKEN`.

**Hosting for distribution:** GitHub Releases, with `https://github.com/cevans3505/PrintInvoicer/releases` as the download page.

**Local install:** `publish.ps1` installs to `%LOCALAPPDATA%\Programs\PrintInvoicer` for development. It is not a deployment service.

## Environment Configuration

**Required environment variables:**
- None. The program reads no environment variables for configuration. The workflows use the standard `GITHUB_TOKEN`, `GITHUB_REF_NAME` and `GH_REPO` values provided by GitHub Actions.

**Settings that act like configuration** (stored in the SQLite `settings` table and edited on the Settings tab):
- `CheckForUpdates` (on by default): turns the one GitHub request on or off.
- `BackupFolder`: where daily backups go.
- Invoice folder: where invoice PDFs go.
- Company name, contact line, default customer, payment terms and electricity price, which appear on invoices.

**Secrets location:**
- None stored or needed. No `.env`, credentials or key files are tracked in the repository, and `.gitignore` does not list any such file as present. The only secret-like value in the pipeline is the CI-provided `GITHUB_TOKEN`, which is scoped to each job.

## Webhooks & Callbacks

**Incoming:**
- None. The program does not listen on any port and has no HTTP server.

**Outgoing:**
- None, apart from the single GitHub release check described above.

## Places to Check When Reviewing Integrations

- `git grep -n "AUDIT:" -- src` lists every outside-access point (26 tags at the time of this analysis).
- Searches that should return nothing in `src`, per `AUDITING.md`: `HttpClient` (only `UpdateService.cs`), `Socket`, `WebClient`, `DllImport`, `LibraryImport`, `Assembly.Load`, `Registry`, `Clipboard`.

---

*Integration audit: 2026-11-01*

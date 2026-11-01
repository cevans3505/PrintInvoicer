# Auditing PrintInvoicer

A guide for anyone checking this code before trusting it. It lists every place the program reaches outside itself: the
network, other programs, and files. Those are the places where software can do harm, so they are the places worth
checking first.

This guide and the code comments describe what the code is **meant** to do. Don't take them on trust: they are a map,
not proof. Read the code they point to.

## Start here

Every line that touches the network, starts another program, prints, or writes, reads or deletes a file is marked with
a comment starting `AUDIT:`. To list them all:

```powershell
git grep -n "AUDIT:" -- src
```

or search the `src` folder for `AUDIT:` in any editor. There are 26 tags, and the tables below summarize them. If you find
code that does any of these things **without** an `AUDIT:` tag, that's a gap in this guide; please report it.

## Network

| What | Where | When |
| --- | --- | --- |
| `GET https://api.github.com/repos/cevans3505/PrintInvoicer/releases/latest` | `src/PrintInvoicer.App/UpdateService.cs`, `FetchLatestAsync` | At startup, at most once a day, unless turned off in Settings. Also when the user clicks **Check now**. |

That is the only network request in the program. It sends no information about the user or their data; only the
standard request headers (a `User-Agent` of `PrintInvoicer/1`). From the reply it reads only the release's tag and page
address. There is no telemetry, analytics or crash upload. Error details are saved locally and never sent (see below).

## Programs started

All of these use Windows' "open this with its default program" (`Process.Start` with `UseShellExecute`). Each one happens
only in response to a click.

| What opens | Where | Triggered by |
| --- | --- | --- |
| A PDF just created, in the default PDF viewer | `JobEditorViewModel.OpenPdf`, `MainViewModel.GenerateInvoices` | Preview, Finalize, Detail PDF, Open internal |
| A GitHub release page in the browser | `UpdateService.Download` (refuses any address outside this repository) | **Download** on the update bar |
| The GitHub "new issue" page, and Explorer showing the error file | `ErrorReport.Show` | **Yes** in the error dialog |
| A folder in Explorer | `SettingsViewModel.OpenFolder`, `OpenBackupFolder` | The **Open** buttons in Settings |
| PrintInvoicer itself, to restart | `SettingsViewModel.RestoreBackup` | After the user confirms **Restore a backup** |
| The Windows print dialog, then the chosen printer | `InvoicePrinter.Print` | **Print invoices** / **Print internal** |

## Files written or deleted

| Location | What | Where in the code |
| --- | --- | --- |
| `Documents\PrintInvoicer\printinvoicer.db` | All jobs, materials, printers and settings (SQLite) | `Database` (`Open`, used by every read and write) |
| `Documents\PrintInvoicer\Backups\` (or the folder set in Settings) | Daily and manual copies of the database; oldest daily copies deleted to keep 14; a leftover half-written `.partial` copy deleted before retrying | `DatabaseBackup` |
| `Documents\PrintInvoicer\Invoices\` (or the folder set in Settings) | Invoice and detail-sheet PDFs | `InvoicePdf.Write` |
| `%TEMP%\PrintInvoicer previews\` | Preview PDFs | `InvoicePdf.Write` |
| `%LOCALAPPDATA%\PrintInvoicer\update-check.json` | When GitHub was last asked, and the newest release it reported | `UpdateService.SaveState` |
| `%LOCALAPPDATA%\PrintInvoicer\window-layout.json` | Window positions and sizes | `WindowLayout.Save` |
| `%LOCALAPPDATA%\PrintInvoicer\Errors\` | Details of unexpected errors (app version, Windows version, time, the error) | `ErrorReport.Save` |

**Restoring a backup** (Settings, after a confirmation) replaces the database's contents with the chosen backup. The
current data is backed up first, and a damaged backup is refused.

Nothing is written anywhere else. The program never modifies the sliced files it reads.

## Files read

| What | Where in the code |
| --- | --- |
| Sliced files the user chooses or drags in (`.gcode.3mf`, `.gcode`, `.goo`), opened read-only | `BambuFileReader`, `GooFileReader` |
| The original sliced file again, read-only, to get a sharper plate picture for an invoice | `BambuFileReader.ReadPlateImage` |
| Its own database, backups (when restoring), and the small JSON files above | `Database`, `DatabaseBackup`, `UpdateService`, `WindowLayout` |

The file readers treat every file as untrusted. They never run anything from a file. The XML parser has DTDs and
external references switched off, every size is capped, and every number is range-checked (limits in
`src/PrintInvoicer.Core/Slicing/SliceLimits.cs`). Hundreds of tests in `tests/PrintInvoicer.Tests/*RobustnessTests.cs`
feed the readers damaged files.

## What it never does

A search of this project's code finds none of the following:

- **Registry access.**
- **Native code or P/Invoke in this project's code.** The packages below do ship native libraries: the SQLite engine
  (`e_sqlite3.dll`) and QuestPDF's PDF renderer (`QuestPdfSkia.dll`, `qpdf.dll`).
- **Loading code at run time.** No plugins, scripts or downloaded code.
- **Elevation.** It never asks for administrator rights.
- **Self-updating.** Nothing is downloaded or installed automatically; the update bar only links to the release page.
- **Credentials.** No passwords, accounts, tokens or keys.
- **Clipboard access.**
- **Background services or startup entries.**

To check for yourself, search `src` for: `Registry`, `DllImport`, `LibraryImport`, `Assembly.Load`, `HttpClient`,
`WebClient`, `Socket`, `Process.Start`, `File.Delete`, `Clipboard`.

## Third-party packages in the program

| Package | Version | Used for |
| --- | --- | --- |
| Microsoft.Data.Sqlite | 10.0.14 | The SQLite database (includes the native SQLite engine via SQLitePCLRaw) |
| QuestPDF | 2026.9.1 | Laying out and rendering the PDF invoices (Community license; includes native Skia and qpdf libraries) |
| CommunityToolkit.Mvvm | 8.4.2 | Wiring the windows to their logic (source generators; no I/O of its own) |

Plus .NET 10 and WPF from Microsoft. The test projects use xUnit; tests are not part of the released program.

## Building and releasing

These run on a developer's PC or on GitHub, never on users' machines:

- **`publish.ps1`** runs the tests and builds the program. It then installs it to `%LOCALAPPDATA%\Programs\PrintInvoicer`
  with a Start menu shortcut, and refreshes Windows' icon cache (`ie4uinit.exe -show`).
- **`.github/workflows/ci.yml`** builds and tests every change on a GitHub Windows machine, with a read-only token.
- **`.github/workflows/release.yml`**, on a version tag, builds the program the same way and attaches the zip to a
  **draft** release. Only that last step has write access, and a person publishes the release by hand.

Releases are not code-signed. To be sure a download matches this source, build it yourself from the tag with
`publish.ps1` or the `dotnet publish` command in the release workflow.

## A suggested order for a review

1. Search for `AUDIT:` and read each tagged line and its method.
2. Read `UpdateService.cs` (the only network code) and `ErrorReport.cs`.
3. Read the file readers in `src/PrintInvoicer.Core/Slicing/`, the code that handles files from outside.
4. Skim `Database.cs` and `DatabaseBackup.cs`: all SQL uses parameters for values.
5. The rest is pricing (`Pricing/`), data classes (`Models/`), windows (`*.xaml`, `ViewModels/`) and PDF layout
   (`Pdf/`). These do calculation and display, and touch nothing outside the program.

Found something? Please [open an issue](https://github.com/cevans3505/PrintInvoicer/issues).

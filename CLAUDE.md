# PrintInvoicer: notes for AI coding agents

Windows desktop app (.NET 10, WPF, SQLite, QuestPDF) that prices 3D print jobs (FDM and resin) and makes PDF invoices.
Public, MIT, shared as-is. User docs: `README.md`. Security map: `AUDITING.md`.

## Layout

- `src/PrintInvoicer.Core` (net10.0, no UI, runs on Linux): `Models/`, `Pricing/` (PricingCalculator, PriceBook,
  PriceSnapshot), `Slicing/` (Bambu and .goo readers), `Data/` (Database: hand-written SQL; DatabaseBackup).
- `src/PrintInvoicer.App` (net10.0-windows, WPF): `ViewModels/`, `*.xaml`, `Pdf/` (InvoicePdf), UpdateService,
  ErrorReport, WindowLayout.
- `tests/PrintInvoicer.Tests` (Core, runs anywhere) and `tests/PrintInvoicer.UiTests` (WPF, Windows only, offscreen).
- Big files: `Data/Database.cs`, `Pdf/InvoicePdf.cs`, `ViewModels/JobEditorViewModel.cs`. Grep for the member you
  need instead of reading them whole.

## Commands

- Test everything: `dotnet test PrintInvoicer.sln` (Linux: `dotnet test tests/PrintInvoicer.Tests` only).
- Install locally: `.\publish.ps1` with the app closed (runs all tests first).
- Release: bump `<Version>` in `src/PrintInvoicer.App/PrintInvoicer.App.csproj` (and the heading of
  `packaging/Read me.txt` on a major/minor bump), push to `main`, wait for CI, push tag `v<version>`; the Release
  workflow makes a draft; check the zip, write notes, publish. Steps in README, "Making a release".

## Rules

- **Invoices already finalized must never change.** Pricing reads frozen prices (`PriceSnapshot`) for finalized jobs.
  Any change to how totals are computed goes behind a new `PricingRules` version, keeping old behavior for older
  versions. Check old totals still match.
- **Schema changes** only through `AddColumnIfMissing` / `CREATE TABLE IF NOT EXISTS` in `Database.EnsureCreated`,
  with defaults that keep existing data meaning the same.
- **Outside access:** any new line that touches the network, starts a program, prints, or reads/writes/deletes a
  file gets an `// AUDIT:` comment and a row in `AUDITING.md`.
- **Comments:** purpose comments on classes, methods and non-obvious blocks; not on plain lines.
- **UI tests** run offscreen only: never show, activate or focus a window on screen, never trigger a dialog. Use
  `WpfHost` in the UI tests.
- **No personal data** in code, tests or docs: sample names like "Harbor Dive Shop", "Sample Printing Co.".
- **Writing style** in docs, comments and messages: plain English, no em dashes.
- File readers treat every file as untrusted: range-check values, cap sizes, throw `InvalidDataException` with a
  user-readable message, never anything else.

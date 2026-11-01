---
last_mapped_commit: 2df24f47743d36dde2022025fa255f942c9bc7ae
last_mapped_at: 2026-11-01
---
# Codebase Concerns

**Analysis Date:** 2026-11-01

Scope: full repository, read against the hard rules in `CLAUDE.md` (finalized invoices never change, pricing behind
`PricingRules` versions, schema changes only through `AddColumnIfMissing`, untrusted file readers). Items are ordered
roughly by how much they matter to the user's money and data, not by how hard they are to fix.

## Tech Debt

**Retroactive price freeze runs on every start:**
- Issue: `Database` constructor calls `FreezeFinalizedJobs`, which gives any finalized job with no snapshot today's
  prices. This covers invoices finalized before snapshots existed. Those invoices were never frozen under the prices
  they were sent with, so whatever prices are current at the first start after upgrade become their record.
- Files: `src/PrintInvoicer.Core/Data/Database.cs` (`FreezeFinalizedJobs`, lines ~36-56, called from the constructor)
- Impact: If a legacy invoice was sent under older rates, its frozen total can silently differ from what the customer
  received. The method also runs a query on every open, though it does nothing once all finalized jobs have snapshots.
- Fix approach: Leave the behavior alone for invoices already frozen (they must not change). Record in the database
  (for example a `settings` key) that the one-time freeze has run, so the check is explicit and auditable. For any
  future rule change, follow the `PricingRules` version path instead of editing this method. Add a test that a
  legacy finalized job gets a snapshot exactly once.

**Revert to draft and re-finalize reprices the invoice:**
- Issue: `Job.Revert` (`src/PrintInvoicer.Core/Models/Job.cs`, ~lines 75-81) clears `Finalized`, the snapshot and the
  frozen total. `JobEditorViewModel` (`src/PrintInvoicer.App/ViewModels/JobEditorViewModel.cs`, ~line 469) asks the
  user first, then reprices at today's rates. This is intended, but the old PDF and the new total can diverge, and
  nothing in the database keeps the earlier invoice record.
- Files: `src/PrintInvoicer.Core/Models/Job.cs`, `src/PrintInvoicer.App/ViewModels/JobEditorViewModel.cs`
- Impact: A customer who received an invoice may be sent a different total later. The confirmation prompt is the only
  guard.
- Fix approach: Keep the confirmation. Consider keeping the last frozen snapshot and total in a history table (new
  table via `CREATE TABLE IF NOT EXISTS`) so a reverted invoice's previous state can be seen. Make the prompt state the
  old and new totals.

**Large files with mixed responsibilities:**
- Issue: `Database.cs` (889 lines), `Pdf/InvoicePdf.cs` (652), `ViewModels/JobEditorViewModel.cs` (534),
  `ViewModels/MainViewModel.cs` (510), `Slicing/BambuFileReader.cs` (464) and `ViewModels/PartViewModel.cs` (375).
- Files: as listed.
- Impact: Hard to review, and changes in one area touch unrelated code. `CLAUDE.md` already advises grepping for members.
- Fix approach: Split `Database` by aggregate (jobs, filaments and resins, printers, settings, extra costs) into partial
  classes or repository classes. Do this only with the existing tests (`DatabaseTests.cs`, `PriceSnapshotTests.cs`)
  green before and after.

**Schema versioning is implicit:**
- Issue: There is no schema version number. Upgrades are a list of `AddColumnIfMissing` calls and `CREATE TABLE IF
  NOT EXISTS` in `EnsureCreated` (`Database.cs`, ~lines 81-151). Older builds would open a newer database without
  complaint and silently ignore the new columns.
- Files: `src/PrintInvoicer.Core/Data/Database.cs`
- Impact: Running an older release against a database written by a newer one can drop features that depend on the new
  columns (for example snapshot handling) without any warning. Backups from a newer version restored into an older
  build have the same problem.
- Fix approach: Store a `schema_version` in `settings`, bump it when columns are added, and refuse to write to a
  database with a higher version than the build understands (show a message, do not corrupt the data).

**String-typed money columns:**
- Issue: Prices, discount and totals are stored as `TEXT` (for example `discount_percent TEXT NOT NULL DEFAULT '0'`,
  `finalized_total TEXT`) and parsed with `Dec(...)` / `Str(...)`. `Database.cs` catches `FormatException` and
  `OverflowException` around parsing (~line 240).
- Files: `src/PrintInvoicer.Core/Data/Database.cs`
- Impact: Works, but a damaged value is handled by a catch that may hide the problem. Anything that sorts or sums these
  values in SQL will sort text, not numbers.
- Fix approach: Leave existing data as is. For new columns, consider integer minor units. Do not change existing
  columns (frozen data depends on them).

**Explorer and Process.Start arguments built by string concatenation:**
- Issue: `SettingsViewModel.OpenFolder` (`src/PrintInvoicer.App/ViewModels/SettingsViewModel.cs`, ~line 186) passes
  `"\"{path}\""` and `ErrorReport.Show` (`src/PrintInvoicer.App/ErrorReport.cs`, ~line 60) passes
  `/select,"{path}"` to `explorer.exe`. A folder or path that contains a double quote breaks the argument list.
- Files: as listed.
- Impact: Low. The paths come from the user's own settings or the app's own folders, and the calls only open Explorer.
  It still breaks for odd folder names.
- Fix approach: Pass `ArgumentList` on `ProcessStartInfo` (not `UseShellExecute` with a single string), or reject
  quotes in the Settings folder field. Keep the `AUDIT:` tags and update `AUDITING.md` if the call changes.

**Date handling uses local `DateTime.Today` in many places:**
- Issue: Invoice dates, paid and printed dates, created dates and the invoice-number year all use `DateTime.Today`
  (for example `Database.cs` ~line 159, `Job.cs` line 14, `MainViewModel.cs` ~lines 221-223, `JobEditorViewModel.cs`
  ~line 458). A job open across midnight, or a clock change, can give the wrong date or the wrong invoice-number year.
- Files: as listed.
- Impact: Invoice numbers and dates can disagree with the day the work happened. Low frequency, but it is on invoices.
- Fix approach: Capture "today" once per user action and pass it down (the code already does this in some paths). Add
  tests around the year boundary for `PeekNextInvoiceNumber`.

## Known Bugs

No confirmed bugs were found by reading the code. The items below are behaviors that look wrong or fragile and should
be checked by running the app:

**Stale window after a backup restore:**
- Symptoms: After **Restore a backup**, open windows could still show data from before the restore until the app
  restarts.
- Files: `src/PrintInvoicer.App/ViewModels/SettingsViewModel.cs` (`RestoreBackup`, ~line 109-118),
  `src/PrintInvoicer.Core/Data/DatabaseBackup.cs` (`Restore`).
- Trigger: Restore a backup while other windows are open.
- Workaround: The restore prompts for a restart (`Process.Start` of the same executable), which clears this.
  Verify the restart happens before any further save, so old in-memory data is not written over the restored database.

**Backup folder and backup run at startup can overlap:**
- Symptoms: Two backup runs on the same day could contend for the same `*.partial` file.
- Files: `src/PrintInvoicer.Core/Data/DatabaseBackup.cs` (`Copy`, which deletes and reuses `target + ".partial"`),
  `src/PrintInvoicer.App/App.xaml.cs` (`BackUpTodayAsync` on `Task.Run`).
- Trigger: A manual backup clicked while the startup daily backup is still running, if both use the same target name.
- Workaround: None needed in normal use. Confirm the manual backup name differs from the daily name.

## Security Considerations

**Network access is limited and well marked:**
- Risk: Only one outbound request exists (`UpdateService.FetchLatestAsync`, `api.github.com`). It is marked `AUDIT:`.
- Files: `src/PrintInvoicer.App/UpdateService.cs`
- Current mitigation: Download only opens URLs that start with this repository's GitHub path. The response is read for two
  fields. The request has a 10-second timeout.
- Recommendations: The response stream is not size-capped before deserialization. A capped read (for example 256 KB)
  costs little and matches the "treat every input as untrusted" rule used for file readers.

**Releases are not code-signed:**
- Risk: Users cannot verify the download came from the maintainer, and Windows SmartScreen will warn on first run.
- Files: `.github/workflows/release.yml`, `AUDITING.md` (Building and releasing).
- Current mitigation: The release is a draft that a person publishes. `AUDITING.md` tells reviewers to build from the tag.
- Recommendations: Add a SHA-256 checksum file to each release, and state it in the release notes. Consider code
  signing if a certificate becomes available.

**File readers are the main untrusted-input surface:**
- Risk: Sliced files (`.gcode.3mf`, `.gcode`, `.goo`) are parsed from outside the program.
- Files: `src/PrintInvoicer.Core/Slicing/BambuFileReader.cs`, `src/PrintInvoicer.Core/Slicing/GooFileReader.cs`,
  `src/PrintInvoicer.Core/Slicing/SliceLimits.cs`.
- Current mitigation: Entry size caps (`MaxMetadataBytes`, `MaxImageBytes`), header character cap
  (`MaxGcodeHeaderChars`), XML parser with DTD and external references off, range checks on every value, and robustness
  tests (`tests/PrintInvoicer.Tests/*RobustnessTests.cs`). The readers raise `InvalidDataException`.
- Recommendations: Check that every new reader path throws only `InvalidDataException` for bad input. Check that the
  ZIP reader caps the total uncompressed size across all entries, not only per entry (not verified by this review).

**Restore accepts any database that has the three main tables:**
- Risk: `DatabaseBackup.LooksLikePrintInvoicer` checks only that `jobs`, `settings` and `plates` exist. A file that is
  a valid SQLite database with those names passes the check and replaces the live data.
- Files: `src/PrintInvoicer.Core/Data/DatabaseBackup.cs`.
- Current mitigation: `PRAGMA integrity_check`, and a backup of the current data before the copy.
- Recommendations: Also check the schema version once it exists (see Tech Debt), and the column list for the tables
  the app reads. The restore is user-initiated, so this is a low risk.

**Output file and folder names come from user data:**
- Risk: Invoice file names are built from customer and invoice fields. A name with characters Windows rejects, or a
  reserved name such as `CON`, could fail or write somewhere unexpected.
- Files: `src/PrintInvoicer.App/Pdf/InvoicePdf.cs` (`Write`), `src/PrintInvoicer.App/ViewModels/MainViewModel.cs`
  (`GenerateInvoices`, ~line 407-424).
- Current mitigation: Not verified by this review. Check that file names are sanitized (`Path.GetInvalidFileNameChars`).
- Recommendations: Sanitize and length-limit every name derived from user input, with a test that includes `..`,
  `\`, `:` and reserved device names.

## Performance Bottlenecks

**Job list loads every job on each reload:**
- Problem: `MainViewModel` calls `_db.GetJobs()` in several places (for example ~line 161 and ~line 238). `GetJobs`
  (`Database.cs`, ~line 638) returns all jobs with their snapshot strings.
- Files: `src/PrintInvoicer.App/ViewModels/MainViewModel.cs`, `src/PrintInvoicer.Core/Data/Database.cs`
- Cause: No paging or caching. Snapshots are stored as JSON text per finalized job, so the data grows with history.
- Improvement path: Not measured. Time the list with a few hundred jobs before changing anything. If slow, load the
  snapshot column only when an invoice is opened, and page the list.

**Repeated pricing on each change:**
- Problem: `PricingCalculator.Calculate` runs on the whole job when any field changes in `JobEditorViewModel` and
  `PartViewModel`.
- Files: `src/PrintInvoicer.Core/Pricing/PricingCalculator.cs`, `src/PrintInvoicer.App/ViewModels/JobEditorViewModel.cs`
- Cause: Calculation is cheap for typical jobs, so this is only a concern for very large part lists.
- Improvement path: Not measured. Only address this if a real job is slow.

## Fragile Areas

**Pricing rules and frozen snapshots:**
- Files: `src/PrintInvoicer.Core/Pricing/PriceBook.cs` (`PricingRules`, `RulesVersion`),
  `src/PrintInvoicer.Core/Pricing/PriceSnapshot.cs`, `src/PrintInvoicer.Core/Pricing/PricingCalculator.cs` (`v2` branch, ~line 122).
- Why fragile: `PriceBook.RulesVersion` defaults to `PricingRules.Current`. Any edit to calculation code that is not
  behind a version check changes every invoice that is re-priced, including drafts reverted from finalized. Snapshots
  store `RulesVersion` in full. Snapshots written before it was recorded are read back as version 1 (`PriceSnapshot.cs`).
- Safe modification: Add a new `PricingRules` version, keep the old branch, bump `Current`, and add a test that a
  version 1 snapshot still yields its recorded total (`PricingRulesTests.cs`, `PriceSnapshotTests.cs`).
- Test coverage: Good for pricing, but the test suite does not cover `FreezeFinalizedJobs` on a database that was
  created by an old build (no test found for this path).

**Invoice PDFs are re-rendered from current code:**
- Files: `src/PrintInvoicer.App/Pdf/InvoicePdf.cs`, QuestPDF (package version in `src/PrintInvoicer.App/PrintInvoicer.App.csproj`).
- Why fragile: A finalized invoice's amounts are frozen, but its layout is not. Changing the layout code or upgrading
  QuestPDF changes how an old invoice looks when it is printed again.
- Safe modification: Treat layout changes as visible changes. Keep totals and line amounts reading only from the snapshot,
  and compare a rendered old invoice before and after a QuestPDF upgrade.
- Test coverage: `InvoicePdfTests` do not exist under that name; `InvoicePdf` is referenced by one test file only.

**Restarting the app as part of restore:**
- Files: `src/PrintInvoicer.App/ViewModels/SettingsViewModel.cs` (~line 118).
- Why fragile: The restart runs `Environment.ProcessPath` directly. In a debug run or a moved install, the path may not
  be the expected executable.
- Safe modification: Check the behavior after a restore from both an installed copy and a `dotnet run` session.

## Scaling Limits

**Local SQLite file only:**
- Current capacity: One user, one PC, one `printinvoicer.db` in `Documents\PrintInvoicer\`.
- Limit: No sharing between PCs, no multi-user access. Two running copies of the app on one database are not guarded.
- Scaling path: Not planned. If two copies must be supported, add a lock file or single-instance check (not present).

**Backups kept at 14 daily copies:**
- Current capacity: Oldest daily copies are deleted beyond 14 (`DatabaseBackup`, `DailyName` pattern).
- Limit: Manual backups are not pruned. A long-running install can fill the backup folder with manual copies.
- Scaling path: Add a count or size limit for manual backups, and show their total size in Settings.

## Dependencies at Risk

**QuestPDF Community license:**
- Risk: QuestPDF's Community license is free for companies below a revenue threshold. This app is public and MIT, and
  the user base is small shops. Users above the threshold would need a commercial license to use the PDF layout.
- Impact: Legal exposure for the user and any downstream user, not a technical failure.
- Migration plan: State the license condition in `README.md` and `AUDITING.md`, which list QuestPDF as "Community
  license" already. Consider a PDF library with a permissive license if the layout code can be ported.

**Microsoft.Data.Sqlite and SQLitePCLRaw native engine:**
- Risk: Native `e_sqlite3.dll` ships with the app. Updating it changes database behavior.
- Impact: Low for normal use. Watch for SQLite changes around `PRAGMA integrity_check` and `BackupDatabase`, both used in
  backups.
- Migration plan: Keep the package pinned in `PrintInvoicer.Core.csproj`, and run `DatabaseBackup` tests on each update.

## Missing Critical Features

**No crash recovery for an in-progress job:**
- Problem: Unsaved edits in `JobEditorWindow` are lost if the app crashes before **Save**. The error handler saves a
  report to `%LOCALAPPDATA%\PrintInvoicer\Errors\` but not the job.
- Blocks: Recovering a half-entered job after a crash.

**No way to check the database for corruption from the UI:**
- Problem: `PRAGMA integrity_check` runs only on a backup that is being restored. The live database is never checked.
- Blocks: Early warning of a damaged live database before a backup copies it.

## Test Coverage Gaps

**Windows-only UI and app code with no direct tests:**
- What's not tested: `MainViewModel`, `PartViewModel`, `InvoicePrinter`, `PlateImages`, `ErrorReport`, `SeedData` have
  no test file that references them. `UpdateService`, `WindowLayout`, `InvoicePdf`, `JobEditorViewModel` and
  `SettingsViewModel` are referenced by one test file each.
- Files: `src/PrintInvoicer.App/ViewModels/MainViewModel.cs`, `src/PrintInvoicer.App/ViewModels/PartViewModel.cs`,
  `src/PrintInvoicer.App/Pdf/InvoicePrinter.cs`, `src/PrintInvoicer.App/Pdf/PlateImages.cs`,
  `src/PrintInvoicer.App/ErrorReport.cs`, `src/PrintInvoicer.Core/Data/SeedData.cs`.
- Risk: Changes to invoice generation, finalize and print flow can break without a test failing. The App project runs
  only on Windows, so Linux CI cannot exercise it.
- Priority: High for `MainViewModel` (finalize and generate paths) and `InvoicePdf` (what the customer sees).
  Medium for `PlateImages` and `ErrorReport`.

**Legacy-database upgrade path:**
- What's not tested: A database created by an older release, opened by the current build, with finalized jobs that
  have no snapshot. This is the path `FreezeFinalizedJobs` takes on real user data.
- Files: `src/PrintInvoicer.Core/Data/Database.cs`, `tests/PrintInvoicer.Tests/PriceSnapshotTests.cs`.
- Risk: A regression here changes the totals of invoices already sent.
- Priority: High. Add a fixture database built from the schema before snapshots.

**Schema upgrade across versions:**
- What's not tested: Opening a database written by a newer build with an older build (see Tech Debt).
- Risk: Silent loss of new fields on save.
- Priority: Medium.

---

*Concerns audit: 2026-11-01*

# PrintInvoicer

A Windows desktop app for invoicing 3D printing jobs on FDM and resin printers. It reads sliced Bambu Studio and ELEGOO
resin files, prices material, machine time, electricity and your own extra costs per plate, adds slicing, modeling and
post-processing fees, and produces a PDF invoice plus an internal detail sheet.

> [!IMPORTANT]
> **Personal project, shared as-is.** I built PrintInvoicer for my own use, invoicing 3D printing work for a local
> business. I'm sharing it publicly because I believe in open source and in sharing code for the benefit of everyone.
> It is not a commercial product and is not actively maintained. I fix what I need, and may add things if enough people
> ask, but there is no support commitment. It is provided under the [MIT license](LICENSE), "as is", without warranty
> of any kind.

> [!WARNING]
> **AI use disclosure.** This application was written primarily by an AI model, Anthropic's Claude Opus 5.5, working
> through Claude Code under my direction. I chose what to build, made the design decisions, and tested it on my own
> jobs. I have years of coding experience, but I did not write most of this code by hand, and I have **not** reviewed
> it line by line. My review before each release is high level: reading through the changes to check that they make
> sense and that nothing looks out of place. That is not a full code review, and it is not a security audit, so please
> don't treat it as one.
>
> **Checks that have been done:**
> - Several hundred automated tests (pricing, file reading, storage, backups, and the app's windows and invoices), run
>   on a Windows machine on GitHub for every change, and again before each release.
> - Tests that feed the file readers hundreds of damaged, cut-off and oddly built files, to make sure a bad file gets
>   a clear error instead of a crash or a wrong number on an invoice.
> - A targeted, AI-performed bug hunt of the pricing, invoice and storage code. Each bug it found was proven with a
>   failing test before being fixed.
> - Real use: the app's totals were checked against my original costing spreadsheet and my own real jobs.
> - A check that the code and releases contain no personal data, and a review of each release zip before publishing.
>
> **Checks that have not been done:**
> - No independent human code review or professional security audit.
> - Not tested on a wide range of PCs, Windows versions or setups. FDM file reading has only been tested with files
>   from Bambu Studio for one printer, and resin file reading with a handful of ELEGOO `.goo` files.
> - Not checked for tax, accounting or legal correctness. It does not handle sales tax or VAT, and amounts are in US
>   dollars.
> - The program is not code-signed, so Windows will warn that it comes from an unknown publisher.
>
> **What it does with your data:** everything stays on your PC, in `Documents\PrintInvoicer`. The only network access
> is an optional once-a-day check of this repository's releases for a newer version, which can be turned off in
> Settings. There is no tracking or telemetry.
>
> **Use it at your own risk.** Review the code yourself before relying on it, and check its invoices before you send
> them. If you find a problem, please [open an issue](https://github.com/cevans3505/PrintInvoicer/issues).

## Install or update

From Windows PowerShell, with PrintInvoicer closed:

```powershell
.\publish.ps1
```

This runs the tests, builds a self-contained `PrintInvoicer.exe` (no .NET install needed to run it), copies it to
`%LOCALAPPDATA%\Programs\PrintInvoicer` and adds a **PrintInvoicer** Start menu shortcut.

Building needs the .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`).

To set it up on someone else's PC, send them the [latest release](https://github.com/cevans3505/PrintInvoicer/releases/latest).
The zip holds `PrintInvoicer.exe`, which runs from anywhere and keeps its own data in their `Documents\PrintInvoicer`.

### Updates

When the app starts it asks GitHub, at most once a day, whether a newer release exists, and shows a bar at the top of
the window if so. **Download** opens the release page; nothing is downloaded or installed automatically. **Later** hides
the bar until the next start. Settings has the on/off switch and a **Check now** button. The last answer is kept in
`%LOCALAPPDATA%\PrintInvoicer\update-check.json`.

Only published releases are seen, so a new version reaches other installs once its release is published. Releases are
built on GitHub, not on a PC: pushing a `v<version>` tag builds the zip and creates a **draft** release, which stays
invisible to the update check until you publish it. The steps are under [Making a release](#making-a-release).
A fork that publishes its own releases should change `UpdateService.Repository`.

## Setting up

1. **Settings**: your name and contact line for the invoice header, default customer, payment terms, electricity price,
   and the labor and fee rates.
2. **Printers**: one row per printer with its machine rate, power draw, waste and markup. Adding the first sliced file for
   a kind of printer sets one up with starting rates, named from the file, so check them here.
3. **Materials**: filament spools for FDM printers, resin bottles for resin printers. Each list appears once a printer of
   its kind exists. Resin bottles are priced by g or mL; density (g/mL) converts the slicer's volume for bottles sold by weight.
4. **Extra costs** (on the Printers tab): anything else you charge, each with a cost and what it is counted against: per
   job, per print, per print hour, per mL of resin or per g of filament, for all printers, FDM only or resin only.

## Using it

1. **Jobs > New job**, then drag in sliced files:
   - Bambu Studio `.gcode.3mf` (File > Export > Export plate sliced file). Files with several sliced plates add every plate.
   - ELEGOO `.goo` resin files (Saturn and Mars printers, from ELEGOO SatelLite, CHITUBOX or VoxelDance Tango): print time,
     resin volume, layers and the preview are read from the file.
   - Anything else (CHITUBOX `.ctb`, Anycubic, Lychee, other slicers): **Add plate by hand**, pick the printer, and type
     the print time and grams or mL.
2. Per plate, set how many times it is printed and how many parts are on it, and pick the
   printer if you have more than one of that kind. Per AMS slot or resin, check the material pick and tick
   **Customer supplied** when the customer provided it. Typing grams, mL or print time overrides the slicer value (highlighted,
   with a reset link). Resin files do not record how many parts are on the plate, so a resin plate asks for the count
   and finalizing warns until it is set or confirmed.
3. Enter modeling hours and tick post-processing if they apply. Untick any extra cost that should be left off this job.
4. Jobs start as drafts. **Preview invoice** shows the invoice as it stands (marked as a preview, undated, saved to a temp
   folder). When the parts are ready to deliver, **Finalize invoice** dates it today and writes the PDF to send into the
   invoices folder; re-finalizing a job from an earlier day asks whether to move the date. Finalizing also freezes the
   prices: the invoice keeps the material prices, printer rates, extra costs and terms it was sent with, and the job is
   locked (only Paid, Printed and the invoice date can change). **Revert to draft** unlocks it and prices it at today's
   rates again.
   **Detail PDF** is the internal version: raw material prices, actual cost before rounding, and each printer's rates.
   From the job list, the same actions work on several selected jobs, and **Print invoices** offers to finalize drafts first.
5. Tick **Paid** in the job list once the shop pays.

The job list filters by search words (all must appear; "quote a phrase"; limit to one column with **in**), paid, draft or
finalized, printed, and a date range on the Date column. The footer totals the jobs left visible, split into paid,
invoiced but unpaid, and drafts, plus the total of any selected rows.

## How prices are calculated

Every plate is priced on its printer's rates.

| Item | Formula |
| --- | --- |
| Material, own filament | grams x cost per gram x (1 + waste) x (1 + markup), per print of the plate |
| Material, own resin | mL x cost per mL x (1 + waste) x (1 + markup), per print of the plate |
| Material, customer supplied | $0 (no waste or markup) |
| Machine time | print hours x the printer's machine rate |
| Electricity | print hours x the printer's watts / 1000 x $/kWh |
| Extra costs per print, hour, mL or g | amount used across every print x cost, added to the part (per-g and per-mL costs can be set to skip customer-supplied material) |
| Printed part line | sum of its plates' costs / pieces, rounded to the cent, x pieces; if that puts the line more than $0.50 off the cost (cheap parts in bulk), the per-piece price keeps 4 decimals |
| Slicing | flat fee per unique part, however many plates or files it is split across |
| 3D modeling | hours entered x hourly rate |
| Post-processing | flat fee per job when ticked |
| Discount | percent of everything above, per job (new jobs start with the default in Settings); the invoice shows full price, discount and total |
| Customer-supplied material | not billed; the invoice shows what it would have cost at your rates, and a "Your savings" box adds it to the discount |
| Extra costs per job | flat, when the job has a plate on a printer the cost applies to |

Starting rates for a new printer: FDM $2.50/hr, 200 W, 5% waste, 5% markup; resin $2.50/hr, 100 W, 10% waste, 5% markup.
Finalized invoices keep the pricing rules they were priced under, so a later change to these rules never re-totals an
invoice already sent.

Shop defaults: $0.20/kWh, $50/hr modeling, $10 slicing, $20 post-processing. All are editable.

Parts are grouped by the object name in the sliced file (resin files use the file name). To merge two parts, rename one to
match the other and save.

## Data

Everything lives in `Documents\PrintInvoicer\printinvoicer.db` (SQLite). PDFs go to `Documents\PrintInvoicer\Invoices`
unless changed in Settings.

**Backups:** each day the app first opens, it copies the database to `Documents\PrintInvoicer\Backups` and keeps the
newest 14 daily copies, plus any made with **Back up now** (never pruned). Set **Back up to** in Settings to a NAS or
cloud-synced folder so the copies survive a failed drive. **Restore a backup...** replaces all data with a chosen copy,
saving the current data first as a `before-restore` copy, and restarts the app. Copies are ordinary SQLite files.

Window sizes and positions are remembered per PC in `%LOCALAPPDATA%\PrintInvoicer\window-layout.json`. Delete it to go
back to the default layout.

The `.goo` reader follows ELEGOO's published [GOO format spec](https://github.com/elegooofficial/GOO).

## Development

```powershell
dotnet test PrintInvoicer.sln
dotnet run --project src\PrintInvoicer.App
```

- `src/PrintInvoicer.Core`: file reader (`Slicing/BambuFileReader.cs`), pricing (`Pricing/PricingCalculator.cs`), storage (`Data/Database.cs`)
- `src/PrintInvoicer.App`: WPF UI and QuestPDF invoices
- `tests/PrintInvoicer.Tests`: pricing, file readers, storage, backups; includes a regression test that matches the CS-001
  job from the original costing workbook
- `tests/PrintInvoicer.UiTests`: opens the real windows and renders every invoice kind on sample data, offscreen (nothing
  appears on screen), and fails on any WPF binding error. Screenshots land in its `bin\...\screens` folder for a look.
  `publish.ps1` runs both test projects before building.

Every push and pull request to `main` runs the **CI** workflow (`.github/workflows/ci.yml`) on a GitHub Windows runner:
it builds the solution in Release and runs both test projects, each in its own job. Test results (`.trx`) and the
screen-test screenshots are attached to the run as artifacts.

### Making a release

1. Bump `<Version>` in `src/PrintInvoicer.App/PrintInvoicer.App.csproj` (for example `3.5.0`), and the heading in
   `packaging/Read me.txt` if the major or minor number changed. Commit and push to `main`, and wait for CI to pass.
2. Tag that commit with `v` plus the exact version and push the tag:

   ```powershell
   git tag v3.5.0
   git push origin v3.5.0
   ```

3. The **Release** workflow (`.github/workflows/release.yml`) checks the tag matches `<Version>` (and stops with an
   error if not), runs the unit tests, builds `PrintInvoicer.exe` with the same `dotnet publish` settings as
   `publish.ps1`, zips it with `Read me.txt` into `PrintInvoicer-<version>.zip`, and creates a **draft** release for
   the tag with the zip attached. Follow it on the repository's **Actions** tab.
4. On the **Releases** page, open the draft, check the zip, replace the placeholder text with the release notes, and
   press **Publish release**. Only then do installed copies see the update.

If the tag was wrong, delete it (`git push origin --delete v3.5.0` and `git tag -d v3.5.0`), fix things, and tag again.
If a draft for the tag already exists, delete the draft first; the workflow will not overwrite it.

`publish.ps1` is still the way to build and install on your own PC; it no longer needs to be part of a release.

## License

MIT. See [LICENSE](LICENSE).

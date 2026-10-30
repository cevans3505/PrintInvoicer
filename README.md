# PrintInvoicer

A Windows desktop app for invoicing 3D printing jobs on FDM and resin printers. It reads sliced Bambu Studio and ELEGOO
resin files, prices material, machine time, electricity and your own extra costs per plate, adds slicing, modeling and
post-processing fees, and produces a PDF invoice plus an internal detail sheet.

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

Only published releases are seen, so a new version reaches other installs once it is released:
bump `<Version>` in `src/PrintInvoicer.App/PrintInvoicer.App.csproj`, run `publish.ps1`, zip `publish\PrintInvoicer.exe`
with `dist\Read me.txt`, and create a GitHub release tagged `v<version>` with the zip attached.
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
2. Per plate, set how many times it is printed and how many parts are on it (resin files do not record this), and pick the
   printer if you have more than one of that kind. Per AMS slot or resin, check the material pick and tick
   **Shop supplied** when the shop provided it. Typing grams, mL or print time overrides the slicer value (highlighted,
   with a reset link).
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
| Material, shop supplied | $0 (no waste or markup) |
| Machine time | print hours x the printer's machine rate |
| Electricity | print hours x the printer's watts / 1000 x $/kWh |
| Extra costs per print, hour, mL or g | amount used across every print x cost, added to the part |
| Printed part line | sum of its plates' costs / pieces, rounded to the cent, x pieces |
| Slicing | flat fee per unique part, however many plates or files it is split across |
| 3D modeling | hours entered x hourly rate |
| Post-processing | flat fee per job when ticked |
| Extra costs per job | flat, when the job has a plate on a printer the cost applies to |

Starting rates for a new printer: FDM $2.50/hr, 200 W, 5% waste, 5% markup; resin $2.50/hr, 100 W, 10% waste, 5% markup.
Shop defaults: $0.20/kWh, $50/hr modeling, $10 slicing, $20 post-processing. All are editable.

Parts are grouped by the object name in the sliced file (resin files use the file name). To merge two parts, rename one to
match the other and save.

## Data

Everything lives in `Documents\PrintInvoicer\printinvoicer.db` (SQLite). PDFs go to `Documents\PrintInvoicer\Invoices`
unless changed in Settings. Back up the `Documents\PrintInvoicer` folder.

Window sizes and positions are remembered per PC in `%LOCALAPPDATA%\PrintInvoicer\window-layout.json`. Delete it to go
back to the default layout.

The `.goo` reader follows ELEGOO's published [GOO format spec](https://github.com/elegooofficial/GOO).

## Development

```powershell
dotnet test
dotnet run --project src\PrintInvoicer.App
```

- `src/PrintInvoicer.Core`: file reader (`Slicing/BambuFileReader.cs`), pricing (`Pricing/PricingCalculator.cs`), storage (`Data/Database.cs`)
- `src/PrintInvoicer.App`: WPF UI and QuestPDF invoices
- `tests/PrintInvoicer.Tests`: includes a regression test that matches the CS-001 job from the original costing workbook

## License

MIT. See [LICENSE](LICENSE).

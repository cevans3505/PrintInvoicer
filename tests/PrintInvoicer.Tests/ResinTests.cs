using System.Buffers.Binary;
using System.Text;
using Microsoft.Data.Sqlite;
using PrintInvoicer.Core.Data;
using PrintInvoicer.Core.Models;
using PrintInvoicer.Core.Pricing;
using PrintInvoicer.Core.Slicing;

namespace PrintInvoicer.Tests;

public sealed class ResinTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pi-resin-{Guid.NewGuid():N}");

    public ResinTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private string WriteGoo(string name, byte[] header)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, header);
        return path;
    }

    [Fact]
    public void Reads_goo_header()
    {
        var file = GooFileReader.Read(WriteGoo("garklein.stl_0.05_1.2_2025_06_02_12_57_00.goo", TestFiles.GooHeader()));

        Assert.Equal(PrintTechnology.Resin, file.Technology);
        Assert.Equal("ELEGOO Saturn 4 Ultra", file.PrinterModel);
        var plate = Assert.Single(file.Plates);
        Assert.Equal(19860, plate.PredictedSeconds);
        Assert.Equal(3032, plate.LayerCount);
        Assert.Equal(0.05m, plate.LayerHeightMm);
        Assert.Equal("Creality Blue Fast resin", plate.Resin!.Profile);
        Assert.Equal(37.21m, plate.Resin.Ml);
        Assert.NotNull(plate.Thumbnail);
    }

    [Fact]
    public void Preview_background_becomes_transparent()
    {
        var png = GooFileReader.Read(WriteGoo("p.goo", TestFiles.GooHeader(background: 0x0000))).Plates[0].Thumbnail!;
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        // IHDR: 290 x 290, RGBA.
        Assert.Equal(290, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(6, png[25]);
    }

    [Fact]
    public void Rejects_files_that_are_not_goo()
    {
        var bad = TestFiles.GooHeader();
        bad[5] = 0x99;
        Assert.Throws<InvalidDataException>(() => GooFileReader.Read(WriteGoo("bad.goo", bad)));
        Assert.Throws<InvalidDataException>(() => GooFileReader.Read(WriteGoo("short.goo", new byte[100])));
        Assert.Throws<InvalidDataException>(() => SliceFileReader.Read(WriteGoo("model.ctb", new byte[100])));
    }

    [Theory]
    [InlineData("garklein.stl_0.05_1.2_2025_06_02_12_57_00.goo", "garklein")]
    [InlineData("Tiny_1_202501282210.goo", "Tiny_1")]
    [InlineData("cabecas dos desolators e o dripper_1_02h48m_78ml_uvtools0mm.goo", "cabecas dos desolators e o dripper_1_02h48m_78ml_uvtools0mm")]
    [InlineData("Dive Light Clip.goo", "Dive Light Clip")]
    public void Part_name_from_file(string file, string expected) => Assert.Equal(expected, GooFileReader.PartNameFromFile(file));

    private static Resin BlueResin() => new()
    {
        Id = 7, Vendor = "Creality", Name = "Blue Fast resin", BottlePrice = 33m, BottleSize = 1000m, BottleUnit = BottleUnit.Grams, Density = 1.10m,
    };

    [Fact]
    public void Resin_cost_per_ml_by_weight_or_volume()
    {
        Assert.Equal(0.0363m, BlueResin().CostPerMl);
        Assert.Equal(0.033m, new Resin { BottlePrice = 33m, BottleSize = 1000m, BottleUnit = BottleUnit.Milliliters }.CostPerMl);
        Assert.Equal(0m, new Resin { BottlePrice = 33m, BottleSize = 0 }.CostPerMl);
    }

    private static (Job Job, PriceBook Book) ResinJob(int copies = 2, int perPlate = 4, IEnumerable<ExtraCost>? extras = null)
    {
        var printer = Printer.NewDefault(PrintTechnology.Resin, "ELEGOO Saturn 4 Ultra");
        printer.Id = 3;
        var book = new PriceBook(new AppSettings(), resins: [BlueResin()], printers: [printer], extraCosts: extras);
        var job = new Job { Title = "Clips" };
        var file = GooFileReader.Parse("clip.goo", TestFiles.GooHeader(seconds: 7200, volumeMm3: 40000f));
        JobBuilder.AddFile(job, file, [], new Dictionary<string, long>(), book.Resins.Values, printer.Id);
        var plate = job.Parts[0].Plates[0];
        plate.Copies = copies;
        plate.PartsPerPlate = perPlate;
        plate.PartsCountConfirmed = true;   // as the job window does when the count is typed
        return (job, book);
    }

    [Fact]
    public void Prices_resin_plate_on_its_printer()
    {
        var (job, book) = ResinJob();
        var quote = PricingCalculator.Calculate(job, book);

        Assert.Empty(quote.Warnings);
        var pq = quote.Parts[0].Plates[0];
        Assert.Equal(7, pq.Resin!.Resin!.Id);             // matched "Blue Fast resin" in the profile name
        // 40 mL x $0.0363/mL x (1.10 x 1.05) x 2 prints
        Assert.Equal(40m * 0.0363m * 1.155m * 2, pq.Material);
        Assert.Equal(2m * 2.50m * 2, pq.Machine);          // 2 h x $2.50 x 2 prints
        Assert.Equal(2m * 0.1m * 0.20m * 2, pq.Electricity); // 100 W
        Assert.Equal(8, quote.Parts[0].Quantity);
        Assert.Equal(80m, quote.Parts[0].Ml);
    }

    [Fact]
    public void Resin_part_count_must_be_confirmed()
    {
        var (job, book) = ResinJob();
        var plate = job.Parts[0].Plates[0];
        plate.PartsCountConfirmed = false;                 // as added from the file
        Assert.Contains(PricingCalculator.Calculate(job, book).Warnings, w => w.Contains("how many parts are on the plate"));
        plate.PartsCountConfirmed = true;
        Assert.Empty(PricingCalculator.Calculate(job, book).Warnings);

        // Straight from a file it starts unconfirmed; typed-in and FDM plates do not.
        var fresh = new Job();
        JobBuilder.AddFile(fresh, GooFileReader.Parse("x.goo", TestFiles.GooHeader()), [], new Dictionary<string, long>());
        Assert.False(fresh.Parts[0].Plates[0].PartsCountConfirmed);
        Assert.True(JobBuilder.ManualPlate(new Printer { Technology = PrintTechnology.Resin }).PartsCountConfirmed);
    }

    [Fact]
    public void Shop_supplied_resin_is_free_and_missing_resin_warns()
    {
        var (job, book) = ResinJob();
        var resin = job.Parts[0].Plates[0].Resin!;
        resin.CustomerSupplied = true;
        Assert.Equal(0m, PricingCalculator.Calculate(job, book).Parts[0].Material);

        resin.CustomerSupplied = false;
        resin.ResinId = null;
        var quote = PricingCalculator.Calculate(job, book);
        Assert.Contains(quote.Warnings, w => w.Contains("no resin selected"));
    }

    [Fact]
    public void Measured_volume_overrides_slicer()
    {
        var (job, book) = ResinJob(copies: 1);
        job.Parts[0].Plates[0].Resin!.OverrideMl = 50m;
        Assert.Equal(50m * 0.0363m * 1.155m, PricingCalculator.Calculate(job, book).Parts[0].Plates[0].Material);
    }

    [Fact]
    public void Extra_costs_by_basis()
    {
        var extras = new[]
        {
            new ExtraCost { Id = 1, Name = "IPA and gloves", Cost = 0.75m, Basis = CostBasis.PerPrint, AppliesTo = PrintTechnology.Resin },
            new ExtraCost { Id = 2, Name = "FEP wear", Cost = 0.10m, Basis = CostBasis.PerPrintHour },
            new ExtraCost { Id = 3, Name = "Resin handling", Cost = 0.01m, Basis = CostBasis.PerMlResin },
            new ExtraCost { Id = 4, Name = "Nozzle wear", Cost = 0.002m, Basis = CostBasis.PerGramFilament },
            new ExtraCost { Id = 5, Name = "Wash and cure", Cost = 5m, Basis = CostBasis.PerJob, AppliesTo = PrintTechnology.Resin },
            new ExtraCost { Id = 6, Name = "FDM bed prep", Cost = 3m, Basis = CostBasis.PerJob, AppliesTo = PrintTechnology.Fdm },
            new ExtraCost { Id = 7, Name = "Retired", Cost = 9m, Basis = CostBasis.PerJob, Active = false },
        };
        var (job, book) = ResinJob(copies: 2, extras: extras);
        var quote = PricingCalculator.Calculate(job, book);

        var plate = quote.Parts[0].Plates[0];
        Assert.Equal(new[] { 1L, 2L, 3L }, plate.Extras.Select(e => e.Cost.Id));
        Assert.Equal(1.50m, plate.Extras[0].Amount);          // 2 prints
        Assert.Equal(0.40m, plate.Extras[1].Amount);          // 4 print hours
        Assert.Equal(0.80m, plate.Extras[2].Amount);          // 80 mL
        var jobExtra = Assert.Single(quote.JobExtras);        // resin only, inactive one skipped
        Assert.Equal("Wash and cure", jobExtra.Cost.Name);
        Assert.Equal(quote.PartsAmount + 10m + 5m, quote.Total);  // one $10 slicing fee

        // Switched off for this job only.
        job.ExcludedExtraCosts.Add(5);
        job.ExcludedExtraCosts.Add(1);
        var without = PricingCalculator.Calculate(job, book);
        Assert.Empty(without.JobExtras);
        Assert.Equal(new[] { 2L, 3L }, without.Parts[0].Plates[0].Extras.Select(e => e.Cost.Id));
    }

    [Fact]
    public void Missing_printer_falls_back_with_a_warning()
    {
        var (job, _) = ResinJob();
        var noResinPrinter = new PriceBook(new AppSettings(), resins: [BlueResin()], printers: [Printer.NewDefault(PrintTechnology.Fdm)]);
        var quote = PricingCalculator.Calculate(job, noResinPrinter);
        Assert.Contains(quote.Warnings, w => w.Contains("no Resin printer"));
        Assert.Equal(100m, quote.Parts[0].Plates[0].Printer.Watts);
    }

    [Fact]
    public void Chooses_printer_by_name_then_kind()
    {
        var file = GooFileReader.Parse("x.goo", TestFiles.GooHeader(printer: "ELEGOO Mars 5 Ultra"));
        var fdm = new Printer { Id = 1, Name = "Bambu Lab H2D", Technology = PrintTechnology.Fdm };
        var saturn = new Printer { Id = 2, Name = "ELEGOO Saturn 4 Ultra", Technology = PrintTechnology.Resin };
        var mars = new Printer { Id = 3, Name = "ELEGOO Mars 5 Ultra", Technology = PrintTechnology.Resin };

        Assert.Equal(3, JobBuilder.ChoosePrinter(file, [fdm, saturn, mars])!.Id);
        Assert.Equal(2, JobBuilder.ChoosePrinter(file, [fdm, saturn])!.Id);
        Assert.Null(JobBuilder.ChoosePrinter(file, [fdm]));
    }

    [Fact]
    public void Resin_matcher_prefers_remembered_choice()
    {
        var a = new Resin { Id = 1, Vendor = "Elegoo", Name = "ABS-Like 3.0" };
        var b = new Resin { Id = 2, Vendor = "Creality", Name = "Blue Fast resin" };
        Assert.Equal(2, ResinMatcher.Match("Creality Blue Fast resin", [a, b], new Dictionary<string, long>())!.Id);
        Assert.Equal(1, ResinMatcher.Match("Creality Blue Fast resin", [a, b],
            new Dictionary<string, long> { [ResinMatcher.MappingKey("Creality Blue Fast resin")] = 1 })!.Id);
        Assert.Null(ResinMatcher.Match("New Script", [a, b], new Dictionary<string, long>()));
        Assert.Equal(1, ResinMatcher.Match("New Script", [a], new Dictionary<string, long>())!.Id);
    }

    [Fact]
    public void Manual_plates_take_typed_values()
    {
        var printer = new Printer { Id = 4, Name = "Photon", Technology = PrintTechnology.Resin };
        var plate = JobBuilder.ManualPlate(printer);
        Assert.True(plate.IsManual);
        Assert.NotNull(plate.Resin);
        Assert.Empty(plate.Filaments);
        Assert.Single(JobBuilder.ManualPlate(new Printer { Technology = PrintTechnology.Fdm }).Filaments);
    }

    [Fact]
    public void Database_round_trips_printers_resins_extras_and_resin_plates()
    {
        var db = new Database(Path.Combine(_dir, "t.db"));
        Assert.Single(db.GetResins());                 // the example resin in a new database
        Assert.Empty(db.GetPrinters());                // nothing until a file is added or one is set up

        var printer = Printer.NewDefault(PrintTechnology.Resin, "ELEGOO Saturn 4 Ultra");
        db.SavePrinter(printer);
        var resin = BlueResin();
        resin.Id = 0;
        db.SaveResin(resin);
        var extra = new ExtraCost { Name = "IPA", Cost = 0.5m, Basis = CostBasis.PerPrint, AppliesTo = PrintTechnology.Resin };
        db.SaveExtraCost(extra);

        var job = new Job { Title = "Clips", InvoiceNumber = "2026-050" };
        JobBuilder.AddFile(job, GooFileReader.Parse("clip.goo", TestFiles.GooHeader()), [], db.GetMappings(), db.GetResins(), printer.Id);
        job.Parts[0].Plates[0].Resin!.OverrideMl = 41.5m;
        job.ExcludedExtraCosts.Add(extra.Id);
        db.SaveJob(job);

        var back = Assert.Single(db.GetJobs());
        var plate = back.Parts[0].Plates[0];
        Assert.Equal(PrintTechnology.Resin, plate.Technology);
        Assert.Equal(printer.Id, plate.PrinterId);
        Assert.Equal(3032, plate.LayerCount);
        Assert.Equal(0.05m, plate.LayerHeightMm);
        Assert.Equal("Creality Blue Fast resin", plate.Resin!.SlicerProfile);
        Assert.Equal(37.21m, plate.Resin.SlicedMl);
        Assert.Equal(41.5m, plate.Resin.OverrideMl);
        Assert.Equal(resin.Id, plate.Resin.ResinId);
        Assert.Equal(new[] { extra.Id }, back.ExcludedExtraCosts);
        Assert.False(plate.PartsCountConfirmed);
        back.Parts[0].Plates[0].PartsCountConfirmed = true;
        db.SaveJob(back);
        Assert.True(db.GetJobs().Single().Parts[0].Plates[0].PartsCountConfirmed);

        var book = db.LoadPriceBook();
        Assert.Equal(0.5m, Assert.Single(book.ExtraCosts).Cost);
        Assert.Equal("ELEGOO Saturn 4 Ultra", book.Printers[printer.Id].Name);

        // In use, so deleting deactivates instead.
        Assert.False(db.DeletePrinter(printer.Id));
        Assert.False(db.DeleteResin(resin.Id));
        Assert.False(db.GetPrinters().Single().Active);
    }

    [Fact]
    public void Existing_database_moves_rates_into_an_fdm_printer()
    {
        string path = Path.Combine(_dir, "old.db");
        using (var c = new SqliteConnection($"Data Source={path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            // The 2.x schema: rates were shop settings and plates had no printer.
            cmd.CommandText = """
                CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO settings VALUES ('MachineRatePerHour', '3.25'), ('PrinterWatts', '180'), ('WasteAllowance', '0.07'),
                    ('MaterialMarkup', '0.04'), ('BusinessName', 'Sample Printing Co.');
                CREATE TABLE jobs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    title TEXT NOT NULL, customer TEXT NOT NULL, invoice_number TEXT, created_date TEXT NOT NULL,
                    invoice_date TEXT, modeling_hours TEXT NOT NULL, post_processing INTEGER NOT NULL,
                    paid INTEGER NOT NULL, paid_date TEXT, notes TEXT NOT NULL);
                CREATE TABLE parts (id INTEGER PRIMARY KEY AUTOINCREMENT, job_id INTEGER NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
                    sort INTEGER NOT NULL, name TEXT NOT NULL, charge_slicing INTEGER NOT NULL);
                CREATE TABLE plates (id INTEGER PRIMARY KEY AUTOINCREMENT, part_id INTEGER NOT NULL REFERENCES parts(id) ON DELETE CASCADE,
                    sort INTEGER NOT NULL, source_file TEXT NOT NULL, plate_index INTEGER NOT NULL, copies INTEGER NOT NULL,
                    parts_per_plate INTEGER NOT NULL, sliced_seconds INTEGER NOT NULL, override_seconds INTEGER, thumbnail BLOB);
                INSERT INTO jobs VALUES (1, 'Old job', 'Harbor Dive Shop', '2026-001', '2026-10-28', NULL, '0', 0, 0, NULL, '');
                INSERT INTO parts VALUES (1, 1, 0, 'Cap', 1);
                INSERT INTO plates VALUES (1, 1, 0, 'cap.gcode.3mf', 1, 1, 10, 3600, NULL, NULL);
                """;
            cmd.ExecuteNonQuery();
        }

        var db = new Database(path);
        var printer = Assert.Single(db.GetPrinters());
        Assert.Equal(PrintTechnology.Fdm, printer.Technology);
        Assert.Equal(3.25m, printer.MachineRatePerHour);
        Assert.Equal(180m, printer.Watts);
        Assert.Equal(0.07m, printer.WasteAllowance);
        Assert.Equal(0.04m, printer.MaterialMarkup);
        var plate = db.GetJobs().Single().Parts[0].Plates[0];
        Assert.Equal(printer.Id, plate.PrinterId);
        Assert.Equal(PrintTechnology.Fdm, plate.Technology);
        Assert.Equal("Sample Printing Co.", db.LoadSettings().BusinessName);
        Assert.Empty(db.GetResins());                  // no example resin added to an existing database

        _ = new Database(path);                        // opening again changes nothing
        Assert.Single(db.GetPrinters());
    }
}

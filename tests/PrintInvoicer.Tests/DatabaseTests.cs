using PrintInvoicer.Core.Data;
using PrintInvoicer.Core.Models;
using PrintInvoicer.Core.Pricing;
using PrintInvoicer.Core.Slicing;

namespace PrintInvoicer.Tests;

public sealed class DatabaseTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pi-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    [Fact]
    public void Seeds_filaments_once()
    {
        var db = new Database(_path);
        int count = db.GetFilaments().Count;
        Assert.Equal(13, count);
        _ = new Database(_path);
        Assert.Equal(count, db.GetFilaments().Count);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var db = new Database(_path);
        Assert.Equal(0.20m, db.LoadSettings().ElectricityPerKwh);

        var s = db.LoadSettings();
        s.ElectricityPerKwh = 0.196m;
        s.PaymentTerms = "Due on receipt";
        Assert.True(s.CheckForUpdates);
        s.CheckForUpdates = false;
        db.SaveSettings(s);

        var back = db.LoadSettings();
        Assert.Equal(0.196m, back.ElectricityPerKwh);
        Assert.Equal("Due on receipt", back.PaymentTerms);
        Assert.False(back.CheckForUpdates);
    }

    [Fact]
    public void Invoice_numbers_advance()
    {
        var db = new Database(_path);
        var date = new DateTime(2026, 10, 28);
        Assert.Equal("2026-001", db.AllocateInvoiceNumber(date));
        Assert.Equal("2026-002", db.AllocateInvoiceNumber(date));
    }

    [Fact]
    public void Invoice_numbers_skip_ones_already_used()
    {
        var db = new Database(_path);
        var date = new DateTime(2026, 10, 28);
        db.SaveJob(new Job { Title = "a", InvoiceNumber = db.AllocateInvoiceNumber(date) });   // 2026-001
        db.SaveJob(new Job { Title = "typed", InvoiceNumber = "2026-002" });

        // A settings save from a stale copy set the counter back to 1, as happened with the Settings tab.
        db.SetNextInvoiceNumber(1, 2026);

        Assert.Equal("2026-003", db.AllocateInvoiceNumber(date));
        Assert.Equal("2026-004", db.AllocateInvoiceNumber(date));
    }

    [Fact]
    public void Peek_is_one_past_the_highest_saved_number_and_does_not_use_it_up()
    {
        var db = new Database(_path);
        var date = new DateTime(2026, 10, 29);
        db.SaveJob(new Job { Title = "a", InvoiceNumber = "2026-001" });
        db.SaveJob(new Job { Title = "b", InvoiceNumber = "2026-005" });
        db.SaveJob(new Job { Title = "other format", InvoiceNumber = "CS-099" });

        Assert.Equal("2026-006", db.PeekNextInvoiceNumber(date));
        Assert.Equal("2026-006", db.PeekNextInvoiceNumber(date));
        Assert.Equal(1, db.LoadSettings().NextInvoiceNumber);

        // A counter set ahead in Settings still wins.
        db.SetNextInvoiceNumber(20, 2026);
        Assert.Equal("2026-020", db.PeekNextInvoiceNumber(date));
    }

    [Fact]
    public void Numbering_restarts_at_001_each_year()
    {
        var db = new Database(_path);
        for (int i = 0; i < 5; i++) db.AllocateInvoiceNumber(new DateTime(2026, 12, 30));   // 2026-001..005, counter at 6
        Assert.Equal("2027-001", db.AllocateInvoiceNumber(new DateTime(2027, 1, 2)));
        Assert.Equal("2027-002", db.AllocateInvoiceNumber(new DateTime(2027, 1, 3)));
        // A late 2026 invoice still continues 2026's own sequence.
        db.SaveJob(new Job { Title = "late", InvoiceNumber = "2026-005" });
        Assert.Equal("2026-006", db.PeekNextInvoiceNumber(new DateTime(2026, 12, 31)));
    }

    [Fact]
    public void A_counter_set_ahead_applies_only_to_its_year()
    {
        var db = new Database(_path);
        db.SetNextInvoiceNumber(50, 2026);
        Assert.Equal("2026-050", db.PeekNextInvoiceNumber(new DateTime(2026, 11, 1)));
        Assert.Equal("2027-001", db.PeekNextInvoiceNumber(new DateTime(2027, 1, 1)));
    }

    [Fact]
    public void Without_a_year_in_the_prefix_numbering_continues()
    {
        var db = new Database(_path);
        var s = db.LoadSettings();
        s.InvoicePrefix = "INV-";
        db.SaveSettings(s);
        for (int i = 0; i < 3; i++) db.AllocateInvoiceNumber(new DateTime(2026, 12, 30));
        Assert.Equal("INV-004", db.AllocateInvoiceNumber(new DateTime(2027, 1, 2)));
    }

    [Fact]
    public void Detects_duplicate_invoice_numbers()
    {
        var db = new Database(_path);
        var job = new Job { Title = "a", InvoiceNumber = "2026-001" };
        db.SaveJob(job);

        Assert.True(db.InvoiceNumberInUse(" 2026-001 ", excludeJobId: null));
        Assert.False(db.InvoiceNumberInUse("2026-001", excludeJobId: job.Id));
        Assert.False(db.InvoiceNumberInUse("2026-002", excludeJobId: null));
    }

    [Fact]
    public void Job_round_trips_with_plates_and_slots()
    {
        var db = new Database(_path);
        var filaments = db.GetFilaments();
        var job = new Job { Title = "Tube caps", Customer = "Harbor Dive Shop", ModelingHours = 1.25m, PostProcessing = true };
        JobBuilder.AddFile(job, BambuFileReader.Read(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Cylindex_Tube_Cap.gcode.3mf")),
            filaments, db.GetMappings());
        job.Parts[0].Plates[0].Copies = 2;
        job.Parts[0].Plates[0].Filaments[4].OverrideGrams = 150.5m;
        job.Parts[0].Plates[0].Filaments[0].CustomerSupplied = true;
        db.SaveJob(job);

        // Save again to make sure rewriting children does not duplicate them.
        job.Paid = true;
        db.SaveJob(job);

        var back = Assert.Single(db.GetJobs());
        Assert.True(back.Paid);
        Assert.Equal(1.25m, back.ModelingHours);
        var plate = Assert.Single(Assert.Single(back.Parts).Plates);
        Assert.Equal(2, plate.Copies);
        Assert.Equal(20, plate.PartsPerPlate);
        Assert.Equal(21354, plate.SlicedSeconds);
        Assert.NotNull(plate.Thumbnail);
        Assert.Equal(5, plate.Filaments.Count);
        Assert.Equal(150.5m, plate.Filaments[4].OverrideGrams);
        Assert.True(plate.Filaments[0].CustomerSupplied);
        Assert.Equal(5.22m, plate.Filaments[1].SlicedGrams);

        // Creality white was matched by vendor + color from the seeded list.
        var white = filaments.Single(f => f.Id == plate.Filaments[0].FilamentId);
        Assert.Equal("White", white.ColorName);

        db.DeleteJob(back.Id);
        Assert.Empty(db.GetJobs());
    }

    [Fact]
    public void Printed_flag_round_trips()
    {
        var db = new Database(_path);
        var job = new Job { Title = "a" };
        db.SaveJob(job);
        Assert.False(db.GetJobs()[0].Printed);

        db.SetPrinted(job.Id, true, new DateTime(2026, 10, 29));
        var back = db.GetJobs()[0];
        Assert.True(back.Printed);
        Assert.Equal(new DateTime(2026, 10, 29), back.PrintedDate);

        // Saving the whole job keeps it.
        back.Title = "b";
        db.SaveJob(back);
        Assert.True(db.GetJobs()[0].Printed);
    }

    [Fact]
    public void Older_database_gets_printed_column()
    {
        // A jobs table as created before the printed flag existed, with one saved job.
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE jobs (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT NOT NULL, customer TEXT NOT NULL,
                    invoice_number TEXT, created_date TEXT NOT NULL, invoice_date TEXT, modeling_hours TEXT NOT NULL,
                    post_processing INTEGER NOT NULL, paid INTEGER NOT NULL, paid_date TEXT, notes TEXT NOT NULL);
                INSERT INTO jobs(title, customer, invoice_number, created_date, modeling_hours, post_processing, paid, notes)
                VALUES ('old', 'Harbor Dive Shop', '2026-001', '2026-10-28', '0', 0, 0, '');
                CREATE TABLE parts (id INTEGER PRIMARY KEY AUTOINCREMENT, job_id INTEGER NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
                    sort INTEGER NOT NULL, name TEXT NOT NULL, charge_slicing INTEGER NOT NULL);
                CREATE TABLE plates (id INTEGER PRIMARY KEY AUTOINCREMENT, part_id INTEGER NOT NULL REFERENCES parts(id) ON DELETE CASCADE,
                    sort INTEGER NOT NULL, source_file TEXT NOT NULL, plate_index INTEGER NOT NULL, copies INTEGER NOT NULL,
                    parts_per_plate INTEGER NOT NULL, sliced_seconds INTEGER NOT NULL, override_seconds INTEGER, thumbnail BLOB);
                INSERT INTO parts(job_id, sort, name, charge_slicing) VALUES (1, 0, 'Cap', 1);
                INSERT INTO plates(part_id, sort, source_file, plate_index, copies, parts_per_plate, sliced_seconds)
                VALUES (1, 0, 'cap.gcode.3mf', 1, 1, 10, 3600);
                """;
            cmd.ExecuteNonQuery();
        }

        var db = new Database(_path);
        var job = Assert.Single(db.GetJobs());
        Assert.Equal("old", job.Title);
        Assert.False(job.Printed);
        var oldPlate = Assert.Single(Assert.Single(job.Parts).Plates);
        Assert.False(oldPlate.HasFailures);
        Assert.Equal("", oldPlate.FailureNote);

        db.SetPrinted(job.Id, true, DateTime.Today);
        Assert.True(db.GetJobs()[0].Printed);
    }

    [Fact]
    public void Plate_failures_round_trip()
    {
        var db = new Database(_path);
        var job = new Job
        {
            Title = "viewers",
            Parts = { new JobPart { Name = "Body", Plates = { new JobPlate { PartsPerPlate = 10, HasFailures = true, FailedCount = 1,
                FailureNote = "Broke free from the print bed" } } } },
        };
        db.SaveJob(job);

        var plate = db.GetJobs()[0].Parts[0].Plates[0];
        Assert.True(plate.HasFailures);
        Assert.Equal(1, plate.FailedCount);
        Assert.Equal("Broke free from the print bed", plate.FailureNote);
        Assert.Equal(10, plate.TotalObjects);
    }

    [Fact]
    public void Deleting_a_filament_keeps_remembered_resin_choices()
    {
        // Filaments and resins are numbered separately, so a new database has filament 1 and resin 1.
        var db = new Database(_path);
        var resin = db.GetResins().Single();
        var filament = db.GetFilaments().Single(f => f.Id == resin.Id);
        string resinKey = ResinMatcher.MappingKey("Creality Blue Fast resin");
        db.SaveMapping(resinKey, resin.Id);
        db.SaveMapping("BAMBU|PLA|#FFFFFF", filament.Id);

        Assert.True(db.DeleteFilament(filament.Id));

        var mappings = db.GetMappings();
        Assert.Equal(resin.Id, mappings[resinKey]);
        Assert.False(mappings.ContainsKey("BAMBU|PLA|#FFFFFF"));
    }

    [Fact]
    public void Used_filament_is_deactivated_not_deleted()
    {
        var db = new Database(_path);
        var filament = db.GetFilaments().First(f => f.ColorName == "Black" && f.Vendor == "Creality");
        var job = new Job
        {
            Parts = { new JobPart { Name = "x", Plates = { new JobPlate { Filaments = { new PlateFilament { FilamentId = filament.Id } } } } } },
        };
        db.SaveJob(job);

        Assert.False(db.DeleteFilament(filament.Id));
        Assert.False(db.GetFilaments().Single(f => f.Id == filament.Id).Active);
    }
}

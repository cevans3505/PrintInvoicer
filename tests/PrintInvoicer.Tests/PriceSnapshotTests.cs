using Microsoft.Data.Sqlite;
using PrintInvoicer.Core.Data;
using PrintInvoicer.Core.Models;
using PrintInvoicer.Core.Pricing;
using PrintInvoicer.Core.Slicing;

namespace PrintInvoicer.Tests;

/// <summary>A finalized invoice keeps the prices it was sent with, whatever changes later.</summary>
public sealed class PriceSnapshotTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pi-snap-{Guid.NewGuid():N}.db");
    private static readonly DateTime Today = new(2026, 10, 30);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    /// <summary>The Wire Brush Insert job (CS-001) on an FDM printer, plus a resin plate and an extra cost.</summary>
    private (Database Db, Job Job) Setup()
    {
        var db = new Database(_path);
        var fdm = Printer.NewDefault(PrintTechnology.Fdm, "Bambu Lab H2D");
        var resinPrinter = Printer.NewDefault(PrintTechnology.Resin, "ELEGOO Saturn 4 Ultra");
        db.SavePrinter(fdm);
        db.SavePrinter(resinPrinter);
        db.SaveExtraCost(new ExtraCost { Name = "IPA", Cost = 0.75m, Basis = CostBasis.PerPrint, AppliesTo = PrintTechnology.Resin });

        var job = new Job { Title = "Brushes", InvoiceNumber = "2026-010" };
        JobBuilder.AddFile(job, BambuFileReader.Read(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Wire_Brush_Insert.gcode.3mf")),
            db.GetFilaments(), new Dictionary<string, long>(), printerId: fdm.Id);
        JobBuilder.AddFile(job, GooFileReader.Parse("clip.goo", TestFiles.GooHeader(profile: "Standard resin")), [],
            new Dictionary<string, long>(), db.GetResins(), resinPrinter.Id);
        db.SaveJob(job);
        return (db, job);
    }

    private static decimal Total(Database db, Job job) => PricingCalculator.Calculate(job, PriceSnapshot.For(job, db.LoadPriceBook())).Total;

    private static void Finalize(Database db, Job job)
    {
        job.FinalizeInvoice(Today, redate: false);
        PriceSnapshot.Freeze(job, db.LoadPriceBook());
        db.SaveJob(job);
    }

    /// <summary>Raises every price the job depends on.</summary>
    private static void ChangeEveryPrice(Database db)
    {
        foreach (var f in db.GetFilaments()) { f.SpoolPrice += 10; db.SaveFilament(f); }
        foreach (var r in db.GetResins()) { r.BottlePrice += 10; db.SaveResin(r); }
        foreach (var p in db.GetPrinters()) { p.MachineRatePerHour = 4m; p.Watts += 100; p.WasteAllowance = 0.2m; db.SavePrinter(p); }
        foreach (var e in db.GetExtraCosts()) { e.Cost = 3m; db.SaveExtraCost(e); }
        db.SaveExtraCost(new ExtraCost { Name = "New fee", Cost = 9m, Basis = CostBasis.PerJob });
        var s = db.LoadSettings();
        s.ElectricityPerKwh = 0.50m; s.SlicingFeePerPart = 25m; s.PaymentTerms = "Due on receipt";
        db.SaveSettings(s);
    }

    [Fact]
    public void Finalized_total_survives_every_price_change()
    {
        var (db, job) = Setup();
        decimal before = Total(db, job);
        Finalize(db, job);
        Assert.Equal(before, job.FinalizedTotal);

        ChangeEveryPrice(db);

        var saved = db.GetJobs().Single();
        var book = PriceSnapshot.For(saved, db.LoadPriceBook());
        var quote = PricingCalculator.Calculate(saved, book);
        Assert.Equal(before, quote.Total);
        Assert.DoesNotContain(quote.Warnings, w => w.Contains("finalized at"));
        Assert.Equal("Net 30", book.Settings.PaymentTerms);    // the terms it was sent with
    }

    [Fact]
    public void Drafts_follow_todays_prices()
    {
        var (db, job) = Setup();
        decimal before = Total(db, job);
        ChangeEveryPrice(db);
        Assert.NotEqual(before, Total(db, db.GetJobs().Single()));
    }

    [Fact]
    public void Revert_to_draft_reprices_at_todays_rates()
    {
        var (db, job) = Setup();
        Finalize(db, job);
        decimal sent = job.FinalizedTotal!.Value;
        ChangeEveryPrice(db);

        job.RevertToDraft();
        db.SaveJob(job);
        var saved = db.GetJobs().Single();
        Assert.Null(saved.PriceSnapshot);
        Assert.Null(saved.FinalizedTotal);
        Assert.True(Total(db, saved) > sent);
    }

    [Fact]
    public void Refinalizing_keeps_the_first_prices()
    {
        var (db, job) = Setup();
        Finalize(db, job);
        decimal sent = job.FinalizedTotal!.Value;
        ChangeEveryPrice(db);

        job.FinalizeInvoice(Today.AddDays(5), redate: true);
        PriceSnapshot.Freeze(job, db.LoadPriceBook());
        Assert.Equal(sent, job.FinalizedTotal);
        Assert.Equal(sent, Total(db, job));
    }

    [Fact]
    public void Draft_reverted_by_an_older_version_is_finalized_at_todays_prices()
    {
        var (db, job) = Setup();
        Finalize(db, job);
        decimal sent = job.FinalizedTotal!.Value;

        // Versions before 3.2 revert to draft without knowing about frozen prices, so they are left behind.
        using (var c = new SqliteConnection($"Data Source={_path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE jobs SET finalized = 0, finalized_date = NULL, invoice_date = NULL";
            cmd.ExecuteNonQuery();
        }
        ChangeEveryPrice(db);

        var reopened = new Database(_path);
        var draft = reopened.GetJobs().Single();
        decimal today = Total(reopened, draft);
        Assert.NotEqual(sent, today);

        Finalize(reopened, draft);
        Assert.Equal(today, draft.FinalizedTotal);
        Assert.Equal(today, Total(reopened, reopened.GetJobs().Single()));
    }

    [Fact]
    public void Changed_finalized_job_is_flagged()
    {
        var (db, job) = Setup();
        Finalize(db, job);
        job.Parts[0].Plates[0].Copies = 3;   // edited behind the lock, e.g. by an older version
        var quote = PricingCalculator.Calculate(job, PriceSnapshot.For(job, db.LoadPriceBook()));
        Assert.Contains(quote.Warnings, w => w.Contains("was finalized at"));
    }

    [Fact]
    public void Pdf_folder_comes_from_current_settings()
    {
        var (db, job) = Setup();
        Finalize(db, job);
        var s = db.LoadSettings();
        s.InvoiceFolder = @"D:\Invoices";
        db.SaveSettings(s);
        Assert.Equal(@"D:\Invoices", PriceSnapshot.For(job, db.LoadPriceBook()).Settings.InvoiceFolder);
    }

    [Fact]
    public void Older_finalized_invoices_are_frozen_on_upgrade()
    {
        var (db, job) = Setup();
        decimal before = Total(db, job);
        job.FinalizeInvoice(Today, redate: false);      // finalized the 3.1 way: no snapshot
        db.SaveJob(job);
        Assert.Null(db.GetJobs().Single().PriceSnapshot);

        var reopened = new Database(_path);
        var saved = reopened.GetJobs().Single();
        Assert.NotNull(saved.PriceSnapshot);
        Assert.Equal(before, saved.FinalizedTotal);
        ChangeEveryPrice(reopened);
        Assert.Equal(before, Total(reopened, reopened.GetJobs().Single()));
    }
}

using System.IO;
using Microsoft.Data.Sqlite;
using PrintInvoicer.App;
using PrintInvoicer.Core.Data;
using PrintInvoicer.Core.Models;
using PrintInvoicer.Core.Slicing;
using PrintInvoicer.Tests;

namespace PrintInvoicer.UiTests;

/// <summary>
/// A throwaway database with one of everything: an FDM job from a Bambu file, a resin job from a .goo file, a plate
/// typed in by hand, extra costs, and a finalized invoice. It becomes App.Db for the test that made it.
/// </summary>
internal sealed class SampleShop : IDisposable
{
    public Database Db { get; }
    public string Folder { get; }
    public Job FdmJob { get; }
    public Job ResinJob { get; }
    public Job FinalizedJob { get; }

    public SampleShop()
    {
        Folder = Path.Combine(WpfHost.TempFolder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        Db = new Database(Path.Combine(Folder, "printinvoicer.db"));

        var s = Db.LoadSettings();
        s.BusinessName = "Sample Printing Co.";
        s.ContactLine = "555.0100";
        s.DefaultCustomer = "Harbor Dive Shop";
        s.InvoiceFolder = Path.Combine(Folder, "Invoices");
        Db.SaveSettings(s);

        var fdm = Printer.NewDefault(PrintTechnology.Fdm, "Bambu Lab H2D");
        var resin = Printer.NewDefault(PrintTechnology.Resin, "ELEGOO Saturn 4 Ultra");
        Db.SavePrinter(fdm);
        Db.SavePrinter(resin);
        Db.SaveExtraCost(new ExtraCost { Name = "IPA and gloves", Cost = 0.75m, Basis = CostBasis.PerPrint, AppliesTo = PrintTechnology.Resin });
        Db.SaveExtraCost(new ExtraCost { Name = "Wash and cure", Cost = 5m, Basis = CostBasis.PerJob, AppliesTo = PrintTechnology.Resin });

        FdmJob = new Job { Title = "Wire Brush Inserts", Customer = "Harbor Dive Shop", InvoiceNumber = "2026-001", Notes = "Sample notes" };
        JobBuilder.AddFile(FdmJob, BambuFileReader.Read(TestFiles.Fixture("Wire_Brush_Insert.gcode.3mf")), Db.GetFilaments(),
            Db.GetMappings(), printerId: fdm.Id);
        Db.SaveJob(FdmJob);

        ResinJob = new Job { Title = "Regulator clips", Customer = "Harbor Dive Shop", InvoiceNumber = "2026-002" };
        JobBuilder.AddFile(ResinJob, GooFileReader.Parse(Path.Combine(Folder, "clip.goo"), TestFiles.GooHeader(profile: "Standard resin")),
            [], Db.GetMappings(), Db.GetResins(), resin.Id);
        var manual = JobBuilder.ManualPlate(resin);
        manual.SlicedSeconds = 5400;
        manual.Resin!.SlicedMl = 20m;
        manual.Resin.ResinId = Db.GetResins().First().Id;
        ResinJob.Parts.Add(new JobPart { Name = "Hose clip", Plates = { manual } });
        Db.SaveJob(ResinJob);

        FinalizedJob = new Job { Title = "Tube caps", Customer = "Harbor Dive Shop", InvoiceNumber = "2026-003" };
        JobBuilder.AddFile(FinalizedJob, BambuFileReader.Read(TestFiles.Fixture("Cylindex_Tube_Cap.gcode.3mf")), Db.GetFilaments(),
            Db.GetMappings(), printerId: fdm.Id);
        InvoiceActions.Finalize(Db, FinalizedJob, new DateTime(2026, 10, 30), redate: false);
        Db.SaveJob(FinalizedJob);

        App.App.Db = Db;
    }

    public Job Reload(Job job) => Db.GetJobs().Single(j => j.Id == job.Id);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
    }
}

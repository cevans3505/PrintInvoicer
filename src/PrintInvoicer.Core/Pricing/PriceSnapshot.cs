using System.Text.Json;
using System.Text.Json.Serialization;
using PrintInvoicer.Core.Models;

namespace PrintInvoicer.Core.Pricing;

/// <summary>
/// The prices a finalized invoice was worked out from, saved with the job so the invoice the customer has never changes
/// when a material price, printer rate, extra cost or shop setting is edited later. Only what the job uses is kept.
/// </summary>
public static class PriceSnapshot
{
    private sealed record Contents(AppSettings Settings, List<Filament> Filaments, List<Resin> Resins, List<Printer> Printers, List<ExtraCost> ExtraCosts);

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public static string Capture(Job job, PriceBook book)
    {
        var plates = job.AllPlates.ToList();
        var filaments = plates.SelectMany(p => p.Filaments).Select(f => book.Filament(f.FilamentId)).OfType<Filament>().DistinctBy(f => f.Id).ToList();
        var resins = plates.Select(p => book.Resin(p.Resin?.ResinId)).OfType<Resin>().DistinctBy(r => r.Id).ToList();
        // The printer each plate is actually priced on, including the default stand-in when none is set up.
        var printers = plates.Select(p => book.PrinterFor(p).Printer).DistinctBy(p => p.Id).ToList();
        var extras = book.ExtraCosts.Where(e => e.Active).ToList();
        return JsonSerializer.Serialize(new Contents(book.Settings, filaments, resins, printers, extras), Json);
    }

    /// <summary>
    /// The saved prices. Where PDFs are saved is not part of the invoice, so <paramref name="current"/>'s folder is kept.
    /// </summary>
    public static PriceBook Restore(string snapshot, AppSettings current)
    {
        var c = JsonSerializer.Deserialize<Contents>(snapshot, Json) ?? throw new JsonException("Empty price snapshot.");
        c.Settings.InvoiceFolder = current.InvoiceFolder;
        return new PriceBook(c.Settings, c.Filaments, c.Resins, c.Printers, c.ExtraCosts);
    }

    /// <summary>The prices to use for this job: its snapshot once finalized, otherwise today's.</summary>
    public static PriceBook For(Job job, PriceBook current) =>
        job.Finalized && job.PriceSnapshot is string s ? Restore(s, current.Settings) : current;

    /// <summary>Freezes the job's prices and total at finalize. A job already frozen keeps its original snapshot.</summary>
    public static void Freeze(Job job, PriceBook current)
    {
        if (job.PriceSnapshot is not null) return;
        job.PriceSnapshot = Capture(job, current);
        job.FinalizedTotal = PricingCalculator.Calculate(job, Restore(job.PriceSnapshot, current.Settings)).Total;
    }
}

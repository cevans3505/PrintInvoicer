using PrintInvoicer.Core.Models;

namespace PrintInvoicer.Core.Pricing;

/// <summary>Everything a job is priced from: shop settings, printers, materials and extra costs.</summary>
public sealed class PriceBook
{
    public AppSettings Settings { get; }
    public IReadOnlyDictionary<long, Filament> Filaments { get; }
    public IReadOnlyDictionary<long, Resin> Resins { get; }
    public IReadOnlyDictionary<long, Printer> Printers { get; }
    public IReadOnlyList<ExtraCost> ExtraCosts { get; }

    public PriceBook(AppSettings settings, IEnumerable<Filament>? filaments = null, IEnumerable<Resin>? resins = null,
        IEnumerable<Printer>? printers = null, IEnumerable<ExtraCost>? extraCosts = null)
    {
        Settings = settings;
        Filaments = (filaments ?? []).ToDictionary(f => f.Id);
        Resins = (resins ?? []).ToDictionary(r => r.Id);
        Printers = (printers ?? []).ToDictionary(p => p.Id);
        ExtraCosts = (extraCosts ?? []).ToList();
    }

    public Filament? Filament(long? id) => id is long v && Filaments.TryGetValue(v, out var f) ? f : null;
    public Resin? Resin(long? id) => id is long v && Resins.TryGetValue(v, out var r) ? r : null;

    /// <summary>The plate's printer; failing that the first active printer of its kind, or default rates (with a reason).</summary>
    public (Printer Printer, string? Problem) PrinterFor(JobPlate plate)
    {
        if (plate.PrinterId is long id && Printers.TryGetValue(id, out var p) && p.Technology == plate.Technology)
            return (p, null);

        var fallback = Printers.Values.Where(x => x.Technology == plate.Technology).OrderByDescending(x => x.Active).ThenBy(x => x.Id).FirstOrDefault();
        string kind = Printer.TechnologyLabel(plate.Technology);
        if (fallback != null)
            return (fallback, plate.PrinterId is null ? null : $"its printer was deleted, so {fallback.Name} rates are used");
        return (Printer.NewDefault(plate.Technology), $"no {kind} printer is set up, so default {kind} rates are used. Add one on the Printers tab");
    }

    /// <summary>Active printers of a kind, for pickers.</summary>
    public IEnumerable<Printer> PrintersOf(PrintTechnology t) => Printers.Values.Where(p => p.Technology == t && p.Active).OrderBy(p => p.Name);
}

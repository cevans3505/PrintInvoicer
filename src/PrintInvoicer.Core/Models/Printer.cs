namespace PrintInvoicer.Core.Models;

/// <summary>The two kinds of printer the app prices: FDM (filament) and resin (MSLA). The numbers are stored in the database.</summary>
public enum PrintTechnology { Fdm = 0, Resin = 1 }

/// <summary>
/// A printer and the rates charged for printing on it. Waste and markup apply to your own material printed on it,
/// so a resin printer can carry a higher waste allowance (resin left on supports and in the vat) than an FDM one.
/// </summary>
public class Printer
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public PrintTechnology Technology { get; set; }

    /// <summary>What you charge per hour of printing on this printer, and how much power it draws while printing.</summary>
    public decimal MachineRatePerHour { get; set; }
    public decimal Watts { get; set; }

    // Fractions, so 0.05 = 5%.
    public decimal WasteAllowance { get; set; }
    public decimal MaterialMarkup { get; set; }

    public bool Active { get; set; } = true;
    public string Notes { get; set; } = "";

    /// <summary>Multiplier applied to your own material printed on this printer: (1 + waste) x (1 + markup).</summary>
    public decimal MaterialFactor => (1 + WasteAllowance) * (1 + MaterialMarkup);

    /// <summary>"FDM" or "Resin", for display.</summary>
    public string TechnologyName => TechnologyLabel(Technology);

    public static string TechnologyLabel(PrintTechnology t) => t == PrintTechnology.Resin ? "Resin" : "FDM";

    /// <summary>Starting rates for a printer added automatically or from the Printers tab. Every value is editable.</summary>
    public static Printer NewDefault(PrintTechnology technology, string? name = null) => technology == PrintTechnology.Resin
        ? new Printer { Name = name ?? "Resin printer", Technology = technology, MachineRatePerHour = 2.50m, Watts = 100m, WasteAllowance = 0.10m, MaterialMarkup = 0.05m }
        : new Printer { Name = name ?? "FDM printer", Technology = technology, MachineRatePerHour = 2.50m, Watts = 200m, WasteAllowance = 0.05m, MaterialMarkup = 0.05m };

    public override string ToString() => Name;
}

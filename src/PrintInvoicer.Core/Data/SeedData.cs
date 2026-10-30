using PrintInvoicer.Core.Models;

namespace PrintInvoicer.Core.Data;

/// <summary>Starter filament list (prices as of 2026-10-25), loaded into a new database. Edit it on the Materials tab.</summary>
public static class SeedData
{
    public static IEnumerable<Filament> Filaments() => new[]
    {
        F("Bambu Lab", "PLA Basic", "", "", 24.99m, "Workbook note: slicer files show both $15.99 and $24.99. Set what you actually pay."),
        F("Bambu Lab", "PETG HF", "", "", 24.99m),
        F("Bambu Lab", "Support for PLA/PETG", "", "", 69.98m, "Sold on smaller spools; enter the actual spool price and weight."),
        F("Generic", "PLA", "", "", 20m, "Slicer default price."),
        F("Generic", "TPU", "", "", 20m, "Slicer default price; TPU usually costs more."),
        F("eSUN", "PLA+", "", "", 22.99m),
        F("Anycubic", "PLA", "", "", 20m, "Used on the Kobra 2 Pro."),
        F("Creality", "PLA", "Black", "#000000", 14.99m),
        F("Creality", "PLA", "Red", "#FF0000", 18.04m),
        F("Creality", "PLA", "Blue", "#0000FF", 19.99m),
        F("Creality", "PLA", "Yellow", "#FFFF00", 20.89m),
        F("Creality", "PLA", "White", "#FFFFFF", 19.99m),
        F("Overture", "TPU 95A High Speed", "Black", "#000000", 27.99m),
    };

    /// <summary>One example resin for a brand-new database, so the Materials tab shows what to fill in.</summary>
    public static IEnumerable<Resin> Resins() => new[]
    {
        new Resin { Vendor = "Generic", Name = "Standard resin", ColorName = "Grey", BottlePrice = 30m, BottleSize = 1000m,
            BottleUnit = BottleUnit.Grams, Density = 1.10m, Notes = "Example. Set your own vendor, price, bottle size and density." },
    };

    private static Filament F(string vendor, string material, string color, string hex, decimal price, string notes = "") =>
        new() { Vendor = vendor, Material = material, ColorName = color, ColorHex = hex, SpoolPrice = price, SpoolGrams = 1000m, Notes = notes };
}

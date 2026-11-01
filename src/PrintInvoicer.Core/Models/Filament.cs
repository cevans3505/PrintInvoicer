namespace PrintInvoicer.Core.Models;

/// <summary>
/// A filament you buy. Its spool price and weight give the cost per gram used to price your own material on FDM
/// plates. Stored in the database's filaments table.
/// </summary>
public class Filament
{
    public long Id { get; set; }
    public string Vendor { get; set; } = "";
    public string Material { get; set; } = "";
    public string ColorName { get; set; } = "";

    /// <summary>Color as #RRGGBB, used to match AMS slots in sliced files.</summary>
    public string ColorHex { get; set; } = "";

    /// <summary>What you pay for one spool, and how many grams are on it.</summary>
    public decimal SpoolPrice { get; set; }
    public decimal SpoolGrams { get; set; } = 1000m;
    /// <summary>Inactive filaments stay on jobs that already use them but are no longer offered for new plates.</summary>
    public bool Active { get; set; } = true;
    public string Notes { get; set; } = "";

    /// <summary>Spool price divided by spool weight; 0 when the weight is not set (pricing then warns).</summary>
    public decimal CostPerGram => SpoolGrams > 0 ? SpoolPrice / SpoolGrams : 0m;

    /// <summary>Vendor, material and color joined, e.g. "Creality PLA Black", as shown in pickers and on invoices.</summary>
    public string DisplayName
    {
        get
        {
            var parts = new[] { Vendor, Material, ColorName }.Where(s => !string.IsNullOrWhiteSpace(s));
            return string.Join(" ", parts);
        }
    }

    public override string ToString() => DisplayName;
}

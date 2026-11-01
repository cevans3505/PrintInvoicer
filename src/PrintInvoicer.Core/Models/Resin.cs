namespace PrintInvoicer.Core.Models;

/// <summary>How a resin bottle's size is given: by volume, or by weight (most resin is sold by the kilogram).</summary>
public enum BottleUnit { Grams = 0, Milliliters = 1 }

/// <summary>
/// A resin you buy. Its bottle price, size and density give the cost per mL used to price your own resin on resin
/// plates. Stored in the database's resins table.
/// </summary>
public class Resin
{
    public long Id { get; set; }
    public string Vendor { get; set; } = "";
    public string Name { get; set; } = "";
    public string ColorName { get; set; } = "";
    public string ColorHex { get; set; } = "";

    /// <summary>What you pay for one bottle, and its size in the unit below.</summary>
    public decimal BottlePrice { get; set; }
    public decimal BottleSize { get; set; } = 1000m;
    public BottleUnit BottleUnit { get; set; } = BottleUnit.Grams;

    /// <summary>g/mL, used to turn the slicer's volume into weight when the bottle is sold by weight.</summary>
    public decimal Density { get; set; } = 1.10m;

    /// <summary>Inactive resins stay on jobs that already use them but are no longer offered for new plates.</summary>
    public bool Active { get; set; } = true;
    public string Notes { get; set; } = "";

    /// <summary>Slicers report resin by volume, so cost is worked out per mL.</summary>
    public decimal CostPerMl =>
        BottleSize <= 0 ? 0m
        : BottleUnit == BottleUnit.Milliliters ? BottlePrice / BottleSize
        : BottlePrice / BottleSize * Density;

    /// <summary>Vendor, name and color joined, as shown in pickers and on invoices.</summary>
    public string DisplayName
    {
        get
        {
            var parts = new[] { Vendor, Name, ColorName }.Where(s => !string.IsNullOrWhiteSpace(s));
            return string.Join(" ", parts);
        }
    }

    public override string ToString() => DisplayName;
}

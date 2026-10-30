namespace PrintInvoicer.Core.Models;

public class Filament
{
    public long Id { get; set; }
    public string Vendor { get; set; } = "";
    public string Material { get; set; } = "";
    public string ColorName { get; set; } = "";

    /// <summary>Color as #RRGGBB, used to match AMS slots in sliced files.</summary>
    public string ColorHex { get; set; } = "";

    public decimal SpoolPrice { get; set; }
    public decimal SpoolGrams { get; set; } = 1000m;
    public bool Active { get; set; } = true;
    public string Notes { get; set; } = "";

    public decimal CostPerGram => SpoolGrams > 0 ? SpoolPrice / SpoolGrams : 0m;

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

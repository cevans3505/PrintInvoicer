namespace PrintInvoicer.Core.Models;

/// <summary>What an extra cost is counted against.</summary>
public enum CostBasis
{
    PerJob = 0,
    PerPrint = 1,
    PerPrintHour = 2,
    PerMlResin = 3,
    PerGramFilament = 4,
}

/// <summary>
/// A cost you define yourself, such as IPA and gloves per resin print, FEP film wear per print hour, or a wash and
/// cure fee. Each applies to every job printed on the matching kind of printer, and can be switched off for one job.
/// </summary>
public class ExtraCost
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Cost { get; set; }
    public CostBasis Basis { get; set; } = CostBasis.PerPrint;

    /// <summary>Only plates on this kind of printer are charged; null means every printer.</summary>
    public PrintTechnology? AppliesTo { get; set; }

    /// <summary>
    /// Per-gram and per-mL costs only: also count material the customer supplied. Right for wear and handling; untick
    /// for a cost that comes with buying the material.
    /// </summary>
    public bool ChargeCustomerMaterial { get; set; } = true;

    public bool IsMaterialBasis => Basis is CostBasis.PerGramFilament or CostBasis.PerMlResin;

    public bool Active { get; set; } = true;
    public string Notes { get; set; } = "";

    public bool AppliesToTechnology(PrintTechnology t) =>
        (AppliesTo is null || AppliesTo == t) &&
        !(Basis == CostBasis.PerMlResin && t != PrintTechnology.Resin) &&
        !(Basis == CostBasis.PerGramFilament && t != PrintTechnology.Fdm);

    public static string BasisLabel(CostBasis b) => b switch
    {
        CostBasis.PerJob => "per job",
        CostBasis.PerPrint => "per print",
        CostBasis.PerPrintHour => "per print hour",
        CostBasis.PerMlResin => "per mL of resin",
        CostBasis.PerGramFilament => "per g of filament",
        _ => "",
    };

    /// <summary>The unit in a rate, as in "$0.50 / print".</summary>
    public static string RateUnit(CostBasis b) => b switch
    {
        CostBasis.PerJob => "job",
        CostBasis.PerPrint => "print",
        CostBasis.PerPrintHour => "h",
        CostBasis.PerMlResin => "mL",
        CostBasis.PerGramFilament => "g",
        _ => "",
    };

    public override string ToString() => Name;
}

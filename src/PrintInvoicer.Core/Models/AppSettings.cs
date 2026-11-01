namespace PrintInvoicer.Core.Models;

/// <summary>Shop-wide settings. Machine rate, power, waste and markup are set per printer (<see cref="Printer"/>).</summary>
public class AppSettings
{
    // Printing
    public decimal ElectricityPerKwh { get; set; } = 0.20m;

    // Labor and fees
    public decimal ModelingRatePerHour { get; set; } = 50m;
    public decimal SlicingFeePerPart { get; set; } = 10m;
    public decimal PostProcessingFee { get; set; } = 20m;

    // Invoice
    public string BusinessName { get; set; } = "";
    public string ContactLine { get; set; } = "";
    public string DefaultCustomer { get; set; } = "";
    public string PaymentTerms { get; set; } = "Net 30";
    public string InvoicePrefix { get; set; } = "{yyyy}-";
    public int NextInvoiceNumber { get; set; } = 1;

    /// <summary>
    /// The year <see cref="NextInvoiceNumber"/> was set for. When the prefix has {yyyy}, the counter only counts in
    /// that year, so a new year starts again at 001. 0 means not known.
    /// </summary>
    public int NextInvoiceYear { get; set; }
    public string InvoiceFolder { get; set; } = "";

    /// <summary>Where daily backups go; blank means a Backups folder next to the database.</summary>
    public string BackupFolder { get; set; } = "";

    // Updates
    /// <summary>Ask GitHub for a newer release when the app starts (at most once a day).</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Builds an invoice number like 2026-001 from the prefix (which may contain {yyyy}) and a sequence.</summary>
    public string FormatInvoiceNumber(int sequence, DateTime date) => InvoicePrefixFor(date) + sequence.ToString("000");

    public string InvoicePrefixFor(DateTime date) => InvoicePrefix.Replace("{yyyy}", date.Year.ToString("0000"));
}

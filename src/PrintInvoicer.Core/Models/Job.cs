namespace PrintInvoicer.Core.Models;

/// <summary>
/// One print job and its invoice: who it is for, its parts and plates, fees, discount, and its draft / finalized,
/// printed and paid state. Saved by <see cref="Data.Database.SaveJob"/>; priced by <see cref="Pricing.PricingCalculator"/>.
/// </summary>
public class Job
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Customer { get; set; } = "";
    /// <summary>Reserved when the job is created (can be changed by hand); null until one is given.</summary>
    public string? InvoiceNumber { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Today;
    /// <summary>Set when the invoice is finalized; empty for drafts.</summary>
    public DateTime? InvoiceDate { get; set; }

    /// <summary>Hours entered by hand; 0 means no modeling charge.</summary>
    public decimal ModelingHours { get; set; }

    /// <summary>Charge the flat post-processing fee from Settings.</summary>
    public bool PostProcessing { get; set; }

    /// <summary>Percent off the whole invoice, 0 to 100. New jobs start with the default from Settings.</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>All plates for this job have been printed (tracked separately from invoicing and payment).</summary>
    public bool Printed { get; set; }
    public DateTime? PrintedDate { get; set; }

    /// <summary>
    /// The invoice has been finalized for sending: its date is fixed (payment terms run from it) and the PDF in the
    /// invoice folder is the one the customer gets. Until then the job is a draft and can only be previewed.
    /// </summary>
    public bool Finalized { get; set; }
    public DateTime? FinalizedDate { get; set; }

    /// <summary>Prices frozen at finalize (see <see cref="Pricing.PriceSnapshot"/>), and the total they gave.</summary>
    public string? PriceSnapshot { get; set; }
    public decimal? FinalizedTotal { get; set; }

    /// <summary>The customer has paid, and when (tracked by hand).</summary>
    public bool Paid { get; set; }
    public DateTime? PaidDate { get; set; }
    /// <summary>Printed on the invoice in the Notes box.</summary>
    public string Notes { get; set; } = "";

    /// <summary>The unique parts on this invoice, each with the plates it was printed on.</summary>
    public List<JobPart> Parts { get; set; } = new();

    /// <summary>Extra costs switched off for this job only.</summary>
    public HashSet<long> ExcludedExtraCosts { get; set; } = new();

    /// <summary>Every plate of every part.</summary>
    public IEnumerable<JobPlate> AllPlates => Parts.SelectMany(p => p.Plates);

    /// <summary>
    /// Marks the invoice ready to send, dated today. A job finalized on an earlier day keeps its date unless
    /// <paramref name="redate"/> is set. The caller assigns an invoice number first if there is none.
    /// </summary>
    public void FinalizeInvoice(DateTime today, bool redate)
    {
        if (!Finalized || redate || InvoiceDate is null)
        {
            InvoiceDate = today.Date;
            FinalizedDate = today.Date;
        }
        Finalized = true;
    }

    /// <summary>
    /// Back to a draft: the date is cleared so the next finalize dates it, and the frozen prices are dropped so the job
    /// is priced at today's rates again. The invoice number is kept.
    /// </summary>
    public void RevertToDraft()
    {
        Finalized = false;
        FinalizedDate = null;
        InvoiceDate = null;
        PriceSnapshot = null;
        FinalizedTotal = null;
    }

    /// <summary>True when finalizing today would change an existing invoice date.</summary>
    public bool WouldRedate(DateTime today) => Finalized && InvoiceDate is DateTime d && d.Date != today.Date;
}

/// <summary>One unique part. Slicing is charged once per part, however many plates it is split across.</summary>
public class JobPart
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Charge the per-part slicing fee for this part.</summary>
    public bool ChargeSlicing { get; set; } = true;
    public List<JobPlate> Plates { get; set; } = new();

    /// <summary>Pieces billed for this part: for each plate, prints x parts per plate.</summary>
    public int Quantity => Plates.Sum(p => p.Copies * p.PartsPerPlate);
}

/// <summary>One sliced plate, printed <see cref="Copies"/> times.</summary>
public class JobPlate
{
    public long Id { get; set; }

    /// <summary>The sliced file this plate came from; empty for a plate entered by hand.</summary>
    public string SourceFile { get; set; } = "";
    /// <summary>Which plate of the sliced file this is (Bambu files can hold several).</summary>
    public int PlateIndex { get; set; } = 1;
    /// <summary>How many times the plate is printed, and how many parts each print makes.</summary>
    public int Copies { get; set; } = 1;
    public int PartsPerPlate { get; set; } = 1;

    /// <summary>FDM or resin, and the printer whose rates price this plate.</summary>
    public PrintTechnology Technology { get; set; }
    public long? PrinterId { get; set; }

    /// <summary>Resin plates only: the vat's resin. FDM plates use <see cref="Filaments"/> instead.</summary>
    public PlateResin? Resin { get; set; }
    /// <summary>Resin plates: layers and layer height from the file, shown for reference only.</summary>
    public int LayerCount { get; set; }
    public decimal LayerHeightMm { get; set; }

    /// <summary>True for a plate typed in by hand rather than read from a sliced file.</summary>
    public bool IsManual => string.IsNullOrEmpty(SourceFile);

    /// <summary>
    /// False for a plate read from a resin file until someone sets or confirms how many parts are on it: resin files do
    /// not record it, and the per-piece price is only right if it is.
    /// </summary>
    public bool PartsCountConfirmed { get; set; } = true;

    /// <summary>Print time from the slicer, and a time typed in to replace it (null when not replaced).</summary>
    public int SlicedSeconds { get; set; }
    public int? OverrideSeconds { get; set; }
    /// <summary>The plate's preview picture as PNG bytes, shown in the job window and on invoices.</summary>
    public byte[]? Thumbnail { get; set; }

    /// <summary>Some objects on this plate failed to print. Recorded for reference; billing is unchanged.</summary>
    public bool HasFailures { get; set; }
    /// <summary>How many objects failed, and what happened; shown on both PDFs.</summary>
    public int FailedCount { get; set; }
    public string FailureNote { get; set; } = "";

    /// <summary>FDM plates: the filament used per AMS slot. Empty on resin plates.</summary>
    public List<PlateFilament> Filaments { get; set; } = new();

    /// <summary>Objects across every print of this plate.</summary>
    public int TotalObjects => Copies * PartsPerPlate;

    /// <summary>The print time used for pricing: the typed-in time if there is one, otherwise the slicer's.</summary>
    public int EffectiveSeconds => OverrideSeconds ?? SlicedSeconds;
    public decimal EffectiveHours => EffectiveSeconds / 3600m;
}

/// <summary>One AMS slot used by a plate.</summary>
public class PlateFilament
{
    public long Id { get; set; }
    /// <summary>AMS slot number, and the filament details the slicer recorded for it (used to guess the filament).</summary>
    public int SlotId { get; set; }
    public string SlicerType { get; set; } = "";
    public string SlicerColorHex { get; set; } = "";
    public string SlicerVendor { get; set; } = "";
    public string SlicerProfile { get; set; } = "";

    /// <summary>Grams per print from the slicer, and a measured weight typed in to replace it (null when not replaced).</summary>
    public decimal SlicedGrams { get; set; }
    public decimal? OverrideGrams { get; set; }

    /// <summary>Your filament used for this slot, and whether the customer supplied it (then it is not billed).</summary>
    public long? FilamentId { get; set; }
    public bool CustomerSupplied { get; set; }

    /// <summary>The grams used for pricing: the typed-in weight if there is one, otherwise the slicer's.</summary>
    public decimal EffectiveGrams => OverrideGrams ?? SlicedGrams;
}

/// <summary>The resin a resin plate uses. Slicers report it by volume.</summary>
public class PlateResin
{
    /// <summary>The resin profile named in the sliced file, used to pick a resin.</summary>
    public string SlicerProfile { get; set; } = "";

    /// <summary>mL per print from the slicer, and a measured volume typed in to replace it (null when not replaced).</summary>
    public decimal SlicedMl { get; set; }
    public decimal? OverrideMl { get; set; }

    /// <summary>Your resin used for this plate, and whether the customer supplied it (then it is not billed).</summary>
    public long? ResinId { get; set; }
    public bool CustomerSupplied { get; set; }

    /// <summary>The mL used for pricing: the typed-in volume if there is one, otherwise the slicer's.</summary>
    public decimal EffectiveMl => OverrideMl ?? SlicedMl;
}

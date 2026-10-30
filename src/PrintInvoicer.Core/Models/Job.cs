namespace PrintInvoicer.Core.Models;

public class Job
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Customer { get; set; } = "";
    public string? InvoiceNumber { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Today;
    public DateTime? InvoiceDate { get; set; }

    /// <summary>Hours entered by hand; 0 means no modeling charge.</summary>
    public decimal ModelingHours { get; set; }

    public bool PostProcessing { get; set; }

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

    public bool Paid { get; set; }
    public DateTime? PaidDate { get; set; }
    public string Notes { get; set; } = "";

    public List<JobPart> Parts { get; set; } = new();

    /// <summary>Extra costs switched off for this job only.</summary>
    public HashSet<long> ExcludedExtraCosts { get; set; } = new();

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
    public bool ChargeSlicing { get; set; } = true;
    public List<JobPlate> Plates { get; set; } = new();

    public int Quantity => Plates.Sum(p => p.Copies * p.PartsPerPlate);
}

/// <summary>One sliced plate, printed <see cref="Copies"/> times.</summary>
public class JobPlate
{
    public long Id { get; set; }

    /// <summary>The sliced file this plate came from; empty for a plate entered by hand.</summary>
    public string SourceFile { get; set; } = "";
    public int PlateIndex { get; set; } = 1;
    public int Copies { get; set; } = 1;
    public int PartsPerPlate { get; set; } = 1;

    public PrintTechnology Technology { get; set; }
    public long? PrinterId { get; set; }

    /// <summary>Resin plates only: the vat's resin. FDM plates use <see cref="Filaments"/> instead.</summary>
    public PlateResin? Resin { get; set; }
    public int LayerCount { get; set; }
    public decimal LayerHeightMm { get; set; }

    public bool IsManual => string.IsNullOrEmpty(SourceFile);

    /// <summary>
    /// False for a plate read from a resin file until someone sets or confirms how many parts are on it: resin files do
    /// not record it, and the per-piece price is only right if it is.
    /// </summary>
    public bool PartsCountConfirmed { get; set; } = true;

    public int SlicedSeconds { get; set; }
    public int? OverrideSeconds { get; set; }
    public byte[]? Thumbnail { get; set; }

    /// <summary>Some objects on this plate failed to print. Recorded for reference; billing is unchanged.</summary>
    public bool HasFailures { get; set; }
    public int FailedCount { get; set; }
    public string FailureNote { get; set; } = "";

    public List<PlateFilament> Filaments { get; set; } = new();

    /// <summary>Objects across every print of this plate.</summary>
    public int TotalObjects => Copies * PartsPerPlate;

    public int EffectiveSeconds => OverrideSeconds ?? SlicedSeconds;
    public decimal EffectiveHours => EffectiveSeconds / 3600m;
}

/// <summary>One AMS slot used by a plate.</summary>
public class PlateFilament
{
    public long Id { get; set; }
    public int SlotId { get; set; }
    public string SlicerType { get; set; } = "";
    public string SlicerColorHex { get; set; } = "";
    public string SlicerVendor { get; set; } = "";
    public string SlicerProfile { get; set; } = "";

    public decimal SlicedGrams { get; set; }
    public decimal? OverrideGrams { get; set; }

    public long? FilamentId { get; set; }
    public bool CustomerSupplied { get; set; }

    public decimal EffectiveGrams => OverrideGrams ?? SlicedGrams;
}

/// <summary>The resin a resin plate uses. Slicers report it by volume.</summary>
public class PlateResin
{
    /// <summary>The resin profile named in the sliced file, used to pick a resin.</summary>
    public string SlicerProfile { get; set; } = "";

    public decimal SlicedMl { get; set; }
    public decimal? OverrideMl { get; set; }

    public long? ResinId { get; set; }
    public bool CustomerSupplied { get; set; }

    public decimal EffectiveMl => OverrideMl ?? SlicedMl;
}

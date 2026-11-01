using PrintInvoicer.Core.Models;

namespace PrintInvoicer.Core.Pricing;

public record SlotQuote(PlateFilament Slot, Filament? Filament, decimal Grams, decimal CostPerGram, decimal Material);

public record ResinQuote(PlateResin Plate, Resin? Resin, decimal Ml, decimal CostPerMl, decimal Material);

/// <param name="Quantity">How much of the basis was used, e.g. prints, hours or mL, across every copy.</param>
public record ExtraCostQuote(ExtraCost Cost, decimal Quantity, decimal Amount);

public record PlateQuote(
    JobPlate Plate,
    Printer Printer,
    IReadOnlyList<SlotQuote> Slots,
    ResinQuote? Resin,
    IReadOnlyList<ExtraCostQuote> Extras,
    decimal Hours,
    decimal Material,
    decimal Machine,
    decimal Electricity)
{
    public decimal ExtrasAmount => Extras.Sum(e => e.Amount);

    /// <summary>Cost of every copy of this plate, unrounded.</summary>
    public decimal Total => Material + Machine + Electricity + ExtrasAmount;

    /// <summary>Filament grams or resin mL per print.</summary>
    public decimal MaterialAmount => Resin?.Ml ?? Slots.Sum(s => s.Grams);
}

public record PartQuote(JobPart Part, IReadOnlyList<PlateQuote> Plates, int Quantity, decimal UnitPrice, decimal LineAmount)
{
    public decimal Material => Plates.Sum(p => p.Material);
    public decimal Machine => Plates.Sum(p => p.Machine);
    public decimal Electricity => Plates.Sum(p => p.Electricity);
    public decimal Extras => Plates.Sum(p => p.ExtrasAmount);
    public decimal PrintCost => Plates.Sum(p => p.Total);
    public decimal Grams => Plates.Sum(p => p.Slots.Sum(s => s.Grams) * p.Plate.Copies);
    public decimal Ml => Plates.Sum(p => (p.Resin?.Ml ?? 0) * p.Plate.Copies);
    public decimal Hours => Plates.Sum(p => p.Hours * p.Plate.Copies);
}

public record JobQuote(
    IReadOnlyList<PartQuote> Parts,
    int SlicingCount,
    decimal SlicingAmount,
    decimal ModelingHours,
    decimal ModelingAmount,
    decimal PostProcessingAmount,
    IReadOnlyList<ExtraCostQuote> JobExtras,
    IReadOnlyList<string> Warnings)
{
    public decimal PartsAmount => Parts.Sum(p => p.LineAmount);
    public decimal JobExtrasAmount => JobExtras.Sum(e => e.Amount);
    public decimal Total => PartsAmount + SlicingAmount + ModelingAmount + PostProcessingAmount + JobExtrasAmount;

    public IEnumerable<PlateQuote> AllPlates => Parts.SelectMany(p => p.Plates);
}

public static class PricingCalculator
{
    /// <summary>Prices a job with one FDM printer at default rates; for callers that have no printer list.</summary>
    public static JobQuote Calculate(Job job, AppSettings s, IReadOnlyDictionary<long, Filament> filaments) =>
        Calculate(job, new PriceBook(s, filaments.Values, printers: [Printer.NewDefault(PrintTechnology.Fdm)]));

    /// <summary>
    /// Prices a job. Per plate, on its printer's rates: material (amount x cost x (1 + waste) x (1 + markup), or zero
    /// when the customer supplied it), machine time, electricity, and any extra costs counted per print, hour or material,
    /// each multiplied by how many times the plate is printed. Each part's cost is turned into a unit price rounded to
    /// the cent, and the invoice line is unit x quantity, so what the customer sees always multiplies out exactly. When
    /// cents would put the line more than <see cref="SubCentThreshold"/> off the cost (cheap parts in bulk), the unit
    /// price keeps 4 decimals instead. Per-job extra costs are separate lines, charged when the job has a plate on a
    /// printer they apply to. <see cref="PriceBook.RulesVersion"/> selects older rules for invoices frozen under them.
    /// </summary>
    public static JobQuote Calculate(Job job, PriceBook book)
    {
        var s = book.Settings;
        bool v2 = book.RulesVersion >= 2;
        var warnings = new List<string>();
        var parts = new List<PartQuote>();
        var extras = book.ExtraCosts.Where(e => e.Active && !job.ExcludedExtraCosts.Contains(e.Id)).ToList();

        foreach (var part in job.Parts)
        {
            var plates = new List<PlateQuote>();
            foreach (var plate in part.Plates)
            {
                var (printer, problem) = book.PrinterFor(plate);
                if (problem != null) warnings.Add($"{part.Name}, plate {plate.PlateIndex}: {problem}.");
                if (!plate.PartsCountConfirmed)
                    warnings.Add($"{part.Name}: how many parts are on the plate has not been set. Resin files do not record it, " +
                                 $"so the per-piece price is only right once it is (now {plate.PartsPerPlate}).");
                decimal factor = printer.MaterialFactor;

                var slots = new List<SlotQuote>();
                ResinQuote? resin = null;
                if (plate.Technology == PrintTechnology.Resin)
                {
                    var pr = plate.Resin ??= new PlateResin();
                    var r = book.Resin(pr.ResinId);
                    decimal cpm = 0m, material = 0m;
                    if (!pr.CustomerSupplied)
                    {
                        if (r == null)
                            warnings.Add($"{part.Name}, plate {plate.PlateIndex}: no resin selected, so its material is $0.");
                        else
                        {
                            cpm = r.CostPerMl;
                            material = pr.EffectiveMl * cpm * factor * plate.Copies;
                        }
                    }
                    resin = new ResinQuote(pr, r, pr.EffectiveMl, cpm, material);
                }
                else
                {
                    foreach (var slot in plate.Filaments)
                    {
                        var f = book.Filament(slot.FilamentId);
                        decimal grams = slot.EffectiveGrams;
                        decimal cpg = 0m, material = 0m;

                        if (!slot.CustomerSupplied)
                        {
                            if (f == null)
                            {
                                warnings.Add($"{part.Name}, plate {plate.PlateIndex}: slot {slot.SlotId} ({slot.SlicerType} {slot.SlicerColorHex}) has no filament selected, so its material is $0.");
                            }
                            else
                            {
                                cpg = f.CostPerGram;
                                material = grams * cpg * factor * plate.Copies;
                            }
                        }
                        slots.Add(new SlotQuote(slot, f, grams, cpg, material));
                    }
                }

                decimal hours = plate.EffectiveHours;
                decimal machine = hours * printer.MachineRatePerHour * plate.Copies;
                decimal electricity = hours * printer.Watts / 1000m * s.ElectricityPerKwh * plate.Copies;

                var plateExtras = new List<ExtraCostQuote>();
                foreach (var e in extras.Where(e => e.Basis != CostBasis.PerJob && e.AppliesToTechnology(plate.Technology)))
                {
                    decimal used = e.Basis switch
                    {
                        CostBasis.PerPrint => plate.Copies,
                        CostBasis.PerPrintHour => hours * plate.Copies,
                        CostBasis.PerMlResin when resin is not null && (!v2 || e.ChargeCustomerMaterial || !resin.Plate.CustomerSupplied)
                            => resin.Ml * plate.Copies,
                        CostBasis.PerGramFilament
                            => slots.Where(x => !v2 || e.ChargeCustomerMaterial || !x.Slot.CustomerSupplied).Sum(x => x.Grams) * plate.Copies,
                        _ => 0,
                    };
                    if (used > 0) plateExtras.Add(new ExtraCostQuote(e, used, used * e.Cost));
                }

                decimal materialTotal = resin?.Material ?? slots.Sum(x => x.Material);
                plates.Add(new PlateQuote(plate, printer, slots, resin, plateExtras, hours, materialTotal, machine, electricity));
            }

            int qty = part.Quantity;
            // Dividing by 3600 s, or by a 750 g spool, leaves a shortfall in decimal's last digit that would turn an
            // exact half cent into $x.xx4999... and round it down; 15 places is far below a cent and far above that shortfall.
            decimal printCost = v2 ? Math.Round(plates.Sum(p => p.Total), 15, MidpointRounding.AwayFromZero) : plates.Sum(p => p.Total);
            decimal unit, line;
            if (qty > 0)
            {
                unit = Money(printCost / qty);
                line = unit * qty;
                if (v2 && Math.Abs(line - printCost) > SubCentThreshold)
                {
                    // Cheap parts in bulk: 1,000 pieces costing $4.00 would round to $0.00 each.
                    unit = Math.Round(printCost / qty, 4, MidpointRounding.AwayFromZero);
                    line = Money(unit * qty);
                }
                if (printCost > 0 && line == 0)
                    warnings.Add($"{part.Name}: costs {printCost:0.00##} but bills $0.00. Check the quantity.");
            }
            else
            {
                warnings.Add($"{part.Name}: quantity is 0.");
                unit = 0m;
                line = Money(printCost);
            }
            parts.Add(new PartQuote(part, plates, qty, unit, line));
        }

        var technologies = job.AllPlates.Select(p => p.Technology).ToHashSet();
        var jobExtras = extras
            .Where(e => e.Basis == CostBasis.PerJob && technologies.Any(e.AppliesToTechnology))
            .Select(e => new ExtraCostQuote(e, 1, Money(e.Cost)))
            .ToList();

        int slicingCount = job.Parts.Count(p => p.ChargeSlicing);
        // The fee is shown to the cent, so that is what is charged per part, as with a part's unit price.
        decimal slicing = slicingCount * (v2 ? Money(s.SlicingFeePerPart) : s.SlicingFeePerPart);
        decimal modeling = Money(job.ModelingHours * s.ModelingRatePerHour);
        decimal post = job.PostProcessing ? (v2 ? Money(s.PostProcessingFee) : s.PostProcessingFee) : 0m;

        var quote = new JobQuote(parts, slicingCount, slicing, job.ModelingHours, modeling, post, jobExtras, warnings);
        if (job.Finalized && job.FinalizedTotal is decimal sent && sent != quote.Total)
            warnings.Add($"This invoice was finalized at ${sent:0.00} but now works out to ${quote.Total:0.00}. Check it before sending it again.");
        return quote;
    }

    /// <summary>Extra costs that could apply to this job: active, and matching a kind of printer it uses.</summary>
    public static IEnumerable<ExtraCost> ApplicableExtras(Job job, PriceBook book)
    {
        var technologies = job.AllPlates.Select(p => p.Technology).ToHashSet();
        return book.ExtraCosts.Where(e => e.Active && technologies.Any(e.AppliesToTechnology));
    }

    /// <summary>How far a part's line may drift from its cost through per-piece rounding before cents give way to 4 decimals.</summary>
    public const decimal SubCentThreshold = 0.50m;

    public static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

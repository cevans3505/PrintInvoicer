using PrintInvoicer.Core.Models;
using PrintInvoicer.Core.Pricing;
using PrintInvoicer.Core.Slicing;

namespace PrintInvoicer.Tests;

public class PricingTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // Creality prices from the original costing workbook.
    private static List<Filament> CrealityFilaments() => new()
    {
        new() { Id = 1, Vendor = "Creality", Material = "PLA", ColorName = "White", ColorHex = "#FFFFFF", SpoolPrice = 19.99m },
        new() { Id = 2, Vendor = "Creality", Material = "PLA", ColorName = "Yellow", ColorHex = "#FFFF00", SpoolPrice = 20.89m },
        new() { Id = 3, Vendor = "Creality", Material = "PLA", ColorName = "Red", ColorHex = "#FF0000", SpoolPrice = 18.04m },
        new() { Id = 4, Vendor = "Creality", Material = "PLA", ColorName = "Blue", ColorHex = "#0000FF", SpoolPrice = 19.99m },
        new() { Id = 5, Vendor = "Creality", Material = "PLA", ColorName = "Black", ColorHex = "#000000", SpoolPrice = 14.99m },
    };

    private static Job JobFrom(string fixture, List<Filament> filaments, int copies)
    {
        var job = new Job { Title = "test" };
        JobBuilder.AddFile(job, BambuFileReader.Read(Fixture(fixture)), filaments, new Dictionary<string, long>());
        foreach (var plate in job.Parts.SelectMany(p => p.Plates)) plate.Copies = copies;
        return job;
    }

    [Fact]
    public void Matches_workbook_job_CS001()
    {
        // CS-001 in the workbook: Wire_Brush_Insert plate printed twice, electricity at $0.196/kWh.
        var filaments = CrealityFilaments();
        var job = JobFrom("Wire_Brush_Insert.gcode.3mf", filaments, copies: 2);
        var settings = new AppSettings { ElectricityPerKwh = 0.196m };

        var quote = PricingCalculator.Calculate(job, settings, filaments.ToDictionary(f => f.Id));

        Assert.Empty(quote.Warnings);
        var part = Assert.Single(quote.Parts);
        Assert.Equal(70, part.Quantity);
        var plate = Assert.Single(part.Plates);

        // Per-slot material, from Invoice!G18:G22.
        var material = plate.Slots.Select(s => Math.Round(s.Material, 6)).ToArray();
        Assert.Equal(new[] { 0.431964m, 0.085676m, 0.082341m, 0.062150m, 3.788529m }, material);
        Assert.Equal(34.0556m, Math.Round(plate.Machine, 4));       // Invoice!H18
        Assert.Equal(0.53399m, Math.Round(plate.Electricity, 5));   // Invoice!I18
        Assert.Equal(39.0402m, Math.Round(part.PrintCost, 4));

        // Invoice line: 70 x $0.56, plus one $10 slicing fee.
        Assert.Equal(0.56m, part.UnitPrice);
        Assert.Equal(39.20m, part.LineAmount);
        Assert.Equal(1, quote.SlicingCount);
        Assert.Equal(49.20m, quote.Total);
    }

    [Fact]
    public void Plan_example_tube_cap_two_plates()
    {
        var filaments = new List<Filament> { new() { Id = 1, Vendor = "Any", Material = "PLA", SpoolPrice = 25m } };
        var job = JobFrom("Cylindex_Tube_Cap.gcode.3mf", filaments, copies: 2);

        var quote = PricingCalculator.Calculate(job, new AppSettings(), filaments.ToDictionary(f => f.Id));

        var part = Assert.Single(quote.Parts);
        Assert.Equal(40, part.Quantity);
        Assert.Equal(20.03m, PricingCalculator.Money(part.Plates[0].Total / 2)); // one print of the plate
        Assert.Equal(1.00m, part.UnitPrice);
        Assert.Equal(50.00m, quote.Total);
    }

    [Fact]
    public void Customer_supplied_filament_has_no_material_waste_or_markup()
    {
        var filaments = CrealityFilaments();
        var job = JobFrom("Wire_Brush_Insert.gcode.3mf", filaments, copies: 1);
        foreach (var slot in job.Parts[0].Plates[0].Filaments) slot.CustomerSupplied = true;

        var quote = PricingCalculator.Calculate(job, new AppSettings(), filaments.ToDictionary(f => f.Id));

        var plate = quote.Parts[0].Plates[0];
        Assert.Equal(0m, plate.Material);
        Assert.True(plate.Machine > 0);
        Assert.Empty(quote.Warnings);
    }

    [Fact]
    public void Slicing_fee_is_once_per_part_across_plates_and_files()
    {
        var filaments = CrealityFilaments();
        var job = new Job();
        var file = BambuFileReader.Read(Fixture("Wire_Brush_Insert.gcode.3mf"));
        JobBuilder.AddFile(job, file, filaments, new Dictionary<string, long>());
        JobBuilder.AddFile(job, file, filaments, new Dictionary<string, long>());
        JobBuilder.AddFile(job, BambuFileReader.Read(Fixture("Cylindex_Tube_Cap.gcode.3mf")), filaments, new Dictionary<string, long>());

        var quote = PricingCalculator.Calculate(job, new AppSettings(), filaments.ToDictionary(f => f.Id));

        Assert.Equal(2, job.Parts.Count);
        Assert.Equal(2, job.Parts[0].Plates.Count);
        Assert.Equal(70, job.Parts[0].Quantity);
        Assert.Equal(2, quote.SlicingCount);
        Assert.Equal(20m, quote.SlicingAmount);
    }

    [Fact]
    public void Modeling_and_post_processing_only_when_set()
    {
        var empty = PricingCalculator.Calculate(new Job(), new AppSettings(), new Dictionary<long, Filament>());
        Assert.Equal(0m, empty.Total);

        var job = new Job { ModelingHours = 1.5m, PostProcessing = true };
        var quote = PricingCalculator.Calculate(job, new AppSettings(), new Dictionary<long, Filament>());
        Assert.Equal(75m, quote.ModelingAmount);
        Assert.Equal(20m, quote.PostProcessingAmount);
        Assert.Equal(95m, quote.Total);
    }

    [Fact]
    public void Overrides_replace_sliced_values()
    {
        var filaments = CrealityFilaments();
        var job = JobFrom("Wire_Brush_Insert.gcode.3mf", filaments, copies: 1);
        var plate = job.Parts[0].Plates[0];
        plate.OverrideSeconds = 3600;
        foreach (var slot in plate.Filaments) slot.OverrideGrams = 0;
        plate.Filaments[4].OverrideGrams = 100;

        var quote = PricingCalculator.Calculate(job, new AppSettings(), filaments.ToDictionary(f => f.Id));

        var pq = quote.Parts[0].Plates[0];
        Assert.Equal(2.50m, pq.Machine);
        Assert.Equal(0.04m, pq.Electricity);
        Assert.Equal(100m * 0.01499m * 1.1025m, pq.Material);
    }

    [Fact]
    public void Unassigned_slot_warns()
    {
        var job = JobFrom("Wire_Brush_Insert.gcode.3mf", new List<Filament>(), copies: 1);
        var quote = PricingCalculator.Calculate(job, new AppSettings(), new Dictionary<long, Filament>());
        Assert.Equal(5, quote.Warnings.Count);
    }
}

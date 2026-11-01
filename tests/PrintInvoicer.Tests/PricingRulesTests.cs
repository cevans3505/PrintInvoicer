using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using PrintInvoicer.Core.Data;
using PrintInvoicer.Core.Models;
using PrintInvoicer.Core.Pricing;

namespace PrintInvoicer.Tests;

/// <summary>Sub-cent per-piece prices, extra costs on customer-supplied material, and rules versions for frozen invoices.</summary>
public sealed class PricingRulesTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pi-rules-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    private static readonly Printer Fdm = new() { Id = 1, Name = "FDM", Technology = PrintTechnology.Fdm, MachineRatePerHour = 1m };

    /// <summary>One part: a plate printing <paramref name="pieces"/> pieces in <paramref name="seconds"/>, at $1/h and no power cost.</summary>
    private static Job BulkJob(int pieces, int seconds) => new()
    {
        Parts = { new JobPart { Name = "Spacer", ChargeSlicing = false,
            Plates = { new JobPlate { PrinterId = 1, PartsPerPlate = pieces, SlicedSeconds = seconds } } } },
    };

    private static PriceBook Book(int rules = PricingRules.Current, params ExtraCost[] extras) =>
        new(new AppSettings { ElectricityPerKwh = 0m }, printers: [Fdm], extraCosts: extras) { RulesVersion = rules };

    [Fact]
    public void Cheap_parts_in_bulk_keep_four_decimals()
    {
        // 1,000 pieces costing $4.00 in all: cents would make it $0.00 each.
        var quote = PricingCalculator.Calculate(BulkJob(1000, 14400), Book());
        var part = Assert.Single(quote.Parts);
        Assert.Equal(0.004m, part.UnitPrice);
        Assert.Equal(4.00m, part.LineAmount);
        Assert.DoesNotContain(quote.Warnings, w => w.Contains("bills $0.00"));
    }

    [Fact]
    public void Ordinary_parts_keep_cents()
    {
        // 70 pieces costing $39.04: cents give 70 x $0.56 = $39.20, within $0.50, so the invoice stays in cents.
        var quote = PricingCalculator.Calculate(BulkJob(70, (int)(39.04m * 3600)), Book());
        Assert.Equal(0.56m, quote.Parts[0].UnitPrice);
        Assert.Equal(39.20m, quote.Parts[0].LineAmount);
    }

    [Fact]
    public void Older_rules_round_to_cents_and_warn_when_a_part_bills_nothing()
    {
        var quote = PricingCalculator.Calculate(BulkJob(1000, 14400), Book(rules: 1));
        Assert.Equal(0m, quote.Parts[0].UnitPrice);
        Assert.Equal(0m, quote.Parts[0].LineAmount);
        Assert.Contains(quote.Warnings, w => w.Contains("bills $0.00"));
    }

    private static Job MixedSupplyJob() => new()
    {
        Parts = { new JobPart { Name = "Clip", ChargeSlicing = false, Plates = { new JobPlate {
            PrinterId = 1, SlicedSeconds = 3600,
            Filaments =
            {
                new PlateFilament { SlotId = 1, SlicedGrams = 100m, CustomerSupplied = true },
                new PlateFilament { SlotId = 2, SlicedGrams = 20m, CustomerSupplied = true },
                new PlateFilament { SlotId = 3, SlicedGrams = 30m, FilamentId = null },
            } } } } },
    };

    [Fact]
    public void Per_gram_costs_can_skip_customer_supplied_material()
    {
        var wear = new ExtraCost { Id = 1, Name = "Nozzle wear", Cost = 0.01m, Basis = CostBasis.PerGramFilament };
        var surcharge = new ExtraCost { Id = 2, Name = "Premium surcharge", Cost = 0.02m, Basis = CostBasis.PerGramFilament, ChargeCustomerMaterial = false };
        var plate = PricingCalculator.Calculate(MixedSupplyJob(), Book(PricingRules.Current, wear, surcharge)).Parts[0].Plates[0];

        Assert.Equal(150m, plate.Extras.Single(e => e.Cost.Id == 1).Quantity);   // every gram
        Assert.Equal(30m, plate.Extras.Single(e => e.Cost.Id == 2).Quantity);    // only the 30 g that is not the customer's

        // Invoices frozen under the old rules count all material, as they were sent.
        var old = PricingCalculator.Calculate(MixedSupplyJob(), Book(1, wear, surcharge)).Parts[0].Plates[0];
        Assert.Equal(150m, old.Extras.Single(e => e.Cost.Id == 2).Quantity);
    }

    [Fact]
    public void Per_ml_costs_can_skip_customer_supplied_resin()
    {
        var resinPrinter = new Printer { Id = 2, Technology = PrintTechnology.Resin, MachineRatePerHour = 1m };
        var disposal = new ExtraCost { Id = 3, Name = "Resin surcharge", Cost = 0.05m, Basis = CostBasis.PerMlResin, ChargeCustomerMaterial = false };
        var job = new Job { Parts = { new JobPart { Name = "Mini", Plates = { new JobPlate {
            PrinterId = 2, Technology = PrintTechnology.Resin, SlicedSeconds = 3600,
            Resin = new PlateResin { SlicedMl = 40m, CustomerSupplied = true } } } } } };
        var book = new PriceBook(new AppSettings(), printers: [resinPrinter], extraCosts: [disposal]);

        Assert.Empty(PricingCalculator.Calculate(job, book).Parts[0].Plates[0].Extras);
        job.Parts[0].Plates[0].Resin!.CustomerSupplied = false;
        Assert.Equal(2.00m, PricingCalculator.Calculate(job, book).Parts[0].Plates[0].Extras.Single().Amount);
    }

    [Fact]
    public void Charge_on_customer_material_round_trips()
    {
        var db = new Database(_path);
        db.SaveExtraCost(new ExtraCost { Name = "Surcharge", Cost = 0.02m, Basis = CostBasis.PerGramFilament, ChargeCustomerMaterial = false });
        db.SaveExtraCost(new ExtraCost { Name = "Wear", Cost = 0.01m, Basis = CostBasis.PerGramFilament });
        var all = db.GetExtraCosts();
        Assert.False(all.Single(e => e.Name == "Surcharge").ChargeCustomerMaterial);
        Assert.True(all.Single(e => e.Name == "Wear").ChargeCustomerMaterial);
    }

    [Fact]
    public void Discount_comes_off_the_whole_invoice()
    {
        // 10 pieces, 10 h at $1/h = $10.00, plus a $10 slicing fee: $20.00 at full price.
        var job = BulkJob(10, 36000);
        job.Parts[0].ChargeSlicing = true;
        job.DiscountPercent = 15m;
        var quote = PricingCalculator.Calculate(job, Book());
        Assert.Equal(20.00m, quote.Subtotal);
        Assert.Equal(3.00m, quote.DiscountAmount);
        Assert.Equal(17.00m, quote.Total);

        job.DiscountPercent = 12.5m;   // $2.50 exactly; a fraction of a cent rounds to the cent
        Assert.Equal(2.50m, PricingCalculator.Calculate(job, Book()).DiscountAmount);
        job.DiscountPercent = 33.333m;
        Assert.Equal(6.67m, PricingCalculator.Calculate(job, Book()).DiscountAmount);

        job.DiscountPercent = 150m;    // never more than the whole invoice
        Assert.Equal(0m, PricingCalculator.Calculate(job, Book()).Total);
        job.DiscountPercent = 0m;
        Assert.Equal(20.00m, PricingCalculator.Calculate(job, Book()).Total);
    }

    [Fact]
    public void Customer_material_is_valued_at_our_rates_but_not_billed()
    {
        var pla = new Filament { Id = 9, Vendor = "Generic", Material = "PLA", SpoolPrice = 20m, SpoolGrams = 1000m };
        var printer = new Printer { Id = 1, Technology = PrintTechnology.Fdm, WasteAllowance = 0.05m, MaterialMarkup = 0.05m };
        var job = new Job { Parts = { new JobPart { Name = "Clip", ChargeSlicing = false, Plates = { new JobPlate {
            PrinterId = 1, Copies = 2,
            Filaments =
            {
                new PlateFilament { SlotId = 1, SlicedGrams = 100m, FilamentId = 9, CustomerSupplied = true },
                new PlateFilament { SlotId = 2, SlicedGrams = 10m, FilamentId = 9 },
            } } } } } };
        var quote = PricingCalculator.Calculate(job, new PriceBook(new AppSettings(), filaments: [pla], printers: [printer]));

        // 100 g x $0.02/g x 1.1025 x 2 prints = $4.41, not billed; the 10 g of ours is billed.
        Assert.Equal(4.41m, quote.CustomerMaterialValue);
        Assert.Equal(PricingCalculator.Money(10m * 0.02m * 1.1025m * 2), quote.Total);
        Assert.False(quote.HasUnvaluedCustomerMaterial);

        job.Parts[0].Plates[0].Filaments[0].FilamentId = null;   // supplied, but no filament picked
        var unpicked = PricingCalculator.Calculate(job, new PriceBook(new AppSettings(), filaments: [pla], printers: [printer]));
        Assert.Equal(0m, unpicked.CustomerMaterialValue);
        Assert.True(unpicked.HasUnvaluedCustomerMaterial);
    }

    [Fact]
    public void Discount_is_saved_and_frozen_with_the_invoice()
    {
        var db = new Database(_path);
        db.SavePrinter(new Printer { Name = "FDM", Technology = PrintTechnology.Fdm, MachineRatePerHour = 1m });
        var job = BulkJob(10, 36000);
        job.Parts[0].Plates[0].PrinterId = db.GetPrinters().Single().Id;
        job.DiscountPercent = 20m;
        db.SaveJob(job);
        var saved = db.GetJobs().Single();
        Assert.Equal(20m, saved.DiscountPercent);

        saved.FinalizeInvoice(new DateTime(2026, 11, 1), redate: false);
        PriceSnapshot.Freeze(saved, db.LoadPriceBook());
        Assert.Equal(8.00m, saved.FinalizedTotal);   // $10.00 less 20%
    }

    [Fact]
    public void Material_with_no_price_warns()
    {
        var free = new Filament { Id = 9, Vendor = "Generic", Material = "PLA", SpoolPrice = 0m };
        var job = new Job { Parts = { new JobPart { Name = "Clip", Plates = { new JobPlate {
            PrinterId = 1, SlicedSeconds = 3600, Filaments = { new PlateFilament { SlotId = 1, SlicedGrams = 50m, FilamentId = 9 } } } } } } };
        var book = new PriceBook(new AppSettings(), filaments: [free], printers: [Fdm]);
        Assert.Contains(PricingCalculator.Calculate(job, book).Warnings, w => w.Contains("has no price"));

        // The customer's own material is meant to cost nothing.
        job.Parts[0].Plates[0].Filaments[0].CustomerSupplied = true;
        Assert.DoesNotContain(PricingCalculator.Calculate(job, book).Warnings, w => w.Contains("has no price"));

        var resinPrinter = new Printer { Id = 2, Technology = PrintTechnology.Resin };
        var sizeless = new Resin { Id = 3, Name = "Grey", BottlePrice = 30m, BottleSize = 0m };
        var resinJob = new Job { Parts = { new JobPart { Name = "Mini", Plates = { new JobPlate {
            PrinterId = 2, Technology = PrintTechnology.Resin, Resin = new PlateResin { SlicedMl = 10m, ResinId = 3 } } } } } };
        var resinBook = new PriceBook(new AppSettings(), resins: [sizeless], printers: [resinPrinter]);
        Assert.Contains(PricingCalculator.Calculate(resinJob, resinBook).Warnings, w => w.Contains("Grey has no price"));
    }

    [Fact]
    public void A_damaged_setting_names_itself_instead_of_guessing()
    {
        var db = new Database(_path);
        using (var c = new SqliteConnection($"Data Source={_path};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO settings(key, value) VALUES ('ElectricityPerKwh', 'abc')";
            cmd.ExecuteNonQuery();
        }
        var ex = Assert.Throws<InvalidDataException>(() => db.LoadSettings());
        Assert.Contains("ElectricityPerKwh", ex.Message);
        Assert.Contains("abc", ex.Message);
    }

    [Fact]
    public void Frozen_invoices_keep_the_rules_they_were_priced_under()
    {
        var job = BulkJob(1000, 14400);
        job.FinalizeInvoice(new DateTime(2026, 10, 30), redate: false);

        // Frozen today: records the current rules.
        PriceSnapshot.Freeze(job, Book());
        Assert.Equal(PricingRules.Current, JsonNode.Parse(job.PriceSnapshot!)!["RulesVersion"]!.GetValue<int>());
        Assert.Equal(4.00m, job.FinalizedTotal);

        // Frozen by 3.4, whose snapshots have no rules version: still totals the 3.4 way ($0.00), with no mismatch.
        var node = JsonNode.Parse(job.PriceSnapshot!)!.AsObject();
        node.Remove("RulesVersion");
        job.PriceSnapshot = node.ToJsonString();
        job.FinalizedTotal = 0m;
        var book = PriceSnapshot.For(job, Book());
        Assert.Equal(1, book.RulesVersion);
        var quote = PricingCalculator.Calculate(job, book);
        Assert.Equal(0m, quote.Total);
        Assert.DoesNotContain(quote.Warnings, w => w.Contains("was finalized at"));
    }
}

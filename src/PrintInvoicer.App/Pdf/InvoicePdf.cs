using System.IO;
using PrintInvoicer.Core.Models;
using PrintInvoicer.Core.Pricing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PrintInvoicer.App.Pdf;

/// <summary>Preview: as it stands, dated "not finalized", kept out of the invoice folder. Final: the copy to send.</summary>
public enum InvoiceKind { Preview, Final, Internal }

/// <summary>
/// Builds the customer invoice and the internal detail sheet. Both list every plate with its material, hours and rates.
/// The customer copy never mentions waste or markup: its $/kg or $/L is the rate after both, so amount x rate still
/// equals the material charge. The internal copy shows raw material prices, the actual cost before rounding, and the
/// rates of each printer used.
/// </summary>

// Builds every PDF: the customer invoice (CustomerBody), the internal detail sheet (Body) and previews. Each page
// is laid out with QuestPDF from the job and its quote; nothing is calculated here except display formatting.
public static class InvoicePdf
{
    private const string Accent = "#1F6FB2";
    private const string Muted = "#666666";
    private const string Rule = "#D8DDE3";
    private const string FailureText = "#A04000";

    /// <summary>Where PDFs go when no folder is set in Settings: Documents\PrintInvoicer\Invoices.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PrintInvoicer", "Invoices");

    /// <summary>Where previews go: a temporary folder, so a preview can't be mistaken for an invoice to send.</summary>
    public static string PreviewFolder => Path.Combine(Path.GetTempPath(), "PrintInvoicer previews");

    /// <summary>Writes the PDF, replacing any earlier copy of the same kind for this job, and returns its path.</summary>
    public static string Write(Job job, JobQuote quote, PriceBook book, InvoiceKind kind)
    {
        var s = book.Settings;
        string folder = kind == InvoiceKind.Preview ? PreviewFolder
            : string.IsNullOrWhiteSpace(s.InvoiceFolder) ? DefaultFolder : s.InvoiceFolder;
        Directory.CreateDirectory(folder);

        string number = job.InvoiceNumber ?? $"draft {job.CreatedDate:yyyy-MM-dd}";
        string name = kind switch
        {
            InvoiceKind.Preview => $"Preview {number} {Safe(job.Title)}.pdf",
            InvoiceKind.Internal => $"Invoice {number} {Safe(job.Title)} (detail).pdf",
            _ => $"Invoice {number} {Safe(job.Title)}.pdf",
        };
        string path = Path.Combine(folder, name);

        // AUDIT: file write. Saves the PDF to the invoice folder (or, for previews, the temporary previews folder),
        // replacing an older copy of the same invoice.
        Build(job, quote, book, kind).GeneratePdf(path);
        return path;
    }

    /// <summary>Renders each page as a PNG for printing, from the same layout as the PDF.</summary>
    public static IReadOnlyList<byte[]> RenderPages(Job job, JobQuote quote, PriceBook book, InvoiceKind kind, int dpi) =>
        Build(job, quote, book, kind)
            .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = dpi })
            .ToList();

    // The page: Letter size, the header, the body for this kind of PDF, and the footer with page numbers.
    private static Document Build(Job job, JobQuote quote, PriceBook book, InvoiceKind kind) =>
        Document.Create(doc => doc.Page(page =>
        {
            var s = book.Settings;
            Filament? filament(long? id) => book.Filament(id);
            bool detailed = kind == InvoiceKind.Internal;
            page.Size(PageSizes.Letter);
            page.Margin(48);
            // Lato ships inside QuestPDF, so output does not depend on installed system fonts.
            page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Lato"));

            page.Header().Element(c => Header(c, job, s, kind));
            page.Content().PaddingTop(18).Element(c =>
            {
                if (detailed) Body(c, job, quote, s, filament, detailed);
                else CustomerBody(c, job, quote, s, filament);
            });
            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
                if (detailed) t.Span("Internal cost breakdown, not for the customer.   ");
                if (kind == InvoiceKind.Preview) t.Span("Preview only, not a final invoice.   ");
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        }));

    // Your name and contact line, who it is billed to, and the invoice number, date, terms and paid stamp.
    private static void Header(IContainer c, Job job, AppSettings s, InvoiceKind kind)
    {
        c.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(s.BusinessName).FontSize(20).Bold();
                col.Item().Text(s.ContactLine).FontColor(Muted);
                col.Item().PaddingTop(16).Text("Bill to").FontSize(8).FontColor(Muted);
                col.Item().Text(job.Customer).FontSize(12).SemiBold();
            });

            row.ConstantItem(240).AlignRight().Column(col =>
            {
                string title = kind switch { InvoiceKind.Internal => "JOB DETAIL", InvoiceKind.Preview => "INVOICE PREVIEW", _ => "INVOICE" };
                col.Item().AlignRight().Text(title).FontSize(20).Bold().FontColor(kind == InvoiceKind.Preview ? Muted : Accent);
                col.Item().PaddingTop(4).AlignRight().Text(t =>
                {
                    t.Span("Invoice # ").FontColor(Muted);
                    t.Span(job.InvoiceNumber ?? "DRAFT").SemiBold();
                });
                col.Item().AlignRight().Text(t =>
                {
                    t.Span("Date ").FontColor(Muted);
                    if (job.Finalized && job.InvoiceDate is DateTime d) t.Span(d.ToString("MMMM d, yyyy"));
                    else t.Span("not finalized").Italic();
                });
                if (!string.IsNullOrWhiteSpace(s.PaymentTerms))
                {
                    col.Item().AlignRight().Text(t =>
                    {
                        t.Span("Terms ").FontColor(Muted);
                        t.Span(s.PaymentTerms);
                    });
                }
                if (job.Paid)
                    col.Item().PaddingTop(6).AlignRight().Text($"PAID {job.PaidDate:MMM d, yyyy}").FontSize(12).Bold().FontColor("#2E7D32");
            });
        });
    }

    // The internal detail sheet: every plate with its material table, hours, rates and extra costs, then a summary.
    private static void Body(IContainer c, Job job, JobQuote q, AppSettings s, Func<long?, Filament?> filament, bool detailed)
    {
        c.Column(col =>
        {
            if (!string.IsNullOrWhiteSpace(job.Title))
                col.Item().Text(job.Title).FontSize(12).SemiBold();

            foreach (var printer in PrintersUsed(q))
                col.Item().Text(
                        $"{printer.Name}: machine time {Format.Money(printer.MachineRatePerHour)}/hr, {printer.Watts:0} W, " +
                        $"waste {printer.WasteAllowance:P0} and markup {printer.MaterialMarkup:P0} on own material.")
                    .FontColor(Muted).FontSize(9);
            col.Item().PaddingBottom(8).Text($"Electricity {Format.Money(s.ElectricityPerKwh)}/kWh.").FontColor(Muted).FontSize(9);

            foreach (var p in q.Parts)
            {
                col.Item().PaddingTop(10).BorderBottom(1).BorderColor(Rule).PaddingBottom(3).Row(r =>
                {
                    r.RelativeItem().Text(PrettyName(p.Part.Name)).FontSize(12).SemiBold();
                    r.AutoItem().Text(detailed
                        ? $"{p.Quantity} pcs, cost {Format.Money(p.PrintCost)}, billed {p.Quantity} x {Format.Rate(p.UnitPrice)} = {Format.Money(p.LineAmount)}"
                        : $"{p.Quantity} pcs x {Format.Rate(p.UnitPrice)} = {Format.Money(p.LineAmount)}").SemiBold();
                });

                foreach (var pl in p.Plates)
                {
                    var plate = pl.Plate;
                    col.Item().PaddingTop(6).Text(t =>
                    {
                        string source = plate.IsManual ? "Entered by hand"
                            : plate.Technology == PrintTechnology.Resin ? Path.GetFileName(plate.SourceFile)
                            : $"{Path.GetFileName(plate.SourceFile)}, plate {plate.PlateIndex}";
                        t.Span(source).SemiBold();
                        t.Span($"   {pl.Printer.Name}, {plate.PartsPerPlate} parts per plate, printed {plate.Copies}x, {Format.Duration(plate.EffectiveSeconds)} per print");
                        if (plate.LayerCount > 0) t.Span($", {plate.LayerCount:N0} layers at {plate.LayerHeightMm:0.###} mm");
                        if (plate.OverrideSeconds.HasValue)
                            t.Span($" (overridden, sliced {Format.Duration(plate.SlicedSeconds)})").FontColor(Muted);
                    });

                    if (pl.Resin is ResinQuote rq)
                    {
                        col.Item().PaddingLeft(10).Table(t =>
                        {
                            t.ColumnsDefinition(cd =>
                            {
                                cd.RelativeColumn(4.4f);
                                cd.RelativeColumn(1.4f);
                                cd.RelativeColumn(1.4f);
                                cd.RelativeColumn(1.6f);
                            });
                            t.Header(h =>
                            {
                                HeadCell(h.Cell(), "Resin");
                                HeadCell(h.Cell().AlignRight(), "mL per print");
                                HeadCell(h.Cell().AlignRight(), "$ per L");
                                HeadCell(h.Cell().AlignRight(), "Material, all prints");
                            });
                            string label = ResinLabel(rq, detailed);
                            SmallCell(t.Cell(), label);
                            SmallCell(t.Cell().AlignRight(), $"{rq.Ml:0.##}" + (rq.Plate.OverrideMl.HasValue ? "*" : ""));
                            SmallCell(t.Cell().AlignRight(), rq.Plate.CustomerSupplied || rq.CostPerMl == 0 ? "" : Format.Money(rq.CostPerMl * 1000));
                            SmallCell(t.Cell().AlignRight(), Format.Money(rq.Material));
                        });
                    }
                    else col.Item().PaddingLeft(10).Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(34);
                            cd.RelativeColumn(4);
                            cd.RelativeColumn(1.4f);
                            cd.RelativeColumn(1.4f);
                            cd.RelativeColumn(1.6f);
                        });
                        t.Header(h =>
                        {
                            HeadCell(h.Cell(), "Slot");
                            HeadCell(h.Cell(), "Filament");
                            HeadCell(h.Cell().AlignRight(), "g per print");
                            HeadCell(h.Cell().AlignRight(), "$ per kg");
                            HeadCell(h.Cell().AlignRight(), "Material, all prints");
                        });
                        foreach (var slot in pl.Slots)
                        {
                            var f = slot.Filament ?? filament(slot.Slot.FilamentId);
                            string label = slot.Slot.CustomerSupplied ? $"{f?.DisplayName ?? slot.Slot.SlicerProfile} (customer supplied)"
                                : f?.DisplayName ?? (detailed ? $"{slot.Slot.SlicerProfile} (no filament selected)" : slot.Slot.SlicerProfile);
                            string grams = $"{slot.Grams:0.##}" + (slot.Slot.OverrideGrams.HasValue ? "*" : "");
                            decimal perKg = slot.CostPerGram * 1000 * (detailed ? 1 : pl.Printer.MaterialFactor);
                            SmallCell(t.Cell(), slot.Slot.SlotId.ToString());
                            SmallCell(t.Cell(), label);
                            SmallCell(t.Cell().AlignRight(), grams);
                            SmallCell(t.Cell().AlignRight(), slot.Slot.CustomerSupplied || slot.CostPerGram == 0 ? "" : Format.Money(perKg));
                            SmallCell(t.Cell().AlignRight(), Format.Money(slot.Material));
                        }
                    });

                    decimal kwh = pl.Hours * pl.Printer.Watts / 1000m * plate.Copies;
                    string extras = string.Concat(pl.Extras.Select(e => $"{e.Cost.Name} {Format.Money(e.Amount)}   "));
                    col.Item().PaddingLeft(10).PaddingTop(2).Text(
                            $"Machine {pl.Hours * plate.Copies:0.##} h = {Format.Money(pl.Machine)}   Electricity {kwh:0.##} kWh = {Format.Money(pl.Electricity)}   " +
                            extras + $"Plate total {Format.Money(pl.Total)}")
                        .FontSize(9);

                    if (plate.HasFailures && plate.FailedCount > 0)
                    {
                        string note = string.IsNullOrWhiteSpace(plate.FailureNote) ? "" : $" {plate.FailureNote.Trim().TrimEnd('.')}.";
                        col.Item().PaddingLeft(10).PaddingTop(2).Text(
                                $"Print failures: {plate.FailedCount} of {plate.TotalObjects} objects did not complete.{note}")
                            .FontSize(9).FontColor(FailureText);
                    }
                }
            }

            decimal rounding = q.PartsAmount - q.Parts.Sum(p => p.PrintCost);

            col.Item().PaddingTop(16).Row(bottom =>
            {
                bottom.RelativeItem().PaddingRight(20).Element(c => PlatePictures(c, q, captions: true));

                bottom.ConstantItem(290).Column(sum =>
                {
                    void Row(string label, decimal amount, string? note = null, bool bold = false)
                    {
                        sum.Item().Row(r =>
                        {
                            r.RelativeItem().Text(t =>
                            {
                                var main = t.Span(label);
                                if (bold) main.Bold().FontSize(13);
                                if (note != null) t.Span($"  {note}").FontSize(8).FontColor(Muted);
                            });
                            var a = r.AutoItem().AlignRight().Text(Format.Money(amount));
                            if (bold) a.Bold().FontSize(13);
                        });
                    }

                    Row("Material", q.Parts.Sum(p => p.Material));
                    Row("Machine time", q.Parts.Sum(p => p.Machine));
                    Row("Electricity", q.Parts.Sum(p => p.Electricity));
                    if (q.Parts.Any(p => p.Extras > 0))
                        Row("Extra costs on prints", q.Parts.Sum(p => p.Extras));
                    if (detailed || Math.Round(rounding, 2) != 0)
                        Row(detailed ? "Rounding to unit prices" : "Rounding to per-piece prices", rounding);
                    if (detailed || q.SlicingCount > 0)
                        Row("Slicing and print setup", q.SlicingAmount, $"{q.SlicingCount} part(s) x {Format.Money(s.SlicingFeePerPart)}");
                    if (detailed || q.ModelingAmount > 0)
                        Row("3D modeling", q.ModelingAmount, $"{q.ModelingHours:0.##} h x {Format.Money(s.ModelingRatePerHour)}");
                    if (detailed || q.PostProcessingAmount > 0)
                        Row("Post-processing", q.PostProcessingAmount);
                    foreach (var e in q.JobExtras)
                        Row(e.Cost.Name, e.Amount, "per job");
                    if (q.DiscountAmount > 0)
                    {
                        sum.Item().PaddingVertical(4).LineHorizontal(1).LineColor(Rule);
                        Row("Subtotal (full price)", q.Subtotal);
                        Row($"{s.DiscountLabel} ({q.DiscountPercent:0.##}%)", -q.DiscountAmount);
                    }
                    sum.Item().PaddingVertical(4).LineHorizontal(1).LineColor(Rule);
                    Row(detailed ? "Total invoiced" : "Total", q.Total, bold: true);
                    if (q.CustomerMaterialValue > 0)
                        Row("Customer's material at our rates", q.CustomerMaterialValue, "not billed");
                });
            });

            if (HasMeasuredMaterial(job))
                col.Item().PaddingTop(8).Text(detailed ? "* grams or mL entered by hand instead of the slicer estimate" : "* measured")
                    .FontSize(8).FontColor(Muted);

            if (!detailed)
            {
                if (!string.IsNullOrWhiteSpace(job.Notes))
                    col.Item().PaddingTop(20).Text(job.Notes).FontColor(Muted);
                col.Item().PaddingTop(24).Text("Thank you!").FontColor(Muted).Italic();
            }
        });
    }

    /// <summary>
    /// Customer invoice: one table where every charge reads as quantity x rate = amount. Waste and markup are folded
    /// into the filament rate. Each part ends in a subtotal at its per-piece price; if rounding to the cent means the
    /// lines do not add up to that subtotal exactly, a rounding line makes up the difference.
    /// </summary>
    private static void CustomerBody(IContainer c, Job job, JobQuote q, AppSettings s, Func<long?, Filament?> filament)
    {
        var notes = new List<string>();
        bool severalPrinters = PrintersUsed(q).Count() > 1;

        c.Column(col =>
        {
            if (!string.IsNullOrWhiteSpace(job.Title))
                col.Item().PaddingBottom(6).Text(job.Title).FontSize(13).SemiBold();

            col.Item().Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(4.6f);
                    cd.RelativeColumn(1.5f);
                    cd.RelativeColumn(1.7f);
                    cd.RelativeColumn(1.4f);
                });
                t.Header(h =>
                {
                    HeadCell(h.Cell(), "Description");
                    HeadCell(h.Cell().AlignRight(), "Qty");
                    HeadCell(h.Cell().AlignRight(), "Rate");
                    HeadCell(h.Cell().AlignRight(), "Amount");
                });

                foreach (var p in q.Parts)
                {
                    t.Cell().ColumnSpan(4).PaddingTop(8).PaddingBottom(1).Column(head =>
                    {
                        head.Item().Text(PrettyName(p.Part.Name)).FontSize(11).SemiBold();
                        head.Item().Text(PlateSummary(p)).FontSize(8.5f).FontColor(Muted);
                    });

                    // Lines are rounded as shown, so the rounding line is measured against what the reader adds up.
                    var lines = new List<(string Label, string Qty, string Rate, decimal Amount)>();
                    foreach (var byPrinter in p.Plates.GroupBy(pl => pl.Printer.Id))
                    {
                        var printer = byPrinter.First().Printer;
                        decimal hours = byPrinter.Sum(pl => pl.Hours * pl.Plate.Copies);
                        decimal kwh = hours * printer.Watts / 1000m;
                        lines.Add((severalPrinters ? $"Machine time, {printer.Name}" : "Machine time", $"{hours:0.##} h",
                            $"{Format.Money(printer.MachineRatePerHour)} / h", PricingCalculator.Money(byPrinter.Sum(pl => pl.Machine))));
                        lines.Add(($"Electricity, {printer.Watts:0} W printer", $"{kwh:0.00} kWh", $"{Format.Money(s.ElectricityPerKwh)} / kWh",
                            PricingCalculator.Money(byPrinter.Sum(pl => pl.Electricity))));
                    }

                    // Filament grouped by spool, at the rate after waste and markup on its printer.
                    var materials = p.Plates
                        .SelectMany(pl => pl.Slots.Select(sq => (Slot: sq, pl.Plate.Copies, pl.Printer)))
                        .GroupBy(x => (x.Slot.Slot.FilamentId, x.Slot.Slot.CustomerSupplied, x.Printer.Id));
                    foreach (var m in materials)
                    {
                        var first = m.First();
                        var f = first.Slot.Filament ?? filament(first.Slot.Slot.FilamentId);
                        string label = f?.DisplayName ?? first.Slot.Slot.SlicerProfile;
                        decimal grams = m.Sum(x => x.Slot.Grams * x.Copies);
                        string rate = first.Slot.Slot.CustomerSupplied ? "customer supplied"
                            : f is null ? "not set"
                            : $"{Format.Money(first.Slot.CostPerGram * 1000 * first.Printer.MaterialFactor)} / kg";
                        string measured = m.Any(x => x.Slot.Slot.OverrideGrams.HasValue) ? "*" : "";
                        decimal supplied = PricingCalculator.Money(m.Sum(x => x.Slot.SuppliedValue));
                        if (supplied > 0) label += $" (worth {Format.Money(supplied)} at our rates)";
                        lines.Add((label, $"{grams:0.##} g{measured}", rate, PricingCalculator.Money(m.Sum(x => x.Slot.Material))));
                    }

                    // Resin grouped the same way, by volume.
                    var resins = p.Plates
                        .Where(pl => pl.Resin != null)
                        .Select(pl => (Resin: pl.Resin!, pl.Plate.Copies, pl.Printer))
                        .GroupBy(x => (x.Resin.Resin?.Id, x.Resin.Plate.CustomerSupplied, x.Printer.Id));
                    foreach (var m in resins)
                    {
                        var first = m.First();
                        decimal ml = m.Sum(x => x.Resin.Ml * x.Copies);
                        string rate = first.Resin.Plate.CustomerSupplied ? "customer supplied"
                            : first.Resin.Resin is null ? "not set"
                            : $"{Format.Money(first.Resin.CostPerMl * 1000 * first.Printer.MaterialFactor)} / L";
                        string measured = m.Any(x => x.Resin.Plate.OverrideMl.HasValue) ? "*" : "";
                        decimal supplied = PricingCalculator.Money(m.Sum(x => x.Resin.SuppliedValue));
                        string resinLabel = ResinLabel(first.Resin, detailed: false) + (supplied > 0 ? $" (worth {Format.Money(supplied)} at our rates)" : "");
                        lines.Add((resinLabel, $"{ml:0.##} mL{measured}", rate,
                            PricingCalculator.Money(m.Sum(x => x.Resin.Material))));
                    }

                    // Extra costs counted per print, hour or material.
                    foreach (var e in p.Plates.SelectMany(pl => pl.Extras).GroupBy(e => e.Cost.Id))
                    {
                        var extra = e.First().Cost;
                        decimal used = e.Sum(x => x.Quantity);
                        lines.Add((extra.Name, ExtraQuantity(extra.Basis, used), $"{Format.Rate(extra.Cost)} / {ExtraCost.RateUnit(extra.Basis)}",
                            PricingCalculator.Money(e.Sum(x => x.Amount))));
                    }

                    foreach (var line in lines)
                        ItemRow(t, line.Label, line.Qty, line.Rate, line.Amount, indent: true);

                    // Two steps the reader can check: the lines above add up to the cost, and the cost divided by
                    // the pieces gives the per-piece price that is billed.
                    decimal cost = lines.Sum(l => l.Amount);
                    SumRow(t, $"Cost of {Pieces(p.Quantity)}", cost);

                    string division = $"{Format.Money(cost)} ÷ {Pieces(p.Quantity)} = ";
                    if (p.Quantity > 0)
                    {
                        // One more digit than the price shows: 3 for cents, 5 for sub-cent bulk prices.
                        bool cents = Math.Round(p.UnitPrice, 2) == p.UnitPrice;
                        decimal exact = Math.Round(cost / p.Quantity, cents ? 3 : 5, MidpointRounding.AwayFromZero);
                        division += exact == p.UnitPrice
                            ? Format.Rate(p.UnitPrice)
                            : $"${exact.ToString(cents ? "0.000" : "0.00000", System.Globalization.CultureInfo.InvariantCulture)}, rounded to {Format.Rate(p.UnitPrice)}";
                    }
                    PerPieceRow(t, division, p.Quantity, p.UnitPrice, p.LineAmount);

                    foreach (var pl in p.Plates.Where(pl => pl.Plate.HasFailures && pl.Plate.FailedCount > 0))
                    {
                        string where = p.Plates.Count > 1 ? $"{PrettyName(p.Part.Name)}, plate {pl.Plate.PlateIndex}" : PrettyName(p.Part.Name);
                        string detail = string.IsNullOrWhiteSpace(pl.Plate.FailureNote) ? "" : $" {pl.Plate.FailureNote.Trim().TrimEnd('.')}.";
                        notes.Add($"{where}: {pl.Plate.FailedCount} of {pl.Plate.TotalObjects} objects did not complete.{detail}");
                    }
                }

                if (q.SlicingCount > 0)
                    SubtotalRow(t, "Slicing and print setup", q.SlicingCount == 1 ? "1 part" : $"{q.SlicingCount} parts",
                        Format.Money(s.SlicingFeePerPart), q.SlicingAmount, topPadding: 6);
                if (q.ModelingAmount > 0)
                    SubtotalRow(t, "3D modeling", $"{q.ModelingHours:0.##} h", $"{Format.Money(s.ModelingRatePerHour)} / h", q.ModelingAmount, topPadding: 2);
                if (q.PostProcessingAmount > 0)
                    SubtotalRow(t, "Post-processing", "1", Format.Money(q.PostProcessingAmount), q.PostProcessingAmount, topPadding: 2);
                foreach (var e in q.JobExtras)
                    SubtotalRow(t, e.Cost.Name, "1 job", Format.Rate(e.Cost.Cost), e.Amount, topPadding: 2);
            });

            col.Item().PaddingTop(14).Row(bottom =>
            {
                bottom.RelativeItem().PaddingRight(20).Element(x => PlatePictures(x, q, captions: false));
                bottom.ConstantItem(240).AlignMiddle().Column(totals =>
                {
                    if (q.DiscountAmount > 0)
                    {
                        totals.Item().Row(r =>
                        {
                            r.RelativeItem().Text("Subtotal");
                            r.AutoItem().AlignRight().Text(Format.Money(q.Subtotal));
                        });
                        totals.Item().PaddingBottom(4).Row(r =>
                        {
                            r.RelativeItem().Text($"{s.DiscountLabel} ({q.DiscountPercent:0.##}%)");
                            r.AutoItem().AlignRight().Text("-" + Format.Money(q.DiscountAmount));
                        });
                        totals.Item().PaddingBottom(4).LineHorizontal(1).LineColor(Rule);
                    }
                    totals.Item().Row(r =>
                    {
                        r.RelativeItem().Text("Total").FontSize(15).Bold();
                        r.AutoItem().AlignRight().Text(Format.Money(q.Total)).FontSize(15).Bold();
                    });
                });
            });

            // What working together saves the customer: the discount, and the material they supplied at our rates.
            if (q.DiscountAmount > 0 || q.CustomerMaterialValue > 0)
            {
                col.Item().EnsureSpace(90).PaddingTop(14).AlignRight().Width(300).Border(1).BorderColor("#BBBBBB").Padding(10).Column(box =>
                {
                    box.Item().PaddingBottom(4).Text("Your savings on this invoice").SemiBold().FontColor(Accent);
                    void Saving(string label, decimal amount, bool bold = false) => box.Item().Row(r =>
                    {
                        var l = r.RelativeItem().Text(label);
                        var a = r.AutoItem().AlignRight().Text(Format.Money(amount));
                        if (bold) { l.SemiBold(); a.SemiBold(); }
                    });
                    if (q.DiscountAmount > 0) Saving($"{s.DiscountLabel} ({q.DiscountPercent:0.##}%)", q.DiscountAmount);
                    if (q.CustomerMaterialValue > 0) Saving("Material you supplied, at our rates", q.CustomerMaterialValue);
                    if (q.DiscountAmount > 0 && q.CustomerMaterialValue > 0)
                    {
                        box.Item().PaddingVertical(3).LineHorizontal(0.75f).LineColor(Rule);
                        Saving("Total saved", q.DiscountAmount + q.CustomerMaterialValue, bold: true);
                    }
                    if (q.HasUnvaluedCustomerMaterial)
                        box.Item().PaddingTop(4).Text("Some material you supplied is not included.").FontSize(8).FontColor(Muted);
                });
            }

            bool hasNotes = !string.IsNullOrWhiteSpace(job.Notes);
            bool overridden = HasMeasuredMaterial(job);
            if (hasNotes || notes.Count > 0)
            {
                // Move the box to the next page rather than splitting a short note across the break.
                col.Item().EnsureSpace(110).PaddingTop(16).Border(1).BorderColor("#BBBBBB").Padding(10).Column(box =>
                {
                    box.Item().PaddingBottom(4).Text("Notes").SemiBold().FontColor(Accent);
                    if (hasNotes) box.Item().Text(job.Notes.Trim());
                    foreach (var n in notes)
                    {
                        box.Item().PaddingTop(hasNotes ? 6 : 0).Text(t =>
                        {
                            t.Span("Print failures. ").SemiBold().FontColor(FailureText);
                            t.Span(n).FontColor(FailureText);
                        });
                    }
                });
            }
            if (overridden)
                col.Item().PaddingTop(6).Text("Amounts marked * were measured rather than taken from the slicer.").FontSize(8).FontColor(Muted);

            col.Item().PaddingTop(16).Text("Thank you!").FontColor(Muted).Italic();
        });
    }

    /// <summary>"10 pieces: 1 print with 10 per plate, 11 h 29 min per print", one clause per plate.</summary>
    private static string PlateSummary(PartQuote p) =>
        string.Join("; ", p.Plates.Select(pl =>
        {
            var plate = pl.Plate;
            var t = TimeSpan.FromSeconds(plate.EffectiveSeconds);
            string time = (int)t.TotalHours == 0 ? $"{t.Minutes} min"
                : t.Minutes == 0 ? $"{(int)t.TotalHours} h"
                : $"{(int)t.TotalHours} h {t.Minutes} min";
            string prints = plate.Copies == 1 ? "1 print" : $"{plate.Copies} prints";
            return $"{Pieces(plate.TotalObjects)}: {prints} with {plate.PartsPerPlate} per plate, {time} per print";
        }));

    // Small helpers for wording, labels and table cells used by both layouts.
    private static string Pieces(int n) => n == 1 ? "1 piece" : $"{n} pieces";

    private static IEnumerable<Printer> PrintersUsed(JobQuote q) =>
        q.AllPlates.Select(pl => pl.Printer).DistinctBy(p => p.Id);

    private static bool HasMeasuredMaterial(Job job) =>
        job.AllPlates.Any(p => p.Filaments.Any(f => f.OverrideGrams.HasValue) || p.Resin?.OverrideMl is not null);

    private static string ResinLabel(ResinQuote rq, bool detailed)
    {
        string name = rq.Resin?.DisplayName ?? (string.IsNullOrWhiteSpace(rq.Plate.SlicerProfile) ? "Resin" : rq.Plate.SlicerProfile);
        if (rq.Plate.CustomerSupplied) return $"{name} (customer supplied)";
        if (rq.Resin is null && detailed) return $"{name} (no resin selected)";
        return name;
    }

    private static string ExtraQuantity(CostBasis basis, decimal used) => basis switch
    {
        CostBasis.PerPrint => used == 1 ? "1 print" : $"{used:0} prints",
        CostBasis.PerPrintHour => $"{used:0.##} h",
        CostBasis.PerMlResin => $"{used:0.##} mL",
        CostBasis.PerGramFilament => $"{used:0.##} g",
        _ => $"{used:0.##}",
    };

    private static void ItemRow(TableDescriptor t, string label, string qty, string rate, decimal amount, bool indent, bool muted = false)
    {
        IContainer Cell(IContainer x) => x.PaddingVertical(1.5f).DefaultTextStyle(d => d.FontSize(9.5f));
        string color = muted ? Muted : "#1A1A1A";
        t.Cell().Element(Cell).PaddingLeft(indent ? 12 : 0).Text(label).FontColor(color);
        t.Cell().Element(Cell).AlignRight().Text(qty).FontColor(color);
        t.Cell().Element(Cell).AlignRight().Text(rate).FontColor(rate is "customer supplied" or "not set" ? Muted : color);
        t.Cell().Element(Cell).AlignRight().Text(Format.Money(amount)).FontColor(color);
    }

    /// <summary>The sum of the part's lines, ruled off above like a column total.</summary>
    private static void SumRow(TableDescriptor t, string label, decimal amount)
    {
        IContainer Cell(IContainer x) => x.PaddingTop(2).PaddingBottom(3);
        t.Cell().ColumnSpan(3).Element(Cell).PaddingLeft(12).Text(label).FontSize(9.5f).FontColor(Muted);
        t.Cell().Element(Cell).BorderTop(0.75f).BorderColor("#888888").PaddingTop(2).AlignRight().Text(Format.Money(amount)).FontSize(9.5f);
    }

    /// <summary>The billed line: per-piece price with the division that produced it spelled out underneath.</summary>
    private static void PerPieceRow(TableDescriptor t, string division, int qty, decimal unit, decimal amount)
    {
        IContainer Cell(IContainer x) => x.BorderTop(0.5f).BorderBottom(0.5f).BorderColor("#CCCCCC").PaddingVertical(3);
        t.Cell().Element(Cell).PaddingLeft(12).Column(c =>
        {
            c.Item().Text("Price per piece").SemiBold();
            c.Item().Text(division).FontSize(8.5f).FontColor(Muted);
        });
        t.Cell().Element(Cell).AlignRight().AlignMiddle().Text($"{qty} pcs").SemiBold();
        t.Cell().Element(Cell).AlignRight().AlignMiddle().Text($"{Format.Rate(unit)} each").SemiBold();
        t.Cell().Element(Cell).AlignRight().AlignMiddle().Text(Format.Money(amount)).SemiBold();
    }

    private static void SubtotalRow(TableDescriptor t, string label, string qty, string rate, decimal amount, float topPadding = 0)
    {
        IContainer Cell(IContainer x) => x.PaddingTop(topPadding).BorderTop(topPadding > 0 ? 0 : 0.5f).BorderBottom(0.5f).BorderColor("#CCCCCC").PaddingVertical(3);
        t.Cell().Element(Cell).PaddingLeft(topPadding > 0 ? 0 : 12).Text(label).SemiBold();
        t.Cell().Element(Cell).AlignRight().Text(qty).SemiBold();
        t.Cell().Element(Cell).AlignRight().Text(rate).SemiBold();
        t.Cell().Element(Cell).AlignRight().Text(Format.Money(amount)).SemiBold();
    }

    /// <summary>
    /// Plate renders beside the totals; transparent backgrounds keep ink to the parts. The space is about 200 pt wide:
    /// one picture large, or two per row when there are several.
    /// </summary>
    private static void PlatePictures(IContainer c, JobQuote q, bool captions)
    {
        var pictures = q.Parts
            .SelectMany(p => p.Plates.Select(pl => (Caption: $"{PrettyName(p.Part.Name)}, plate {pl.Plate.PlateIndex}", Png: PlateImages.ForInvoice(pl.Plate))))
            .Where(x => x.Png != null)
            .ToList();

        c.Inlined(pics =>
        {
            pics.Spacing(8);
            bool single = pictures.Count == 1;
            foreach (var pic in pictures)
            {
                pics.Item().Width(single ? 160 : 96).Column(col =>
                {
                    col.Item().Height(single ? 120 : 76).AlignCenter().AlignMiddle().Image(pic.Png!).FitArea();
                    if (captions) col.Item().PaddingTop(2).AlignCenter().Text(pic.Caption).FontSize(7).FontColor(Muted);
                });
            }
        });
    }

    private static void HeadCell(IContainer c, string text) =>
        c.BorderBottom(1.5f).BorderColor(Accent).PaddingVertical(4).Text(text).SemiBold().FontColor(Accent);

    private static void SmallCell(IContainer c, string text) =>
        c.BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(2).Text(text).FontSize(9);

    private static string PrettyName(string name) => name.Replace('_', ' ').Trim();

    // A job title made safe to use in a file name.
    private static string Safe(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(s.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
    }
}

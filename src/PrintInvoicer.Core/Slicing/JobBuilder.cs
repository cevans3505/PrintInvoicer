using PrintInvoicer.Core.Models;
using PrintInvoicer.Core.Pricing;

namespace PrintInvoicer.Core.Slicing;

/// <summary>
/// Turns what a file reader found into parts and plates on a job, guesses the material for each slot or vat, and
/// creates plates typed in by hand. Works only on the job in memory; saving is done by the database.
/// </summary>
public static class JobBuilder
{
    /// <summary>
    /// Adds every plate in a sliced file to the job. Plates are grouped into parts by object name (resin files carry no
    /// object names, so the file name is used), so a part split across several plates or files still gets a single
    /// slicing fee. Returns notes worth showing to the user.
    /// </summary>
    /// <param name="printerId">The printer the file was sliced for; see <see cref="ChoosePrinter"/>.</param>
    public static List<string> AddFile(Job job, SlicedFile file, IEnumerable<Filament> filaments, IReadOnlyDictionary<string, long> remembered,
        IEnumerable<Resin>? resins = null, long? printerId = null)
    {
        var notes = new List<string>();
        var filamentList = filaments.ToList();
        var resinList = (resins ?? []).ToList();
        string fileName = Path.GetFileName(file.Path);

        foreach (var sp in file.Plates)
        {
            string name = !string.IsNullOrWhiteSpace(sp.PartName) ? sp.PartName
                : file.Technology == PrintTechnology.Resin ? GooFileReader.PartNameFromFile(file.Path)
                : StripSuffix(fileName);
            if (string.IsNullOrWhiteSpace(name)) name = StripSuffix(fileName);
            if (sp.HasMixedObjects)
                notes.Add($"{fileName} plate {sp.Index} has more than one kind of object; it was added as \"{name}\". Regroup it if needed.");

            var part = job.Parts.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (part == null)
            {
                part = new JobPart { Name = name };
                job.Parts.Add(part);
            }

            var plate = new JobPlate
            {
                SourceFile = file.Path,
                PlateIndex = sp.Index,
                Copies = 1,
                PartsPerPlate = Math.Max(sp.ObjectCount, 1),
                SlicedSeconds = sp.PredictedSeconds,
                Thumbnail = sp.Thumbnail,
                Technology = file.Technology,
                PrinterId = printerId,
                LayerCount = sp.LayerCount,
                LayerHeightMm = sp.LayerHeightMm,
            };

            if (file.Technology == PrintTechnology.Resin)
            {
                string profile = sp.Resin?.Profile ?? "";
                plate.Resin = new PlateResin
                {
                    SlicerProfile = profile,
                    SlicedMl = sp.Resin?.Ml ?? 0,
                    ResinId = ResinMatcher.Match(profile, resinList, remembered)?.Id,
                };
                // The file does not record how many parts are on the plate; the job window asks for it.
                plate.PartsCountConfirmed = false;
            }

            foreach (var sf in sp.Filaments)
            {
                var slot = new PlateFilament
                {
                    SlotId = sf.SlotId,
                    SlicerType = sf.Type,
                    SlicerColorHex = sf.ColorHex,
                    SlicerVendor = sf.Vendor,
                    SlicerProfile = sf.Profile,
                    SlicedGrams = sf.Grams,
                };
                slot.FilamentId = FilamentMatcher.Match(slot, filamentList, remembered)?.Id;
                plate.Filaments.Add(slot);
            }

            part.Plates.Add(plate);
        }
        return notes;
    }

    /// <summary>
    /// The printer for a sliced file: an active printer of the same kind whose name matches the one in the file, else
    /// the first active printer of that kind. Null means none exists yet and the caller should add one.
    /// </summary>
    public static Printer? ChoosePrinter(SlicedFile file, IEnumerable<Printer> printers)
    {
        var sameKind = printers.Where(p => p.Active && p.Technology == file.Technology).OrderBy(p => p.Id).ToList();
        if (file.PrinterModel is string model && !string.IsNullOrWhiteSpace(model))
        {
            var named = sameKind.FirstOrDefault(p => string.Equals(p.Name.Trim(), model.Trim(), StringComparison.OrdinalIgnoreCase))
                     ?? sameKind.FirstOrDefault(p => model.Contains(p.Name.Trim(), StringComparison.OrdinalIgnoreCase) && p.Name.Trim().Length > 2);
            if (named != null) return named;
        }
        return sameKind.FirstOrDefault();
    }

    /// <summary>A plate whose time and material are typed in, for slicers whose files the app cannot read.</summary>
    public static JobPlate ManualPlate(Printer printer)
    {
        var plate = new JobPlate { Technology = printer.Technology, PrinterId = printer.Id, PlateIndex = 1 };
        if (printer.Technology == PrintTechnology.Resin) plate.Resin = new PlateResin();
        else plate.Filaments.Add(new PlateFilament { SlotId = 1 });
        return plate;
    }

    // The file name without its slicer extension, used as a part name when the file names no objects.
    private static string StripSuffix(string fileName)
    {
        foreach (var ext in new[] { ".gcode.3mf", ".3mf", ".gcode", ".goo" })
        {
            if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return fileName[..^ext.Length];
        }
        return fileName;
    }
}

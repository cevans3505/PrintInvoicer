using PrintInvoicer.Core.Models;

namespace PrintInvoicer.Core.Slicing;

public record SlicedFile(string Path, string? PrinterModel, IReadOnlyList<SlicedPlate> Plates)
{
    public PrintTechnology Technology { get; init; } = PrintTechnology.Fdm;
}

public record SlicedPlate(
    int Index,
    int PredictedSeconds,
    IReadOnlyList<string> ObjectNames,
    IReadOnlyList<SlicedFilament> Filaments,
    byte[]? Thumbnail)
{
    public int ObjectCount => ObjectNames.Count;

    public decimal TotalGrams => Filaments.Sum(f => f.Grams);

    /// <summary>Most common object name with the mesh extension removed, used as the part name.</summary>
    public string PartName
    {
        get
        {
            var name = ObjectNames
                .GroupBy(n => n)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? "";
            return StripMeshExtension(name);
        }
    }

    public bool HasMixedObjects => ObjectNames.Distinct().Count() > 1;

    /// <summary>Resin files only: the resin profile and volume for one print of the plate.</summary>
    public SlicedResin? Resin { get; init; }
    public int LayerCount { get; init; }
    public decimal LayerHeightMm { get; init; }

    internal static string StripMeshExtension(string name)
    {
        foreach (var ext in new[] { ".obj", ".stl", ".step", ".stp", ".3mf" })
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return name[..^ext.Length];
        }
        return name;
    }
}

/// <param name="SlotId">The project filament number (1-based), which is the AMS slot mapping in Bambu Studio.</param>
/// <param name="Profile">Filament preset with the printer suffix removed, e.g. "Bambu PLA Basic".</param>
public record SlicedFilament(int SlotId, string Type, string ColorHex, string Vendor, string Profile, decimal Grams);

/// <param name="Profile">Resin profile named in the slicer, e.g. "Creality Blue Fast resin".</param>
public record SlicedResin(string Profile, decimal Ml);

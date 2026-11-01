using PrintInvoicer.Core.Models;

namespace PrintInvoicer.Core.Pricing;

/// <summary>
/// Guesses which of your filaments an AMS slot in a sliced file means, from the slicer's vendor, material type and
/// color, so a new plate starts priced. The guess can always be changed, and that choice is remembered.
/// </summary>
public static class FilamentMatcher
{
    /// <summary>Key used to remember which filament you picked for a given slicer slot.</summary>
    public static string MappingKey(PlateFilament slot) =>
        $"{slot.SlicerVendor}|{slot.SlicerType}|{slot.SlicerColorHex}".ToUpperInvariant();

    /// <summary>
    /// Picks the best filament for a slot. A remembered choice wins; otherwise vendor, material and color are scored.
    /// Returns null when nothing matches at least the material type.
    /// </summary>
    public static Filament? Match(PlateFilament slot, IEnumerable<Filament> filaments, IReadOnlyDictionary<string, long> remembered)
    {
        // Only active filaments are offered for new plates.
        var active = filaments.Where(f => f.Active).ToList();

        // A choice you made before for this slicer vendor, type and color wins.
        if (remembered.TryGetValue(MappingKey(slot), out var id))
        {
            var hit = active.FirstOrDefault(f => f.Id == id);
            if (hit != null) return hit;
        }

        // Otherwise score each filament of the same material type: same vendor +2, same color +4. Highest score wins.
        Filament? best = null;
        int bestScore = 0;
        foreach (var f in active)
        {
            if (!SameMaterial(f.Material, slot.SlicerType)) continue;
            int score = 1;
            if (Same(f.Vendor, slot.SlicerVendor)) score += 2;
            if (!string.IsNullOrEmpty(f.ColorHex) && Same(f.ColorHex, slot.SlicerColorHex)) score += 4;
            if (score > bestScore)
            {
                best = f;
                bestScore = score;
            }
        }
        return best;
    }

    // Equal, ignoring case and surrounding spaces.
    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    // "PLA" in the slicer should match "PLA", "PLA Basic", "PLA+", but not "PLA-CF" or "PETG".
    private static bool SameMaterial(string filamentMaterial, string slicerType)
    {
        if (Same(filamentMaterial, slicerType)) return true;
        var first = filamentMaterial.Split(' ', '+').FirstOrDefault() ?? "";
        return Same(first, slicerType);
    }
}

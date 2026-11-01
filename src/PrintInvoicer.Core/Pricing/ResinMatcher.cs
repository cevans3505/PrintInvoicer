using PrintInvoicer.Core.Models;

namespace PrintInvoicer.Core.Pricing;

/// <summary>
/// Guesses which of your resins a sliced resin file means, from the resin profile name in the file, so a new plate
/// starts priced. The guess can always be changed in the job window, and that choice is remembered.
/// </summary>
public static class ResinMatcher
{
    /// <summary>Key used to remember which resin you picked for a slicer resin profile. Shares the filament mapping table.</summary>
    public static string MappingKey(string slicerProfile) => $"RESIN|{slicerProfile.Trim()}".ToUpperInvariant();

    /// <summary>
    /// Picks a resin for a slicer profile: a remembered choice first, then a resin whose full name or name appears in
    /// the profile. With exactly one active resin, that one is used. Returns null when nothing fits.
    /// </summary>
    public static Resin? Match(string slicerProfile, IEnumerable<Resin> resins, IReadOnlyDictionary<string, long> remembered)
    {
        // 1. A choice you made before for this exact profile name wins.
        var active = resins.Where(r => r.Active).ToList();
        if (remembered.TryGetValue(MappingKey(slicerProfile), out var id) && active.FirstOrDefault(r => r.Id == id) is Resin hit)
            return hit;

        // 2. Otherwise score each resin by how much of its name appears in the profile: the full name scores highest,
        //    the resin name alone less, plus a point if the vendor appears too.
        string profile = slicerProfile.Trim();
        if (profile.Length > 0)
        {
            var byName = active
                .Select(r => (Resin: r, Score: Contains(profile, r.DisplayName) ? 3 : Contains(profile, r.Name) && r.Name.Length > 2 ? 1 + (Contains(profile, r.Vendor) ? 1 : 0) : 0))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();
            if (byName.Resin != null) return byName.Resin;
        }
        // 3. With only one resin there is nothing to choose between; otherwise leave it for the user to pick.
        return active.Count == 1 ? active[0] : null;
    }

    // Case-insensitive "does the profile mention this?"; blank names never match.
    private static bool Contains(string haystack, string needle) =>
        !string.IsNullOrWhiteSpace(needle) && haystack.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);
}

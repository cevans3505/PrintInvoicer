using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PrintInvoicer.Core.Slicing;

/// <summary>
/// Reads print statistics from Bambu Studio output. A sliced .gcode.3mf carries everything we need in
/// Metadata/slice_info.config, so the (often 50+ MB) G-code inside it is never read. A plain .gcode
/// export is also supported by reading its header and config block.
/// </summary>
public static partial class BambuFileReader
{
    public static SlicedFile Read(string path)
    {
        if (path.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase))
            return ReadGcode(path);
        return ReadThreeMf(path);
    }

    public static SlicedFile ReadThreeMf(string path)
    {
        using var zip = ZipFile.OpenRead(path);

        var sliceInfoEntry = zip.GetEntry("Metadata/slice_info.config");
        var sliceInfo = sliceInfoEntry == null ? null : XDocument.Load(sliceInfoEntry.Open());
        var plateElements = sliceInfo?.Root?.Elements("plate").ToList() ?? new();

        // An unsliced project still has a slice_info.config, but its plates carry no filament usage.
        if (plateElements.Count == 0 || !plateElements.Any(p => p.Elements("filament").Any()))
            throw new InvalidDataException(
                "This 3MF has no slicing results. In Bambu Studio, slice the plate and use File > Export > Export plate sliced file (.gcode.3mf).");

        var settings = ReadProjectSettings(zip);
        var vendors = settings.GetValueOrDefault("filament_vendor") ?? new();
        var profiles = settings.GetValueOrDefault("filament_settings_id") ?? new();
        string? printer = settings.GetValueOrDefault("printer_model")?.FirstOrDefault();

        var plates = new List<SlicedPlate>();
        foreach (var plate in plateElements)
        {
            int index = int.Parse(Meta(plate, "index") ?? "1", CultureInfo.InvariantCulture);
            int seconds = (int)Math.Round(ParseDecimal(Meta(plate, "prediction")));

            var objects = plate.Elements("object")
                .Where(o => !string.Equals((string?)o.Attribute("skipped"), "true", StringComparison.OrdinalIgnoreCase))
                .Select(o => (string?)o.Attribute("name") ?? "")
                .ToList();

            var filaments = plate.Elements("filament")
                .Select(f =>
                {
                    int id = int.Parse((string?)f.Attribute("id") ?? "0", CultureInfo.InvariantCulture);
                    return new SlicedFilament(
                        id,
                        (string?)f.Attribute("type") ?? "",
                        NormalizeHex((string?)f.Attribute("color")),
                        ValueAt(vendors, id - 1),
                        CleanProfile(ValueAt(profiles, id - 1)),
                        ParseDecimal((string?)f.Attribute("used_g")));
                })
                .Where(f => f.Grams > 0)
                .ToList();

            // The 512 px render prints cleanly on invoices; both versions have a transparent background.
            var thumb = ReadEntry(zip, $"Metadata/plate_{index}.png") ?? ReadEntry(zip, $"Metadata/plate_{index}_small.png");
            plates.Add(new SlicedPlate(index, seconds, objects, filaments, thumb));
        }

        return new SlicedFile(path, printer, plates);
    }

    /// <summary>The full-size plate render from a sliced .gcode.3mf, or null if the file or image is gone.</summary>
    public static byte[]? ReadPlateImage(string path, int plateIndex)
    {
        if (!path.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return ReadEntry(zip, $"Metadata/plate_{plateIndex}.png");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static SlicedFile ReadGcode(string path)
    {
        // Bambu writes the header and the full config block at the top of the file, so we can stop early.
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? timeText = null, weights = null, labels = null;

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("; CONFIG_BLOCK_END", StringComparison.Ordinal)) break;
            if (!line.StartsWith(';')) continue;

            var m = TimeLine().Match(line);
            if (m.Success) { timeText = m.Groups[1].Value; continue; }
            if (line.StartsWith("; total filament weight [g] :", StringComparison.Ordinal)) { weights = line[(line.IndexOf(':') + 1)..]; continue; }
            if (line.StartsWith("; model label id:", StringComparison.Ordinal)) { labels = line[(line.IndexOf(':') + 1)..]; continue; }

            int eq = line.IndexOf(" = ", StringComparison.Ordinal);
            if (eq > 2) values[line[2..eq].Trim()] = line[(eq + 3)..];
        }

        if (weights == null || timeText == null)
            throw new InvalidDataException("Could not find Bambu Studio print statistics in this G-code header.");

        var grams = weights.Split(',').Select(s => ParseDecimal(s.Trim())).ToList();
        var types = SplitList(values.GetValueOrDefault("filament_type"));
        var colors = SplitList(values.GetValueOrDefault("filament_colour"));
        var vendors = SplitList(values.GetValueOrDefault("filament_vendor"));
        var profiles = SplitList(values.GetValueOrDefault("filament_settings_id"));

        var filaments = grams
            .Select((g, i) => new SlicedFilament(i + 1, ValueAt(types, i), NormalizeHex(ValueAt(colors, i)),
                ValueAt(vendors, i), CleanProfile(ValueAt(profiles, i)), g))
            .Where(f => f.Grams > 0)
            .ToList();

        int count = labels?.Split(',', StringSplitOptions.RemoveEmptyEntries).Length ?? 1;
        string name = Path.GetFileNameWithoutExtension(path);
        var objects = Enumerable.Repeat(name, Math.Max(count, 1)).ToList();

        return new SlicedFile(path, values.GetValueOrDefault("printer_model"),
            new[] { new SlicedPlate(1, ParseDuration(timeText), objects, filaments, null) });
    }

    /// <summary>Parses Bambu durations like "1d 5h 55m 54s".</summary>
    public static int ParseDuration(string text)
    {
        int total = 0;
        foreach (Match m in DurationPart().Matches(text))
        {
            int n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            total += m.Groups[2].Value switch
            {
                "d" => n * 86400,
                "h" => n * 3600,
                "m" => n * 60,
                _ => n,
            };
        }
        return total;
    }

    /// <summary>"Bambu PLA Basic @BBL H2D" becomes "Bambu PLA Basic".</summary>
    public static string CleanProfile(string profile)
    {
        int at = profile.IndexOf(" @", StringComparison.Ordinal);
        return (at >= 0 ? profile[..at] : profile).Trim();
    }

    private static Dictionary<string, List<string>> ReadProjectSettings(ZipArchive zip)
    {
        var result = new Dictionary<string, List<string>>();
        var entry = zip.GetEntry("Metadata/project_settings.config");
        if (entry == null) return result;

        try
        {
            using var doc = JsonDocument.Parse(entry.Open());
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.Array => prop.Value.EnumerateArray().Select(v => v.ToString()).ToList(),
                    _ => new List<string> { prop.Value.ToString() },
                };
            }
        }
        catch (JsonException)
        {
            // Older files may not be JSON; vendor and profile are only used as matching hints.
        }
        return result;
    }

    private static string? Meta(XElement plate, string key) =>
        plate.Elements("metadata").FirstOrDefault(m => (string?)m.Attribute("key") == key)?.Attribute("value")?.Value;

    private static byte[]? ReadEntry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry == null) return null;
        using var ms = new MemoryStream();
        using (var s = entry.Open()) s.CopyTo(ms);
        return ms.ToArray();
    }

    private static List<string> SplitList(string? value)
    {
        if (string.IsNullOrEmpty(value)) return new();
        return value.Split(';').Select(s => s.Trim().Trim('"')).ToList();
    }

    private static string ValueAt(List<string> list, int i) => i >= 0 && i < list.Count ? list[i] : "";

    private static decimal ParseDecimal(string? s) =>
        decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    internal static string NormalizeHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return "";
        hex = hex.Trim().ToUpperInvariant();
        if (!hex.StartsWith('#')) hex = "#" + hex;
        // Bambu sometimes writes #RRGGBBAA; drop the alpha.
        return hex.Length == 9 ? hex[..7] : hex;
    }

    [GeneratedRegex(@"total estimated time:\s*([0-9dhms ]+)")]
    private static partial Regex TimeLine();

    [GeneratedRegex(@"(\d+)\s*([dhms])")]
    private static partial Regex DurationPart();
}

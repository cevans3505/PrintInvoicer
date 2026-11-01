using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PrintInvoicer.Core.Slicing;

/// <summary>
/// Reads print statistics from Bambu Studio output. A sliced .gcode.3mf carries everything we need in
/// Metadata/slice_info.config, so the (often 50+ MB) G-code inside it is never read. A plain .gcode
/// export is also supported by reading its header and config block.
/// </summary>
/// <remarks>
/// Every failure caused by the file's content is an <see cref="InvalidDataException"/> with a message for the user;
/// values are range-checked against <see cref="SliceLimits"/> before they leave the reader.
/// </remarks>
public static partial class BambuFileReader
{
    private const string SliceInfoEntry = "Metadata/slice_info.config", SettingsEntry = "Metadata/project_settings.config";
    private const int MaxXmlDepth = 64;

    public static SlicedFile Read(string path)
    {
        if (path.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase))
            return ReadGcode(path);
        return ReadThreeMf(path);
    }

    public static SlicedFile ReadThreeMf(string path)
    {
        // Opening the file is kept apart so a missing or locked file still reports as the IOException it is.
        using var stream = File.OpenRead(path);
        using var zip = OpenZip(stream);

        var sliceInfoBytes = ReadEntry(zip, SliceInfoEntry, SliceLimits.MaxMetadataBytes);
        var sliceInfo = sliceInfoBytes == null ? null : LoadXml(sliceInfoBytes);
        var plateElements = sliceInfo?.Root?.Elements("plate").ToList() ?? new();

        // An unsliced project still has a slice_info.config, but its plates carry no filament usage.
        if (plateElements.Count == 0 || !plateElements.Any(p => p.Elements("filament").Any()))
            throw new InvalidDataException(
                "This 3MF has no slicing results. In Bambu Studio, slice the plate and use File > Export > Export plate sliced file (.gcode.3mf).");
        if (plateElements.Count > SliceLimits.MaxPlateIndex)
            throw Damaged($"it lists {plateElements.Count:N0} plates");

        var settings = ReadProjectSettings(zip);
        var vendors = settings.GetValueOrDefault("filament_vendor") ?? new();
        var profiles = settings.GetValueOrDefault("filament_settings_id") ?? new();
        string? printer = settings.GetValueOrDefault("printer_model")?.FirstOrDefault();

        var plates = new List<SlicedPlate>();
        foreach (var plate in plateElements)
        {
            string? indexText = Meta(plate, "index");
            int index = indexText == null ? 1 : ParseInt(indexText, 1, SliceLimits.MaxPlateIndex)
                ?? throw Damaged($"a plate has the number {SliceLimits.Quote(indexText)}");

            string? predictionText = Meta(plate, "prediction");
            if (predictionText == null) throw Damaged($"plate {index} has no print time");
            decimal prediction = ParseDecimal(predictionText, SliceLimits.MaxSeconds)
                ?? throw Damaged($"plate {index} has the print time {SliceLimits.Quote(predictionText)}");
            int seconds = (int)Math.Round(prediction);

            var objects = plate.Elements("object")
                .Where(o => !string.Equals((string?)o.Attribute("skipped"), "true", StringComparison.OrdinalIgnoreCase))
                .Select(o => (string?)o.Attribute("name") ?? "")
                .ToList();
            if (objects.Count > SliceLimits.MaxObjects) throw Damaged($"plate {index} lists {objects.Count:N0} objects");

            var filaments = plate.Elements("filament")
                .Select(f =>
                {
                    string? idText = (string?)f.Attribute("id");
                    int id = ParseInt(idText, 1, SliceLimits.MaxSlotId)
                        ?? throw Damaged($"plate {index} has a filament numbered {SliceLimits.Quote(idText)}");
                    string? gramsText = (string?)f.Attribute("used_g");
                    decimal grams = ParseDecimal(gramsText, SliceLimits.MaxGrams)
                        ?? throw Damaged($"plate {index} filament {id} has the weight {SliceLimits.Quote(gramsText)}");
                    return new SlicedFilament(
                        id,
                        (string?)f.Attribute("type") ?? "",
                        NormalizeHex((string?)f.Attribute("color")),
                        ValueAt(vendors, id - 1),
                        CleanProfile(ValueAt(profiles, id - 1)),
                        grams);
                })
                .Where(f => f.Grams > 0)
                .ToList();

            // The 512 px render prints cleanly on invoices; both versions have a transparent background.
            var thumb = ReadImage(zip, $"Metadata/plate_{index}.png") ?? ReadImage(zip, $"Metadata/plate_{index}_small.png");
            plates.Add(new SlicedPlate(index, seconds, objects, filaments, thumb));
        }

        return new SlicedFile(path, printer, plates);
    }

    /// <summary>The full-size plate render from a sliced .gcode.3mf, or null if the file or image is gone or unusable.</summary>
    public static byte[]? ReadPlateImage(string path, int plateIndex)
    {
        if (!path.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            using var zip = OpenZip(stream);
            return ReadImage(zip, $"Metadata/plate_{plateIndex}.png");
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

        foreach (var line in HeaderLines(path))
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

        var (seconds, parts) = SumDuration(timeText);
        if (parts == 0 || seconds > SliceLimits.MaxSeconds)
            throw DamagedGcode($"the print time {SliceLimits.Quote(timeText.Trim())}");

        // An empty entry is an unused slot; anything else must be a weight, or the slot numbers would shift.
        var weightTexts = weights.Split(',');
        if (weightTexts.Length > SliceLimits.MaxSlotId) throw DamagedGcode($"{weightTexts.Length:N0} filament weights");
        var grams = weightTexts
            .Select(s => s.Trim())
            .Select(s => s.Length == 0 ? 0m : ParseDecimal(s, SliceLimits.MaxGrams) ?? throw DamagedGcode($"the filament weight {SliceLimits.Quote(s)}"))
            .ToList();
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
        if (count > SliceLimits.MaxObjects) throw DamagedGcode($"{count:N0} objects");
        string name = Path.GetFileNameWithoutExtension(path);
        var objects = Enumerable.Repeat(name, Math.Max(count, 1)).ToList();

        return new SlicedFile(path, values.GetValueOrDefault("printer_model"),
            new[] { new SlicedPlate(1, (int)seconds, objects, filaments, null) });
    }

    /// <summary>Parses Bambu durations like "1d 5h 55m 54s".</summary>
    /// <exception cref="InvalidDataException">The duration does not fit in an <see cref="int"/> number of seconds.</exception>
    public static int ParseDuration(string text)
    {
        var (seconds, _) = SumDuration(text);
        if (seconds > int.MaxValue) throw new InvalidDataException($"The print time {SliceLimits.Quote(text.Trim())} is too long to be real.");
        return (int)seconds;
    }

    /// <summary>Total seconds and how many number-unit parts were found; the total is capped just past int range.</summary>
    private static (long Seconds, int Parts) SumDuration(string text)
    {
        const long tooLong = (long)int.MaxValue + 1;
        long total = 0;
        int parts = 0;
        foreach (Match m in DurationPart().Matches(text))
        {
            parts++;
            if (!long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long n) || n >= tooLong) return (tooLong, parts);
            total += n * m.Groups[2].Value switch
            {
                "d" => 86400,
                "h" => 3600,
                "m" => 60,
                _ => 1,
            };
            if (total >= tooLong) return (tooLong, parts);
        }
        return (total, parts);
    }

    /// <summary>"Bambu PLA Basic @BBL H2D" becomes "Bambu PLA Basic".</summary>
    public static string CleanProfile(string profile)
    {
        int at = profile.IndexOf(" @", StringComparison.Ordinal);
        return (at >= 0 ? profile[..at] : profile).Trim();
    }

    private static ZipArchive OpenZip(Stream stream)
    {
        ZipArchive? zip = null;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            // The central directory is read on first use; do it here so its errors get the message below.
            _ = zip.Entries.Count;
            return zip;
        }
        catch (Exception ex) when (IsZipFailure(ex))
        {
            zip?.Dispose();
            throw new InvalidDataException(
                "This is not a readable .gcode.3mf file: it is damaged or is a different kind of file. Export the plate sliced file from Bambu Studio again.", ex);
        }
    }

    /// <summary>What the zip library throws for damaged archives and entries.</summary>
    private static bool IsZipFailure(Exception ex) =>
        ex is InvalidDataException or IOException or NotSupportedException or ArgumentException;

    /// <summary>
    /// The entry's bytes, or null if it is not there. Reads at most <paramref name="maxBytes"/> whatever the archive
    /// claims, so a small file that inflates to gigabytes is refused instead of filling memory.
    /// </summary>
    private static byte[]? ReadEntry(ZipArchive zip, string name, int maxBytes)
    {
        var entry = zip.GetEntry(name);
        if (entry == null) return null;
        if (entry.Length > maxBytes) throw TooLarge(name);
        using var ms = new MemoryStream();
        try
        {
            using var s = entry.Open();
            var buffer = new byte[81920];
            int n;
            while ((n = s.Read(buffer)) > 0)
            {
                // The recorded size can lie, so count what actually comes out.
                if (ms.Length + n > maxBytes) throw TooLarge(name);
                ms.Write(buffer, 0, n);
            }
        }
        catch (Exception ex) when (IsZipFailure(ex) && !IsTooLarge(ex))
        {
            throw Damaged($"{name} cannot be unpacked", ex);
        }
        return ms.ToArray();
    }

    /// <summary>A plate picture, or null if it is missing, damaged, too big or not a PNG; the plate still reads without one.</summary>
    private static byte[]? ReadImage(ZipArchive zip, string name)
    {
        try
        {
            var png = ReadEntry(zip, name, SliceLimits.MaxImageBytes);
            return png != null && SliceLimits.IsUsablePng(png) ? png : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static XDocument LoadXml(byte[] bytes)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = SliceLimits.MaxMetadataBytes,
        };
        try
        {
            // XDocument.Load slows down with the square of the nesting depth, so a few MB of nested tags would hang
            // the app. The real file is three levels deep; check that with a streaming pass before building anything.
            using (var scan = XmlReader.Create(new MemoryStream(bytes), settings))
            {
                while (scan.Read())
                {
                    if (scan.Depth > MaxXmlDepth)
                        throw Damaged($"its slicing summary ({SliceInfoEntry}) is nested more than {MaxXmlDepth} levels deep");
                }
            }
            using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
            return XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw Damaged($"its slicing summary ({SliceInfoEntry}) is not valid XML (line {ex.LineNumber})", ex);
        }
    }

    private static Dictionary<string, List<string>> ReadProjectSettings(ZipArchive zip)
    {
        var result = new Dictionary<string, List<string>>();
        try
        {
            var bytes = ReadEntry(zip, SettingsEntry, SliceLimits.MaxMetadataBytes);
            if (bytes == null) return result;
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.Array => prop.Value.EnumerateArray().Select(v => v.ToString()).ToList(),
                    _ => new List<string> { prop.Value.ToString() },
                };
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            // Older files may not be JSON, and a damaged one is no reason to refuse the file: vendor and profile
            // are only used as matching hints.
            result.Clear();
        }
        return result;
    }

    /// <summary>
    /// Lines from the top of a text file. Lines longer than <see cref="SliceLimits.MaxGcodeLineChars"/> are skipped
    /// (none of the header lines we use come close), and reading stops after
    /// <see cref="SliceLimits.MaxGcodeHeaderChars"/>, so a binary file renamed to .gcode cannot exhaust memory.
    /// </summary>
    private static IEnumerable<string> HeaderLines(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[64 * 1024];
        var line = new StringBuilder();
        bool skipping = false;
        long total = 0;
        int n;
        while (total < SliceLimits.MaxGcodeHeaderChars && (n = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += n;
            int start = 0;
            for (int i = 0; i < n; i++)
            {
                if (buffer[i] != '\n') continue;
                if (!skipping && Append(line, buffer.AsSpan(start, i - start))) yield return TrimCr(line);
                line.Clear();
                skipping = false;
                start = i + 1;
            }
            if (!skipping && !Append(line, buffer.AsSpan(start, n - start)))
            {
                skipping = true;
                line.Clear();
            }
        }
        if (!skipping && line.Length > 0 && total < SliceLimits.MaxGcodeHeaderChars) yield return TrimCr(line);

        static bool Append(StringBuilder sb, ReadOnlySpan<char> chars)
        {
            if (sb.Length + chars.Length > SliceLimits.MaxGcodeLineChars) return false;
            sb.Append(chars);
            return true;
        }

        static string TrimCr(StringBuilder sb) =>
            sb.Length > 0 && sb[^1] == '\r' ? sb.ToString(0, sb.Length - 1) : sb.ToString();
    }

    private static string? Meta(XElement plate, string key) =>
        plate.Elements("metadata").FirstOrDefault(m => (string?)m.Attribute("key") == key)?.Attribute("value")?.Value;

    private static List<string> SplitList(string? value)
    {
        if (string.IsNullOrEmpty(value)) return new();
        return value.Split(';').Select(s => s.Trim().Trim('"')).ToList();
    }

    private static string ValueAt(List<string> list, int i) => i >= 0 && i < list.Count ? list[i] : "";

    /// <summary>A whole number from min to max, or null if the text is anything else.</summary>
    private static int? ParseInt(string? s, int min, int max) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= min && n <= max ? n : null;

    /// <summary>A number from 0 to max, or null if the text is missing, not a number, negative or too large.</summary>
    private static decimal? ParseDecimal(string? s, decimal max) =>
        decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d >= 0 && d <= max ? d : null;

    private static InvalidDataException Damaged(string what, Exception? inner = null) =>
        new($"This .gcode.3mf file looks damaged: {what}. Export the plate sliced file from Bambu Studio again.", inner);

    private static InvalidDataException DamagedGcode(string what) =>
        new($"This G-code file looks damaged: its header gives {what}. Export the G-code from Bambu Studio again.");

    private const string TooLargeKey = "PrintInvoicer.TooLarge";

    private static InvalidDataException TooLarge(string name)
    {
        var ex = new InvalidDataException($"This .gcode.3mf file looks damaged: {name} is too large to be a real slicing file.");
        ex.Data[TooLargeKey] = true;
        return ex;
    }

    private static bool IsTooLarge(Exception ex) => ex.Data.Contains(TooLargeKey);

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

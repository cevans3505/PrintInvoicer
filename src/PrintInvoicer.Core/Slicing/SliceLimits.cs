using System.Buffers.Binary;

namespace PrintInvoicer.Core.Slicing;

/// <summary>
/// Bounds for what the readers accept from a sliced file. Real prints are nowhere near these; a value past one means
/// the file is damaged, and it is rejected rather than priced. The size limits keep a damaged or hostile file from
/// making the app read or allocate without end.
/// </summary>
internal static class SliceLimits
{
    public const int MaxSeconds = 60 * 86400;          // 60 days
    public const int MaxLayers = 1_000_000;
    public const float MaxLayerHeightMm = 1f;
    public const decimal MaxMl = 100_000m;              // 100 L
    public const decimal MaxGrams = 100_000m;           // 100 kg per filament
    public const int MaxPlateIndex = 999;
    public const int MaxSlotId = 999;
    public const int MaxObjects = 100_000;

    /// <summary>Largest slice_info.config or project_settings.config read from a .gcode.3mf.</summary>
    public const int MaxMetadataBytes = 16 * 1024 * 1024;
    /// <summary>Largest plate picture kept; Bambu's are a few hundred KB.</summary>
    public const int MaxImageBytes = 16 * 1024 * 1024;
    public const int MaxImageSide = 4096;

    /// <summary>Plain .gcode: lines longer than this are skipped, and the header search stops after this much text.</summary>
    public const int MaxGcodeLineChars = 1024 * 1024;
    public const long MaxGcodeHeaderChars = 32L * 1024 * 1024;

    /// <summary>
    /// True if the bytes start like a PNG whose size the invoice code can decode. The pixels are not checked; the
    /// invoice code already copes with undecodable data, but not with a header claiming a gigapixel image.
    /// </summary>
    public static bool IsUsablePng(byte[] png)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < 33 || png.Length > MaxImageBytes || !png.AsSpan(0, 8).SequenceEqual(signature)) return false;
        if (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(8)) != 13 || !png.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return false;
        int w = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), h = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        return w is >= 1 and <= MaxImageSide && h is >= 1 and <= MaxImageSide;
    }

    /// <summary>A value from a file, shortened so a damaged field cannot flood an error message.</summary>
    public static string Quote(string? value)
    {
        value ??= "";
        var clean = new string(value.Select(c => char.IsControl(c) ? '?' : c).Take(41).ToArray());
        return clean.Length > 40 ? $"\"{clean[..40]}...\"" : $"\"{clean}\"";
    }
}

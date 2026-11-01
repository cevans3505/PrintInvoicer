using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PrintInvoicer.Core.Models;

namespace PrintInvoicer.Core.Slicing;

/// <summary>
/// Reads the header of an ELEGOO .goo resin file (Saturn and Mars printers; written by ELEGOO SatelLite, CHITUBOX and
/// VoxelDance Tango). Layout from ELEGOO's published GOO format spec V1.2; every field is fixed width and big-endian.
/// Only the header is read: printer, resin profile, layers, print time, resin volume and the preview picture.
/// </summary>
public static partial class GooFileReader
{
    // The file extension and the fixed 8-byte "magic" tag every .goo file carries at byte 4.
    public const string Extension = ".goo";

    private static readonly byte[] Magic = [0x07, 0x00, 0x00, 0x00, 0x44, 0x4C, 0x50, 0x00];

    // Byte offsets of the header fields used here.
    private const int PrinterNameAt = 92, ProfileNameAt = 156, SoftwareAt = 12;
    private const int SmallPreviewAt = 194, SmallPreviewSize = 116;
    private const int BigPreviewAt = SmallPreviewAt + 2 * SmallPreviewSize * SmallPreviewSize + 2, BigPreviewSize = 290;
    private const int LayersAt = BigPreviewAt + 2 * BigPreviewSize * BigPreviewSize + 2;   // 195310
    private const int LayerHeightAt = LayersAt + 22;
    private const int PrintSecondsAt = LayersAt + 136;
    private const int VolumeAt = PrintSecondsAt + 4;
    public const int HeaderLength = LayersAt + 167;                                             // 195477

    /// <summary>True for .goo files; used to pick this reader.</summary>
    public static bool CanRead(string path) => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads only the fixed-size header (about 190 KB) of a .goo file, read-only. The layer images after it are never
    /// read, and nothing in the file is executed.
    /// </summary>
    public static SlicedFile Read(string path)
    {
        var header = new byte[HeaderLength];
        // AUDIT: file read. Reads the header of the .goo file the user chose, read-only.
        using (var fs = File.OpenRead(path))
        {
            if (fs.ReadAtLeast(header, HeaderLength, throwOnEndOfStream: false) < HeaderLength)
                throw new InvalidDataException("This .goo file is too short to be a sliced resin file.");
        }
        return Parse(path, header);
    }

    /// <summary>Turns header bytes into a one-plate SlicedFile, rejecting anything that is not a valid, sensible header.</summary>
    public static SlicedFile Parse(string path, ReadOnlySpan<byte> h)
    {
        // The magic tag and the two fixed delimiters confirm the layout before any field is trusted.
        if (h.Length < HeaderLength || !h.Slice(4, 8).SequenceEqual(Magic) || !IsDelimiter(h, BigPreviewAt - 2) || !IsDelimiter(h, LayersAt - 2))
            throw new InvalidDataException("This is not an ELEGOO .goo sliced file, or it uses a version this app does not know.");

        // The fields used: printer name, resin profile name, layer count, layer height, print time and resin volume.
        string printer = Text(h.Slice(PrinterNameAt, 32));
        string profile = Text(h.Slice(ProfileNameAt, 32));
        int layers = BinaryPrimitives.ReadInt32BigEndian(h.Slice(LayersAt));
        float layerHeight = BinaryPrimitives.ReadSingleBigEndian(h.Slice(LayerHeightAt));
        int seconds = BinaryPrimitives.ReadInt32BigEndian(h.Slice(PrintSecondsAt));
        float volumeMm3 = BinaryPrimitives.ReadSingleBigEndian(h.Slice(VolumeAt));

        // Range checks come before any conversion: NaN, infinity or a huge float would make the decimal cast throw,
        // and a negative or absurd value would otherwise be priced.
        if (layers is < 0 or > SliceLimits.MaxLayers)
            throw Damaged($"a layer count of {layers}");
        if (!float.IsFinite(layerHeight) || layerHeight < 0 || layerHeight > SliceLimits.MaxLayerHeightMm)
            throw Damaged($"a layer height of {layerHeight.ToString(CultureInfo.InvariantCulture)} mm");
        if (seconds is < 0 or > SliceLimits.MaxSeconds)
            throw Damaged($"a print time of {seconds} seconds");
        if (!float.IsFinite(volumeMm3) || volumeMm3 < 0 || volumeMm3 > (float)SliceLimits.MaxMl * 1000f)
            throw Damaged($"a resin volume of {volumeMm3.ToString(CultureInfo.InvariantCulture)} mm³");

        // A resin file is one plate. Volume is converted from mm3 to mL; the part count is not in the file.
        var plate = new SlicedPlate(1, seconds, [], [], Preview(h.Slice(BigPreviewAt, 2 * BigPreviewSize * BigPreviewSize), BigPreviewSize))
        {
            Resin = new SlicedResin(profile, Math.Round((decimal)volumeMm3 / 1000m, 2)),
            LayerCount = layers,
            LayerHeightMm = Math.Round((decimal)layerHeight, 3),
        };
        return new SlicedFile(path, string.IsNullOrWhiteSpace(printer) ? null : printer, [plate]) { Technology = PrintTechnology.Resin };
    }

    /// <summary>Software that wrote the file, e.g. "CHITUBOX Basic v2.3.1"; for tests and diagnostics.</summary>
    public static string Software(ReadOnlySpan<byte> h) => Text(h.Slice(SoftwareAt, 32));

    /// <summary>
    /// Part name from the file name. Slicers append settings and timestamps ("garklein.stl_0.05_1.2_2025_06_02_12_57_00",
    /// "Tiny_1_202501282210"), so those are trimmed off.
    /// </summary>
    public static string PartNameFromFile(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var mesh = MeshSuffix().Match(name);
        if (mesh.Success && mesh.Index > 0) name = name[..mesh.Index];
        name = TrailingStamp().Replace(name, "");
        return name.Trim(' ', '_', '-');
    }

    [GeneratedRegex(@"\.(stl|obj|3mf|lys|lyt|chitubox)(?=[_\s.]|$)", RegexOptions.IgnoreCase)]
    // A model file extension inside the name, where slicers start appending their settings.
    private static partial Regex MeshSuffix();

    [GeneratedRegex(@"[_\s-]+\d{12,14}$")]
    // A trailing date-time stamp such as _202501282210.
    private static partial Regex TrailingStamp();

    // The error shown to the user for a damaged .goo file.
    private static InvalidDataException Damaged(string what) =>
        new($"This .goo file looks damaged: its header gives {what}. Slice the model again and save a new .goo file.");

    // The spec puts a CR LF pair after each preview image.
    private static bool IsDelimiter(ReadOnlySpan<byte> h, int at) => h[at] == 0x0D && h[at + 1] == 0x0A;

    // A fixed-width, zero-padded text field.
    private static string Text(ReadOnlySpan<byte> b)
    {
        int end = b.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? b : b[..end]).Trim();
    }

    /// <summary>
    /// Turns the RGB565 preview into a PNG. The slicer draws parts on a flat background (black or near white); that
    /// background is made transparent, working in from the edges, so invoices print only the parts.
    /// </summary>
    private static byte[]? Preview(ReadOnlySpan<byte> data, int size)
    {
        // Expand each 16-bit RGB565 pixel to 8-bit red, green and blue, fully opaque.
        var rgba = new byte[size * size * 4];
        bool any = false;
        for (int i = 0; i < size * size; i++)
        {
            ushort v = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i * 2));
            any |= v != 0;
            rgba[i * 4] = (byte)(((v >> 11) & 31) * 255 / 31);
            rgba[i * 4 + 1] = (byte)(((v >> 5) & 63) * 255 / 63);
            rgba[i * 4 + 2] = (byte)((v & 31) * 255 / 31);
            rgba[i * 4 + 3] = 255;
        }
        if (!any) return null;

        ClearBackground(rgba, size);
        return PngWriter.EncodeRgba(size, size, rgba);
    }

    // Flood fill from every edge pixel: pixels close to the top-left corner's color, and connected to the edge,
    // become transparent. Parts are not touched even if they share the background color, as long as they are
    // enclosed by other colors.
    private static void ClearBackground(byte[] rgba, int size)
    {
        int bg = Rgb(rgba, 0);
        var stack = new Stack<int>();
        for (int i = 0; i < size; i++)
        {
            stack.Push(i);
            stack.Push((size - 1) * size + i);
            stack.Push(i * size);
            stack.Push(i * size + size - 1);
        }
        while (stack.Count > 0)
        {
            int p = stack.Pop();
            if (rgba[p * 4 + 3] == 0 || !Near(Rgb(rgba, p), bg)) continue;
            rgba[p * 4 + 3] = 0;
            int x = p % size, y = p / size;
            if (x > 0) stack.Push(p - 1);
            if (x < size - 1) stack.Push(p + 1);
            if (y > 0) stack.Push(p - size);
            if (y < size - 1) stack.Push(p + size);
        }
    }

    // Pixel p's color packed as 0xRRGGBB.
    private static int Rgb(byte[] rgba, int p) => rgba[p * 4] << 16 | rgba[p * 4 + 1] << 8 | rgba[p * 4 + 2];

    // True when two colors differ by at most 8 in each channel, to absorb the preview's rounding.
    private static bool Near(int a, int b) =>
        Math.Abs((a >> 16) - (b >> 16)) <= 8 && Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) <= 8 && Math.Abs((a & 255) - (b & 255)) <= 8;
}

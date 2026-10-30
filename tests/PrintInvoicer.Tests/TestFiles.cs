using System.Buffers.Binary;
using System.IO;
using System.Text;
using PrintInvoicer.Core.Slicing;

namespace PrintInvoicer.Tests;

/// <summary>Sample files shared by the unit and screen tests; no real customer files are kept in the repo.</summary>
internal static class TestFiles
{
    /// <summary>The Bambu Studio sample files in Fixtures.</summary>
    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>
    /// A .goo header laid out per ELEGOO's GOO spec V1.2, with values like a real Saturn 4 Ultra file
    /// (CHITUBOX, 3032 layers at 0.05 mm, 5 h 31 min, 37.2 mL).
    /// </summary>
    public static byte[] GooHeader(string printer = "ELEGOO Saturn 4 Ultra", string profile = "Creality Blue Fast resin",
        int layers = 3032, float layerHeight = 0.05f, int seconds = 19860, float volumeMm3 = 37205.8f, ushort background = 0xFFFF)
    {
        var h = new byte[GooFileReader.HeaderLength];
        void Str(int at, string s) => Encoding.UTF8.GetBytes(s).CopyTo(h, at);
        Str(0, "V3.0");
        new byte[] { 0x07, 0x00, 0x00, 0x00, 0x44, 0x4C, 0x50, 0x00 }.CopyTo(h, 4);
        Str(12, "CHITUBOX Basic v2.3.1");
        Str(92, printer);
        Str(124, printer);
        Str(156, profile);

        // Previews: a flat background with a blue square in the middle of the big one.
        for (int i = 0; i < 116 * 116; i++) BinaryPrimitives.WriteUInt16BigEndian(h.AsSpan(194 + i * 2), background);
        h[194 + 2 * 116 * 116] = 0x0D; h[194 + 2 * 116 * 116 + 1] = 0x0A;
        int big = 27108;
        for (int y = 0; y < 290; y++)
            for (int x = 0; x < 290; x++)
                BinaryPrimitives.WriteUInt16BigEndian(h.AsSpan(big + (y * 290 + x) * 2), x is > 100 and < 190 && y is > 100 and < 190 ? (ushort)0x001F : background);
        h[big + 2 * 290 * 290] = 0x0D; h[big + 2 * 290 * 290 + 1] = 0x0A;

        int layersAt = 195310;
        BinaryPrimitives.WriteInt32BigEndian(h.AsSpan(layersAt), layers);
        BinaryPrimitives.WriteSingleBigEndian(h.AsSpan(layersAt + 22), layerHeight);
        BinaryPrimitives.WriteInt32BigEndian(h.AsSpan(layersAt + 136), seconds);
        BinaryPrimitives.WriteSingleBigEndian(h.AsSpan(layersAt + 140), volumeMm3);
        BinaryPrimitives.WriteSingleBigEndian(h.AsSpan(layersAt + 144), volumeMm3 / 1000f);
        return h;
    }
}

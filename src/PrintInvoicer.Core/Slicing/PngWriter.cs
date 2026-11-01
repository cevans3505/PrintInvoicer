using System.Buffers.Binary;
using System.IO.Compression;

namespace PrintInvoicer.Core.Slicing;

/// <summary>Minimal PNG encoder for 8-bit RGBA images, so slicer previews can be stored like Bambu's plate PNGs.</summary>
public static class PngWriter
{
    /// <summary>
    /// Encodes raw RGBA pixels (4 bytes per pixel, row by row) as PNG bytes in memory. Nothing is written to disk.
    /// </summary>
    public static byte[] EncodeRgba(int width, int height, byte[] rgba)
    {
        if (rgba.Length != width * height * 4) throw new ArgumentException("Pixel buffer does not match the image size.", nameof(rgba));

        // Each PNG row starts with a filter byte; 0 means the row's pixels are stored as they are.
        var raw = new byte[(width * 4 + 1) * height];
        for (int y = 0; y < height; y++)
        {
            raw[y * (width * 4 + 1)] = 0; // filter: none
            Buffer.BlockCopy(rgba, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);
        }

        // All rows are compressed together with zlib (deflate), as the PNG format requires.
        byte[] idat;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);
            idat = ms.ToArray();
        }

        // Image header: width, height, 8 bits per channel, color type 6 (RGBA).
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // RGBA

        // The PNG signature, then the three chunks every PNG needs: header, image data, end.
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", idat);
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    // Writes one PNG chunk: data length, 4-letter type, data, then a CRC-32 checksum of type and data.
    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(four, data.Length);
        s.Write(four);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc32(typeBytes, data);
        BinaryPrimitives.WriteUInt32BigEndian(four, crc);
        s.Write(four);
    }

    // The standard CRC-32 lookup table (polynomial 0xEDB88320) that PNG checksums use.
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(byte[] a, byte[] b)
    {
        uint c = 0xFFFFFFFFu;
        foreach (var x in a) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in b) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}

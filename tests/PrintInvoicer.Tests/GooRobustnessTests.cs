using System.Buffers.Binary;
using PrintInvoicer.Core.Slicing;

namespace PrintInvoicer.Tests;

/// <summary>Malformed .goo headers: the reader must return sensible values or reject the file with its own message.</summary>
public sealed class GooRobustnessTests : IDisposable
{
    // Field offsets from ELEGOO's GOO spec V1.2, as used by GooFileReader.
    internal const int SmallPreviewAt = 194, SmallDelimiterAt = 194 + 2 * 116 * 116;
    internal const int BigPreviewAt = SmallDelimiterAt + 2, BigDelimiterAt = BigPreviewAt + 2 * 290 * 290;
    internal const int LayersAt = BigDelimiterAt + 2, LayerHeightAt = LayersAt + 22, SecondsAt = LayersAt + 136, VolumeAt = LayersAt + 140;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pi-goo-fuzz-{Guid.NewGuid():N}");

    public GooRobustnessTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(byte[] bytes, string name = "part.goo")
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Checks the contract both on the bytes in memory and on a file read from disk.</summary>
    private SlicedFile? Check(byte[] bytes, string label)
    {
        var parsed = ReaderContract.Check(() => GooFileReader.Parse("part.goo", bytes), label + " (Parse)");
        string path = Write(bytes);
        var read = ReaderContract.Check(() => SliceFileReader.Read(path), label + " (Read)");
        Assert.Equal(parsed is null, read is null);
        return read;
    }

    [Fact]
    public void Valid_header_still_reads()
    {
        var file = ReaderContract.Reads(() => GooFileReader.Parse("part.goo", TestFiles.GooHeader()), "valid header");
        var plate = Assert.Single(file.Plates);
        Assert.Equal(19860, plate.PredictedSeconds);
        Assert.Equal(3032, plate.LayerCount);
        Assert.Equal(0.05m, plate.LayerHeightMm);
        Assert.Equal(37.21m, plate.Resin!.Ml);
    }

    [Fact]
    public void Layer_data_after_the_header_is_ignored()
    {
        var bytes = TestFiles.GooHeader().Concat(new byte[4096]).ToArray();
        Assert.NotNull(Check(bytes, "header plus layer data"));
    }

    public static TheoryData<int> TruncatedLengths()
    {
        var lengths = new SortedSet<int> { 0, 1, 3, 4, 8, 11, 12, 13, GooFileReader.HeaderLength - 1 };
        int[] fieldStarts = [0, 4, 12, 44, 92, 124, 156, 188, 190, 192, SmallPreviewAt, SmallDelimiterAt, BigPreviewAt, BigDelimiterAt,
            LayersAt, LayersAt + 4, LayerHeightAt, LayerHeightAt + 4, SecondsAt, SecondsAt + 4, VolumeAt, VolumeAt + 4, VolumeAt + 8];
        foreach (int at in fieldStarts)
            for (int d = -1; d <= 2; d++)
                if (at + d is >= 0 and < GooFileReader.HeaderLength) lengths.Add(at + d);
        var data = new TheoryData<int>();
        foreach (int n in lengths) data.Add(n);
        return data;
    }

    [Theory]
    [MemberData(nameof(TruncatedLengths))]
    public void Truncated_files_are_rejected(int length)
    {
        var bytes = TestFiles.GooHeader()[..length];
        ReaderContract.Rejects(() => GooFileReader.Parse("part.goo", bytes), $"truncated to {length} (Parse)");
        string path = Write(bytes);
        ReaderContract.Rejects(() => SliceFileReader.Read(path), $"truncated to {length} (Read)");
    }

    [Fact]
    public void Wrong_magic_is_rejected()
    {
        for (int i = 4; i < 12; i++)
        {
            var bytes = TestFiles.GooHeader();
            bytes[i] ^= 0x5A;
            ReaderContract.Rejects(() => GooFileReader.Parse("part.goo", bytes), $"magic byte {i} changed");
        }
    }

    [Theory]
    [InlineData("V3.0")]
    [InlineData("V9.9")]
    [InlineData("")]
    [InlineData("ÿþ\u0001\u0002")]
    public void Any_version_string_keeps_the_contract(string version)
    {
        var bytes = TestFiles.GooHeader();
        Array.Clear(bytes, 0, 4);
        System.Text.Encoding.Latin1.GetBytes(version).CopyTo(bytes, 0);
        Check(bytes, $"version \"{version}\"");
    }

    [Theory]
    [InlineData(SmallDelimiterAt)]
    [InlineData(SmallDelimiterAt + 1)]
    [InlineData(BigDelimiterAt)]
    [InlineData(BigDelimiterAt + 1)]
    public void Bad_delimiters_are_rejected(int at)
    {
        var bytes = TestFiles.GooHeader();
        bytes[at] = 0x00;
        ReaderContract.Rejects(() => GooFileReader.Parse("part.goo", bytes), $"delimiter byte at {at} cleared");
    }

    private static readonly float[] NonsenseFloats =
    [
        float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, float.MinValue, -float.Epsilon,
        -0.05f, -1f, 1e30f, -1e30f,
    ];

    public static TheoryData<float> BadLayerHeights() => new([.. NonsenseFloats, 5f, 5000f]);

    // mm³: 1000 L and 200 L are far beyond any resin vat.
    public static TheoryData<float> BadVolumes() => new([.. NonsenseFloats, 1e9f, 2e8f]);

    [Theory]
    [MemberData(nameof(BadLayerHeights))]
    public void Nonsense_layer_height_is_rejected(float value) =>
        ReaderContract.Rejects(() => GooFileReader.Parse("part.goo", TestFiles.GooHeader(layerHeight: value)), $"layer height {value}");

    [Theory]
    [MemberData(nameof(BadVolumes))]
    public void Nonsense_volume_is_rejected(float value)
    {
        // The spec stores the volume twice (mm³ and mL); the reader uses the first, so break only that one.
        var bytes = TestFiles.GooHeader();
        BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(VolumeAt), value);
        ReaderContract.Rejects(() => GooFileReader.Parse("part.goo", bytes), $"volume {value}");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(ReaderContract.MaxSeconds + 1)]
    public void Nonsense_print_time_is_rejected(int seconds) =>
        ReaderContract.Rejects(() => GooFileReader.Parse("part.goo", TestFiles.GooHeader(seconds: seconds)), $"{seconds} s");

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(ReaderContract.MaxLayers + 1)]
    public void Nonsense_layer_count_is_rejected(int layers) =>
        ReaderContract.Rejects(() => GooFileReader.Parse("part.goo", TestFiles.GooHeader(layers: layers)), $"{layers} layers");

    [Fact]
    public void Edge_values_that_are_plausible_still_read()
    {
        // A zero-second or zero-volume header is odd but harmless: the user sees and can override it.
        ReaderContract.Reads(() => GooFileReader.Parse("p.goo", TestFiles.GooHeader(seconds: 0)), "0 s");
        ReaderContract.Reads(() => GooFileReader.Parse("p.goo", TestFiles.GooHeader(volumeMm3: 0f)), "0 mm³");
        ReaderContract.Reads(() => GooFileReader.Parse("p.goo", TestFiles.GooHeader(layers: 0, layerHeight: 0f)), "no layer info");
        ReaderContract.Reads(() => GooFileReader.Parse("p.goo", TestFiles.GooHeader(layerHeight: 0.01f)), "0.01 mm layers");
        ReaderContract.Reads(() => GooFileReader.Parse("p.goo", TestFiles.GooHeader(volumeMm3: 2_500_000f)), "2.5 L");
        var tiny = ReaderContract.Reads(() => GooFileReader.Parse("p.goo", TestFiles.GooHeader(volumeMm3: 1f)), "1 mm³");
        Assert.Equal(0m, tiny.Plates[0].Resin!.Ml);
    }

    [Theory]
    [InlineData("zeros")]
    [InlineData("ones")]
    [InlineData("random")]
    [InlineData("stripes")]
    [InlineData("single pixel")]
    public void Garbage_previews_give_a_usable_png_or_none(string kind)
    {
        var bytes = TestFiles.GooHeader();
        var rng = new Random(1234);
        foreach (var (at, size) in new[] { (SmallPreviewAt, 116), (BigPreviewAt, 290) })
        {
            var span = bytes.AsSpan(at, 2 * size * size);
            switch (kind)
            {
                case "zeros": span.Clear(); break;
                case "ones": span.Fill(0xFF); break;
                case "random": rng.NextBytes(span); break;
                case "stripes": for (int i = 0; i < span.Length; i++) span[i] = (byte)(i / 2 % 7 == 0 ? 0xF8 : 0x00); break;
                case "single pixel": span.Clear(); span[span.Length / 2] = 0xFF; break;
            }
        }
        var file = Check(bytes, $"{kind} preview");
        Assert.NotNull(file);
        if (kind == "zeros") Assert.Null(file!.Plates[0].Thumbnail);
        else ReaderContract.AssertUsablePng(file!.Plates[0].Thumbnail!, kind);
    }
}

/// <summary>Seeded random damage to a .goo header; a separate class so xunit runs it alongside the other tests.</summary>
public sealed class GooRandomDamageTests
{
    [Fact]
    public void Random_byte_flips_keep_the_contract()
    {
        var valid = TestFiles.GooHeader();
        // Most of the header is preview pixels; aim half the flips at the fields and delimiters that are parsed.
        int[] parsed = [.. Enumerable.Range(0, GooRobustnessTests.SmallPreviewAt), GooRobustnessTests.SmallDelimiterAt, GooRobustnessTests.SmallDelimiterAt + 1, GooRobustnessTests.BigDelimiterAt, GooRobustnessTests.BigDelimiterAt + 1,
            .. Enumerable.Range(GooRobustnessTests.LayersAt, GooFileReader.HeaderLength - GooRobustnessTests.LayersAt)];

        for (int seed = 1; seed <= 100; seed++)
        {
            var rng = new Random(seed);
            var bytes = (byte[])valid.Clone();
            int flips = rng.Next(1, 9);
            for (int i = 0; i < flips; i++)
            {
                int at = rng.Next(2) == 0 ? parsed[rng.Next(parsed.Length)] : rng.Next(bytes.Length);
                bytes[at] = rng.Next(3) == 0 ? (byte)rng.Next(256) : (byte)(bytes[at] ^ (1 << rng.Next(8)));
            }
            ReaderContract.Check(() => GooFileReader.Parse("part.goo", bytes), $"byte flips, seed {seed}");
        }
    }

    [Fact]
    public void Random_field_values_keep_the_contract()
    {
        // Byte flips rarely produce extreme numbers, so also write whole random words into each numeric field.
        for (int seed = 1; seed <= 200; seed++)
        {
            var rng = new Random(seed);
            var bytes = TestFiles.GooHeader();
            foreach (int at in new[] { GooRobustnessTests.LayersAt, GooRobustnessTests.LayerHeightAt, GooRobustnessTests.SecondsAt, GooRobustnessTests.VolumeAt })
                if (rng.Next(2) == 0) rng.NextBytes(bytes.AsSpan(at, 4));
            ReaderContract.Check(() => GooFileReader.Parse("part.goo", bytes), $"random fields, seed {seed}");
        }
    }
}

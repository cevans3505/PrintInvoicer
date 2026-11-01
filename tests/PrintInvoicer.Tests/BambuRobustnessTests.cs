using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using PrintInvoicer.Core.Slicing;

namespace PrintInvoicer.Tests;

/// <summary>Builders and checks shared by the Bambu reader robustness tests.</summary>
public abstract class BambuReaderTests : IDisposable
{
    protected const string SliceInfo = "Metadata/slice_info.config", Settings = "Metadata/project_settings.config";

    protected readonly string Dir = Path.Combine(Path.GetTempPath(), $"pi-bambu-fuzz-{Guid.NewGuid():N}");

    protected BambuReaderTests() => Directory.CreateDirectory(Dir);

    public void Dispose() => Directory.Delete(Dir, recursive: true);

    protected string Write(byte[] bytes, string name = "part.gcode.3mf")
    {
        string path = Path.Combine(Dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    protected SlicedFile? Check(byte[] bytes, string label, string name = "part.gcode.3mf")
    {
        string path = Write(bytes, name);
        return ReaderContract.Check(() => SliceFileReader.Read(path), label);
    }

    protected SlicedFile Reads(byte[] bytes, string label, string name = "part.gcode.3mf")
    {
        string path = Write(bytes, name);
        return ReaderContract.Reads(() => SliceFileReader.Read(path), label);
    }

    protected InvalidDataException Rejects(byte[] bytes, string label, string name = "part.gcode.3mf")
    {
        string path = Write(bytes, name);
        return ReaderContract.Rejects(() => SliceFileReader.Read(path), label);
    }

    protected static byte[] Zip(params (string Name, byte[] Data)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                using var s = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
                s.Write(data);
            }
        }
        return ms.ToArray();
    }

    protected static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    protected static readonly byte[] SmallPng = PngWriter.EncodeRgba(2, 2, [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 0, 0, 0, 0]);

    protected static string Plate(string metadata = """<metadata key="index" value="1"/><metadata key="prediction" value="3600"/>""",
        string filament = """<filament id="1" type="PLA" color="#FF0000" used_g="12.5"/>""") =>
        $"""<?xml version="1.0" encoding="UTF-8"?><config><plate>{metadata}<object name="Harbor_Clip.stl" skipped="false"/>{filament}</plate></config>""";

    protected static byte[] ThreeMf(string sliceInfo, string? settings = null, byte[]? png = null)
    {
        var entries = new List<(string, byte[])> { (SliceInfo, Utf8(sliceInfo)) };
        if (settings != null) entries.Add((Settings, Utf8(settings)));
        if (png != null) entries.Add(("Metadata/plate_1.png", png));
        return Zip([.. entries]);
    }

    protected static List<(string Name, byte[] Data)> FixtureEntries(string fixture)
    {
        using var zip = ZipFile.OpenRead(TestFiles.Fixture(fixture));
        return zip.Entries.Select(e =>
        {
            using var ms = new MemoryStream();
            using (var s = e.Open()) s.CopyTo(ms);
            return (e.FullName, ms.ToArray());
        }).ToList();
    }

    /// <summary>The invoice code calls ReadPlateImage directly; it must return null or a usable PNG, never throw.</summary>
    protected static void AssertPlateImageSafe(string path, int plate, string label)
    {
        byte[]? png = null;
        var task = Task.Run(() => png = BambuFileReader.ReadPlateImage(path, plate));
        try
        {
            if (!task.Wait(ReaderContract.Timeout)) Assert.Fail($"{label}: did not finish.");
        }
        catch (AggregateException ex)
        {
            Assert.Fail($"{label}: threw {ex.InnerException!.GetType().FullName}: {ex.InnerException.Message}");
        }
        if (png != null) ReaderContract.AssertUsablePng(png, label);
    }

    protected static string Gcode(string time = "1h 2m 3s", string weights = "10.50,0.00,2.25", string? labels = "11,12") =>
        string.Join("\n",
            "; HEADER_BLOCK_START",
            $"; model printing time: 1h 0m 0s; total estimated time: {time}",
            $"; total filament weight [g] : {weights}",
            labels == null ? "; nothing" : $"; model label id: {labels}",
            "; HEADER_BLOCK_END",
            "; CONFIG_BLOCK_START",
            "; filament_type = PLA;PLA;PETG",
            "; filament_vendor = \"Harbor Dive Shop\";Generic",
            "; printer_model = Bambu Lab H2D",
            "; CONFIG_BLOCK_END",
            "G28") + "\n";

    protected static byte[] RandomBytes(int n, int seed)
    {
        var b = new byte[n];
        new Random(seed).NextBytes(b);
        return b;
}

/// <summary>Malformed Bambu Studio files: sensible values or a rejection with the reader's own message, nothing else.</summary>
public sealed class BambuRobustnessTests : BambuReaderTests
{
    // ---- .gcode.3mf: the container ----

    [Fact]
    public void Synthetic_file_reads()
    {
        var plate = Assert.Single(Reads(ThreeMf(Plate(), png: SmallPng), "synthetic").Plates);
        Assert.Equal(3600, plate.PredictedSeconds);
        Assert.Equal(12.5m, Assert.Single(plate.Filaments).Grams);
        Assert.Equal("Harbor_Clip", plate.PartName);
        Assert.Equal(SmallPng, plate.Thumbnail);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("text")]
    [InlineData("gcode")]
    [InlineData("png")]
    [InlineData("random")]
    [InlineData("zip signature only")]
    [InlineData("truncated zip")]
    public void Files_that_are_not_zips_are_rejected(string kind)
    {
        byte[] bytes = kind switch
        {
            "empty" => [],
            "text" => Utf8("Harbor Dive Shop order notes"),
            "gcode" => Utf8("; HEADER_BLOCK_START\nG28\n"),
            "png" => SmallPng,
            "random" => RandomBytes(5000, 7),
            "zip signature only" => [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0],
            _ => ThreeMf(Plate())[..60],
        };
        var ex = Rejects(bytes, kind);
        Assert.Contains(".gcode.3mf", ex.Message);
    }

    [Fact]
    public void Zip_without_metadata_is_rejected()
    {
        Rejects(Zip(), "empty zip");
        Rejects(Zip(("3D/3dmodel.model", Utf8("<model/>"))), "model only");
        Rejects(Zip((Settings, Utf8("{}"))), "settings only");
        Rejects(Zip(("metadata/SLICE_INFO.CONFIG", Utf8(Plate()))), "wrong case");
    }

    [Theory]
    [InlineData("")]
    [InlineData("<")]
    [InlineData("<config>")]
    [InlineData("<config/>")]
    [InlineData("<other><plate/></other>")]
    [InlineData("<config><plate></plate></config>")]
    [InlineData("<config><plate><filament id=\"1\" used_g=\"5\"></config>")]
    [InlineData("not xml at all")]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE config [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\">]><config>&b;</config>")]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE config SYSTEM \"http://example.invalid/x.dtd\"><config/>")]
    [InlineData("<config><plate><filament id=\"1\" used_g=\"5\"/></plate></config><extra/>")]
    [InlineData("\u0000\u0001\u0002")]
    public void Malformed_slice_info_is_rejected(string xml) => Rejects(ThreeMf(xml), $"slice_info \"{xml}\"");

    [Fact]
    public void Non_utf8_slice_info_is_rejected()
    {
        var bytes = Zip((SliceInfo, [0x3C, 0x63, 0xFF, 0xFE, 0x80, 0x3E]));
        Rejects(bytes, "invalid UTF-8");
    }

    [Fact]
    public void Deeply_nested_slice_info_is_rejected_quickly()
    {
        // About 1 MB of XML. Building an XDocument from it takes minutes, because that cost grows with the square of the depth.
        const int depth = 150_000;
        string xml = "<config>" + string.Concat(Enumerable.Repeat("<a>", depth)) + string.Concat(Enumerable.Repeat("</a>", depth)) + "</config>";
        var bytes = ThreeMf(xml);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Assert.Contains("nested", Rejects(bytes, $"{depth} nested elements").Message);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), $"took {timer.Elapsed}");
        Reads(ThreeMf(Plate().Replace("<plate>", "<plate><a><b><c/></b></a>")), "a little extra nesting");
    }

    public static TheoryData<string, string> BadPlateValues() => new()
    {
        { "index", "abc" }, { "index", "" }, { "index", "-1" }, { "index", "0" }, { "index", "99999999999" }, { "index", "1.5" }, { "index", "100000" },
        { "prediction", "abc" }, { "prediction", "" }, { "prediction", "-5" }, { "prediction", "NaN" }, { "prediction", "Infinity" },
        { "prediction", "1e30" }, { "prediction", "79228162514264337593543950336" }, { "prediction", "99999999999" },
        { "prediction", "2147483647" },
    };

    [Theory]
    [MemberData(nameof(BadPlateValues))]
    public void Nonsense_plate_metadata_is_rejected(string key, string value)
    {
        string meta = key == "index"
            ? $"""<metadata key="index" value="{value}"/><metadata key="prediction" value="3600"/>"""
            : $"""<metadata key="index" value="1"/><metadata key="prediction" value="{value}"/>""";
        Rejects(ThreeMf(Plate(metadata: meta)), $"{key}=\"{value}\"");
    }

    [Fact]
    public void Missing_prediction_is_rejected() =>
        Rejects(ThreeMf(Plate(metadata: """<metadata key="index" value="1"/>""")), "no prediction");

    [Fact]
    public void Missing_index_means_plate_1()
    {
        var plate = Assert.Single(Reads(ThreeMf(Plate(metadata: """<metadata key="prediction" value="60"/>""")), "no index").Plates);
        Assert.Equal(1, plate.Index);
    }

    [Theory]
    [InlineData("""<filament id="1" used_g="abc"/>""")]
    [InlineData("""<filament id="1" used_g=""/>""")]
    [InlineData("""<filament id="1" used_g="-3"/>""")]
    [InlineData("""<filament id="1" used_g="NaN"/>""")]
    [InlineData("""<filament id="1" used_g="1e40"/>""")]
    [InlineData("""<filament id="1" used_g="79228162514264337593543950336"/>""")]
    [InlineData("""<filament id="1" used_g="500000"/>""")]
    [InlineData("""<filament id="1"/>""")]
    [InlineData("""<filament id="x" used_g="5"/>""")]
    [InlineData("""<filament id="-1" used_g="5"/>""")]
    [InlineData("""<filament id="0" used_g="5"/>""")]
    [InlineData("""<filament id="99999999999" used_g="5"/>""")]
    [InlineData("""<filament used_g="5"/>""")]
    [InlineData("""<filament/>""")]
    [InlineData("""<filament id="1" used_g="5"/><filament id="2" used_g="oops"/>""")]
    public void Nonsense_filament_attributes_are_rejected(string filament) =>
        Rejects(ThreeMf(Plate(filament: filament)), filament);

    [Theory]
    [InlineData("""<filament id="1" used_g="5"/>""")]
    [InlineData("""<filament id="1" used_g="0"/><filament id="2" used_g="5"/>""")]
    [InlineData("""<filament id="1" type="" color="" used_g="5"/>""")]
    [InlineData("""<filament id="1" type="PLA" color="not a colour" used_g="5"/>""")]
    [InlineData("""<filament id="1" used_g=" 5.5 "/>""")]
    public void Odd_but_usable_filament_attributes_read(string filament)
    {
        var plate = Assert.Single(Reads(ThreeMf(Plate(filament: filament)), filament).Plates);
        Assert.Single(plate.Filaments);
    }

    [Fact]
    public void Objects_without_names_read()
    {
        string xml = Plate().Replace("""<object name="Harbor_Clip.stl" skipped="false"/>""", "<object/><object skipped=\"maybe\"/>");
        var plate = Assert.Single(Reads(ThreeMf(xml), "unnamed objects").Plates);
        Assert.Equal(2, plate.ObjectCount);
    }

    // ---- .gcode.3mf: project_settings.config is only a matching hint, so bad JSON is ignored ----

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"just a string\"")]
    [InlineData("42")]
    [InlineData("{\"filament_vendor\": 5, \"filament_settings_id\": {\"a\": 1}, \"printer_model\": []}")]
    [InlineData("{\"filament_vendor\": [null, {\"x\": [1]}, 3.5], \"printer_model\": null}")]
    [InlineData("{\"filament_vendor\": [\"a\"], \"filament_vendor\": [\"b\"]}")]
    [InlineData("not json")]
    public void Malformed_project_settings_are_ignored(string json) =>
        Assert.Single(Reads(ThreeMf(Plate(), json), $"settings \"{json}\"").Plates);

    [Fact]
    public void Deeply_nested_or_binary_project_settings_are_ignored()
    {
        Reads(ThreeMf(Plate(), new string('[', 5000) + new string(']', 5000)), "5000 nested arrays");
        Reads(Zip((SliceInfo, Utf8(Plate())), (Settings, [0x7B, 0xFF, 0xFE, 0x80, 0x7D])), "invalid UTF-8 settings");
    }

    // ---- .gcode.3mf: plate pictures ----

    [Fact]
    public void Missing_plate_pictures_give_no_thumbnail()
    {
        string xml = Plate(metadata: """<metadata key="index" value="7"/><metadata key="prediction" value="60"/>""");
        var plate = Assert.Single(Reads(ThreeMf(xml, png: SmallPng), "plate 7 without picture").Plates);
        Assert.Null(plate.Thumbnail);
        Assert.Null(BambuFileReader.ReadPlateImage(Write(ThreeMf(xml)), 7));
    }

    public static TheoryData<string> BadPngs() => new() { "empty", "random", "signature only", "huge claimed size", "zero size", "negative size", "no IHDR" };

    private static byte[] BadPng(string kind)
    {
        var png = (byte[])SmallPng.Clone();
        switch (kind)
        {
            case "empty": return [];
            case "random": return RandomBytes(3000, 11);
            case "signature only": return png[..8];
            case "huge claimed size":
                BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 100_000);
                BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), 100_000);
                return png;
            case "zero size": BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 0); return png;
            case "negative size": BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), -5); return png;
            default: png[12] = (byte)'X'; return png;
        }
    }

    [Theory]
    [MemberData(nameof(BadPngs))]
    public void Corrupt_plate_pictures_are_dropped(string kind)
    {
        var bytes = ThreeMf(Plate(), png: BadPng(kind));
        var plate = Assert.Single(Reads(bytes, $"{kind} PNG").Plates);
        Assert.Null(plate.Thumbnail);
        Assert.Null(BambuFileReader.ReadPlateImage(Write(bytes), 1));
    }

    [Fact]
    public void Small_picture_is_used_when_the_big_one_is_corrupt()
    {
        var bytes = Zip((SliceInfo, Utf8(Plate())), ("Metadata/plate_1.png", BadPng("random")), ("Metadata/plate_1_small.png", SmallPng));
        Assert.Equal(SmallPng, Assert.Single(Reads(bytes, "corrupt big picture").Plates).Thumbnail);
    }

    // ---- .gcode.3mf: entries that inflate to far more than any real file (each test file stays well under 1 MB) ----

    private static byte[] ZipWithBomb(string bombName, int inflatedBytes, params (string Name, byte[] Data)[] others)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in others)
            {
                using var s = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
                s.Write(data);
            }
            using var bomb = zip.CreateEntry(bombName, CompressionLevel.SmallestSize).Open();
            var chunk = new byte[1 << 20];
            "<config>"u8.CopyTo(chunk);
            for (int written = 0; written < inflatedBytes; written += chunk.Length)
            {
                bomb.Write(chunk);
                if (written == 0) chunk.AsSpan(0, 8).Clear();
            }
        }
        return ms.ToArray();
    }

    [Fact]
    public void Huge_slice_info_is_rejected()
    {
        var bytes = ZipWithBomb(SliceInfo, 64 << 20);
        Assert.True(bytes.Length < 1 << 20);
        Assert.Contains("too large", Rejects(bytes, "64 MB slice_info").Message);
    }

    [Fact]
    public void Huge_project_settings_and_picture_are_skipped()
    {
        var plate = Assert.Single(Reads(ZipWithBomb(Settings, 64 << 20, (SliceInfo, Utf8(Plate()))), "64 MB settings").Plates);
        Assert.Single(plate.Filaments);

        var bytes = ZipWithBomb("Metadata/plate_1.png", 64 << 20, (SliceInfo, Utf8(Plate())));
        Assert.Null(Assert.Single(Reads(bytes, "64 MB picture").Plates).Thumbnail);
        Assert.Null(BambuFileReader.ReadPlateImage(Write(bytes), 1));
    }

    [Fact]
    public void Entry_sizes_that_lie_keep_the_contract()
    {
        // Rewrite the recorded sizes of every entry so the directory understates (and then overstates) what is inside.
        foreach (uint claimed in new uint[] { 10, 0, 0xFFFFFFF0 })
        {
            var bytes = ZipWithBomb("Metadata/plate_1.png", 8 << 20, (SliceInfo, Utf8(Plate())));
            PatchSizes(bytes, claimed);
            Check(bytes, $"sizes rewritten to {claimed}");
            AssertPlateImageSafe(Write(bytes), 1, $"ReadPlateImage, sizes rewritten to {claimed}");
        }
    }

    /// <summary>Overwrites the uncompressed size in each local header and central directory record.</summary>
    private static void PatchSizes(byte[] zip, uint size)
    {
        for (int i = 0; i + 30 <= zip.Length; i++)
        {
            uint sig = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(i));
            if (sig == 0x04034B50) BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(i + 22), size);
            else if (sig == 0x02014B50 && i + 46 <= zip.Length) BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(i + 24), size);
        }
    }

    // ---- plain .gcode ----

    [Fact]
    public void Synthetic_gcode_reads()
    {
        var plate = Assert.Single(Reads(Utf8(Gcode()), "gcode", "part.gcode").Plates);
        Assert.Equal(3723, plate.PredictedSeconds);
        Assert.Equal(2, plate.Filaments.Count);
        Assert.Equal(2, plate.ObjectCount);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("binary")]
    [InlineData("no header")]
    [InlineData("zip")]
    public void Gcode_without_statistics_is_rejected(string kind)
    {
        byte[] bytes = kind switch
        {
            "empty" => [],
            "binary" => RandomBytes(20000, 3),
            "no header" => Utf8("G28\nG1 X10 Y10\n"),
            _ => ThreeMf(Plate()),
        };
        Rejects(bytes, kind, "part.gcode");
    }

    [Theory]
    [InlineData("99999999999d")]
    [InlineData("2147483647s")]
    [InlineData("24855d")]
    [InlineData("100d")]
    [InlineData("5")]
    [InlineData("   ")]
    [InlineData("d h m s")]
    public void Nonsense_gcode_time_is_rejected(string time) =>
        Rejects(Utf8(Gcode(time: time)), $"time \"{time}\"", "part.gcode");

    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1e40")]
    [InlineData("NaN")]
    [InlineData("10.5,abc")]
    [InlineData("500000")]
    [InlineData("79228162514264337593543950336")]
    public void Nonsense_gcode_weights_are_rejected(string weights) =>
        Rejects(Utf8(Gcode(weights: weights)), $"weights \"{weights}\"", "part.gcode");

    [Theory]
    [InlineData("")]
    [InlineData(",,,")]
    [InlineData("0,0")]
    [InlineData("5,,2")]
    [InlineData(" 5 , 2 ")]
    public void Odd_but_usable_gcode_weights_read(string weights) =>
        Reads(Utf8(Gcode(weights: weights)), $"weights \"{weights}\"", "part.gcode");

    [Fact]
    public void Gcode_label_lists_keep_the_contract()
    {
        Reads(Utf8(Gcode(labels: "")), "empty labels", "part.gcode");
        Reads(Utf8(Gcode(labels: null)), "no labels", "part.gcode");
        Check(Utf8(Gcode(labels: string.Join(",", Enumerable.Range(1, 200_000)))), "200000 labels", "part.gcode");
    }

    [Fact]
    public void Gcode_without_config_end_or_newlines_finishes()
    {
        Reads(Utf8(Gcode().Replace("; CONFIG_BLOCK_END", "; nope") + new string('G', 2 << 20)), "no config end", "part.gcode");
        // A 4 MB single line, as a binary file renamed to .gcode would be.
        Rejects(Encoding.ASCII.GetBytes(";" + new string('x', 4 << 20)), "one long line", "part.gcode");
        Check(Utf8(Gcode().Replace("; filament_type = PLA;PLA;PETG", "; filament_type = " + new string('P', 3 << 20))), "long config line", "part.gcode");
    }

    [Theory]
    [InlineData("1d 2h 3m 4s", 93784)]
    [InlineData("0s", 0)]
    public void Parse_duration_still_parses(string text, int seconds) => Assert.Equal(seconds, BambuFileReader.ParseDuration(text));

    [Theory]
    [InlineData("99999999999d")]
    [InlineData("2147483647h")]
    [InlineData("24856d")]
    [InlineData("99999999999999999999999s")]
    public void Parse_duration_rejects_overflow(string text) =>
        Assert.Throws<InvalidDataException>(() => BambuFileReader.ParseDuration(text));

    // ---- dispatch ----

    [Theory]
    [InlineData("part.stl")]
    [InlineData("part.ctb")]
    [InlineData("part")]
    [InlineData("part.gcode.3mf.bak")]
    [InlineData("part.gcode.txt")]
    [InlineData(".hidden")]
    public void Unknown_extensions_are_rejected(string name)
    {
        Rejects(ThreeMf(Plate()), name, name);
        Rejects([], $"{name}, empty", name);
    }

    [Theory]
    [InlineData("part.goo")]
    [InlineData("part.GOO")]
    [InlineData("part.gcode")]
    [InlineData("part.GCODE")]
    [InlineData("part.gcode.3mf")]
    [InlineData("part.3mf")]
    public void Zero_byte_files_are_rejected(string name) => Rejects([], name, name);

}
}

/// <summary>Seeded random damage to Bambu files; a separate class so xunit runs it alongside the other tests.</summary>
public sealed class BambuRandomDamageTests : BambuReaderTests
{
    // ---- .gcode.3mf: random damage to the real sample files ----

    [Theory]
    [InlineData("Cylindex_Tube_Cap.gcode.3mf")]
    [InlineData("Wire_Brush_Insert.gcode.3mf")]
    public void Random_byte_flips_over_fixtures_keep_the_contract(string fixture)
    {
        var valid = File.ReadAllBytes(TestFiles.Fixture(fixture));
        for (int seed = 1; seed <= 150; seed++)
        {
            var rng = new Random(seed);
            var bytes = (byte[])valid.Clone();
            int flips = rng.Next(1, 9);
            for (int i = 0; i < flips; i++) bytes[rng.Next(bytes.Length)] ^= (byte)(1 << rng.Next(8));
            Check(bytes, $"{fixture}, byte flips, seed {seed}");
            AssertPlateImageSafe(Path.Combine(Dir, "part.gcode.3mf"), 1, $"{fixture}, ReadPlateImage, seed {seed}");
        }
    }

    [Theory]
    [InlineData("Cylindex_Tube_Cap.gcode.3mf", SliceInfo)]
    [InlineData("Wire_Brush_Insert.gcode.3mf", SliceInfo)]
    [InlineData("Wire_Brush_Insert.gcode.3mf", Settings)]
    public void Random_text_damage_inside_fixtures_keeps_the_contract(string fixture, string entry)
    {
        // Byte flips mostly break the compression; damaging the text and zipping it again reaches the parsers.
        var entries = FixtureEntries(fixture);
        int target = entries.FindIndex(e => e.Name == entry);
        string text = Encoding.UTF8.GetString(entries[target].Data);
        for (int seed = 1; seed <= 120; seed++)
        {
            var copy = entries.ToList();
            copy[target] = (entry, Utf8(Damage(text, new Random(seed))));
            Check(Zip([.. copy]), $"{fixture} {entry}, text damage, seed {seed}");
        }
    }

    /// <summary>A few edits aimed at what parsers trip on: numbers, quotes, angle brackets, truncation.</summary>
    private static string Damage(string text, Random rng)
    {
        var sb = new StringBuilder(text);
        string[] junk = ["-", "e99", "NaN", "\"", "<", ">", "&", "x", "", "99999999999", "-1", "\0", "]", "}", ","];
        int edits = rng.Next(1, 6);
        for (int i = 0; i < edits && sb.Length > 0; i++)
        {
            int at = rng.Next(sb.Length);
            // Prefer digits so numeric attributes get hit.
            for (int tries = 0; tries < 50 && !char.IsDigit(sb[at]); tries++) at = rng.Next(sb.Length);
            switch (rng.Next(5))
            {
                case 0: sb.Remove(at, Math.Min(rng.Next(1, 20), sb.Length - at)); break;
                case 1: sb.Insert(at, junk[rng.Next(junk.Length)]); break;
                case 2: sb[at] = junk[rng.Next(junk.Length)] is { Length: > 0 } j ? j[0] : ' '; break;
                case 3: sb.Length = at; break;
                default: sb.Insert(at, new string('9', rng.Next(5, 40))); break;
            }
        }
        return sb.ToString();
    }

    [Fact]
    public void Random_text_damage_to_gcode_keeps_the_contract()
    {
        string valid = Gcode();
        for (int seed = 1; seed <= 200; seed++)
            Check(Utf8(Damage(valid, new Random(seed))), $"gcode damage, seed {seed}", "part.gcode");
    }
}

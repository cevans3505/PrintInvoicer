using PrintInvoicer.Core.Slicing;

namespace PrintInvoicer.Tests;

public class BambuFileReaderTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void Reads_multi_color_plate_from_gcode_3mf()
    {
        var file = BambuFileReader.Read(Fixture("Cylindex_Tube_Cap.gcode.3mf"));

        Assert.Equal("Bambu Lab H2D", file.PrinterModel);
        var plate = Assert.Single(file.Plates);
        Assert.Equal(1, plate.Index);
        Assert.Equal(21354, plate.PredictedSeconds);
        Assert.Equal(20, plate.ObjectCount);
        Assert.Equal("Cylindex_Tube_Cap", plate.PartName);
        Assert.False(plate.HasMixedObjects);
        Assert.NotNull(plate.Thumbnail);

        Assert.Equal(new[] { 8.35m, 5.22m, 5.56m, 5.40m, 155.39m }, plate.Filaments.Select(f => f.Grams));
        Assert.Equal(179.92m, plate.TotalGrams); // slot sum; slice_info's own total says 179.93 due to rounding

        var white = plate.Filaments[0];
        Assert.Equal(1, white.SlotId);
        Assert.Equal("PLA", white.Type);
        Assert.Equal("#FFFFFF", white.ColorHex);
        Assert.Equal("Creality", white.Vendor);
        Assert.Equal("Creality PLA Basic", white.Profile);

        var black = plate.Filaments[4];
        Assert.Equal("Bambu Lab", black.Vendor);
        Assert.Equal("Bambu PLA Basic", black.Profile);
    }

    [Theory]
    [InlineData("5h 55m 54s", 21354)]
    [InlineData("1d 2h 3m 4s", 93784)]
    [InlineData("45m 0s", 2700)]
    public void Parses_bambu_durations(string text, int seconds) =>
        Assert.Equal(seconds, BambuFileReader.ParseDuration(text));

    [Fact]
    public void Rejects_unsliced_project()
    {
        string path = Path.Combine(Path.GetTempPath(), $"unsliced-{Guid.NewGuid():N}.3mf");
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            zip.CreateEntry("3D/3dmodel.model");
        }
        try
        {
            Assert.Throws<InvalidDataException>(() => BambuFileReader.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reads_plain_gcode_header()
    {
        string path = Path.Combine(Path.GetTempPath(), $"plain-{Guid.NewGuid():N}.gcode");
        File.WriteAllLines(path, new[]
        {
            "; HEADER_BLOCK_START",
            "; model printing time: 1h 0m 0s; total estimated time: 1h 2m 3s",
            "; total filament weight [g] : 10.50,0.00,2.25",
            "; model label id: 11,12,13,14",
            "; HEADER_BLOCK_END",
            "; CONFIG_BLOCK_START",
            "; filament_colour = #ffffff;#00FF00;#000000",
            "; filament_settings_id = \"Bambu PLA Basic @BBL H2D\";\"Bambu PLA Basic @BBL H2D\";\"Generic PETG @BBL H2D\"",
            "; filament_type = PLA;PLA;PETG",
            "; filament_vendor = \"Bambu Lab\";\"Bambu Lab\";Generic",
            "; CONFIG_BLOCK_END",
            "G28",
        });
        try
        {
            var plate = Assert.Single(BambuFileReader.Read(path).Plates);
            Assert.Equal(3723, plate.PredictedSeconds);
            Assert.Equal(4, plate.ObjectCount);
            Assert.Equal(2, plate.Filaments.Count); // the 0 g slot is dropped
            Assert.Equal(3, plate.Filaments[1].SlotId);
            Assert.Equal("PETG", plate.Filaments[1].Type);
            Assert.Equal("Generic", plate.Filaments[1].Vendor);
            Assert.Equal("Generic PETG", plate.Filaments[1].Profile);
            Assert.Equal("#FFFFFF", plate.Filaments[0].ColorHex);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

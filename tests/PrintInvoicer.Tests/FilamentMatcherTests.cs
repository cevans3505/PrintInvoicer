using PrintInvoicer.Core.Models;
using PrintInvoicer.Core.Pricing;

namespace PrintInvoicer.Tests;

public class FilamentMatcherTests
{
    private static readonly List<Filament> Filaments = new()
    {
        new() { Id = 1, Vendor = "Bambu Lab", Material = "PLA Basic", ColorHex = "" },
        new() { Id = 2, Vendor = "Creality", Material = "PLA", ColorName = "White", ColorHex = "#FFFFFF" },
        new() { Id = 3, Vendor = "Creality", Material = "PLA", ColorName = "Black", ColorHex = "#000000" },
        new() { Id = 4, Vendor = "Bambu Lab", Material = "PETG HF", ColorHex = "#000000" },
        new() { Id = 5, Vendor = "Creality", Material = "PLA", ColorName = "Old", ColorHex = "#123456", Active = false },
    };

    private static PlateFilament Slot(string vendor, string type, string hex) =>
        new() { SlicerVendor = vendor, SlicerType = type, SlicerColorHex = hex };

    [Fact]
    public void Color_and_vendor_beat_vendor_alone() =>
        Assert.Equal(3, FilamentMatcher.Match(Slot("Creality", "PLA", "#000000"), Filaments, new Dictionary<string, long>())?.Id);

    [Fact]
    public void Falls_back_to_vendor_and_material() =>
        Assert.Equal(1, FilamentMatcher.Match(Slot("Bambu Lab", "PLA", "#FF0000"), Filaments, new Dictionary<string, long>())?.Id);

    [Fact]
    public void Material_must_match() =>
        Assert.Null(FilamentMatcher.Match(Slot("Generic", "TPU", "#000000"), Filaments, new Dictionary<string, long>()));

    [Fact]
    public void Remembered_choice_wins()
    {
        var slot = Slot("Creality", "PLA", "#000000");
        var remembered = new Dictionary<string, long> { [FilamentMatcher.MappingKey(slot)] = 2 };
        Assert.Equal(2, FilamentMatcher.Match(slot, Filaments, remembered)?.Id);
    }

    [Fact]
    public void Inactive_filaments_are_skipped() =>
        Assert.NotEqual(5, FilamentMatcher.Match(Slot("Creality", "PLA", "#123456"), Filaments, new Dictionary<string, long>())?.Id);
}

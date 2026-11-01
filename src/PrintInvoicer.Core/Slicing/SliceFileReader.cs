namespace PrintInvoicer.Core.Slicing;

/// <summary>Picks the reader for a sliced file by its extension.</summary>
public static class SliceFileReader
{
    /// <summary>Open-file dialog filter covering every format the app reads.</summary>
    public const string DialogFilter =
        "Sliced files (*.gcode.3mf;*.gcode;*.goo)|*.gcode.3mf;*.gcode;*.goo|" +
        "Bambu Studio (*.gcode.3mf;*.gcode)|*.gcode.3mf;*.gcode|ELEGOO resin (*.goo)|*.goo|All files (*.*)|*.*";

    /// <summary>
    /// Reads a sliced file with the reader for its extension: .goo for ELEGOO resin, .3mf and .gcode for Bambu Studio.
    /// Any other extension is refused with a message pointing to Add plate by hand.
    /// </summary>
    public static SlicedFile Read(string path)
    {
        if (GooFileReader.CanRead(path)) return GooFileReader.Read(path);
        if (path.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase))
            return BambuFileReader.Read(path);
        throw new InvalidDataException(
            "This file type is not read automatically. Supported: Bambu Studio .gcode.3mf and .gcode, and ELEGOO .goo resin files. " +
            "For other slicers, use Add plate by hand and type in the print time and material.");
    }
}

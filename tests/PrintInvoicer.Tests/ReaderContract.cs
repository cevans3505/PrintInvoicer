using System.Buffers.Binary;
using PrintInvoicer.Core.Slicing;

namespace PrintInvoicer.Tests;

/// <summary>
/// The promise every sliced-file reader makes: for any input it either returns values that are safe to price or throws
/// an <see cref="InvalidDataException"/> of its own with a readable message. Anything else (another exception type, a
/// hang, a raw parser message, a negative or absurd number) breaks the contract.
/// </summary>
internal static class ReaderContract
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    // What "sensible" means for values leaving a reader. Generous on purpose: no real print comes near these.
    public const int MaxSeconds = 60 * 86400;
    public const int MaxLayers = 1_000_000;
    public const decimal MaxLayerHeightMm = 1m;
    public const decimal MaxMl = 100_000m;
    public const decimal MaxGrams = 100_000m;
    public const int MaxPlateIndex = 999;
    public const int MaxSlotId = 999;
    public const int MaxObjects = 100_000;
    public const int MaxThumbnailBytes = 16 * 1024 * 1024;
    public const int MaxThumbnailSide = 4096;

    /// <summary>Runs the reader and checks the contract. Returns the file, or null when it was properly rejected.</summary>
    public static SlicedFile? Check(Func<SlicedFile> read, string label)
    {
        SlicedFile? file = null;
        Exception? error = null;
        var task = Task.Run(() =>
        {
            try { file = read(); }
            catch (Exception ex) { error = ex; }
        });
        if (!task.Wait(Timeout)) Assert.Fail($"{label}: the reader did not finish within {Timeout.TotalSeconds:0} s.");

        if (error is not null)
        {
            if (error is not InvalidDataException)
                Assert.Fail($"{label}: threw {error.GetType().FullName}: {error.Message}\n{error.StackTrace}");
            AssertOwnMessage((InvalidDataException)error, label);
            return null;
        }

        Assert.NotNull(file);
        AssertSensible(file!, label);
        return file;
    }

    /// <summary>Runs the reader and requires a rejection.</summary>
    public static InvalidDataException Rejects(Func<SlicedFile> read, string label)
    {
        var file = Check(read, label);
        if (file is not null) Assert.Fail($"{label}: expected the file to be rejected, but it was read.");
        try { read(); }
        catch (InvalidDataException ex) { return ex; }
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>Runs the reader and requires it to read the file.</summary>
    public static SlicedFile Reads(Func<SlicedFile> read, string label)
    {
        var file = Check(read, label);
        if (file is null)
        {
            string message = "";
            try { read(); } catch (InvalidDataException ex) { message = ex.Message; }
            Assert.Fail($"{label}: expected the file to be read, but it was rejected: {message}");
        }
        return file!;
    }

    private static void AssertOwnMessage(InvalidDataException ex, string label)
    {
        Assert.False(string.IsNullOrWhiteSpace(ex.Message), $"{label}: rejection has no message.");
        Assert.True(ex.Message.Length <= 400, $"{label}: rejection message is too long to show: {ex.Message}");
        // The message must be one of the readers' own, not a parser's or the zip library's.
        string? thrower = ex.TargetSite?.DeclaringType?.FullName;
        Assert.True(thrower?.StartsWith("PrintInvoicer.Core.Slicing.", StringComparison.Ordinal) == true,
            $"{label}: InvalidDataException from {thrower ?? "unknown"} reached the user unwrapped: {ex.Message}");
    }

    public static void AssertSensible(SlicedFile file, string label)
    {
        Assert.NotNull(file.Path);
        Assert.NotNull(file.Plates);
        Assert.InRange(file.Plates.Count, 1, MaxPlateIndex);
        foreach (var plate in file.Plates)
        {
            string at = $"{label}, plate {plate.Index}";
            Assert.True(plate.Index is >= 1 and <= MaxPlateIndex, $"{at}: plate index {plate.Index}");
            Assert.True(plate.PredictedSeconds is >= 0 and <= MaxSeconds, $"{at}: {plate.PredictedSeconds} seconds");
            Assert.True(plate.LayerCount is >= 0 and <= MaxLayers, $"{at}: {plate.LayerCount} layers");
            Assert.True(plate.LayerHeightMm is >= 0 and <= MaxLayerHeightMm, $"{at}: layer height {plate.LayerHeightMm}");
            Assert.NotNull(plate.ObjectNames);
            Assert.True(plate.ObjectCount <= MaxObjects, $"{at}: {plate.ObjectCount} objects");
            Assert.All(plate.ObjectNames, Assert.NotNull);
            Assert.NotNull(plate.PartName);

            if (plate.Resin is { } resin)
            {
                Assert.NotNull(resin.Profile);
                Assert.True(resin.Ml is >= 0 and <= MaxMl, $"{at}: {resin.Ml} mL");
            }

            Assert.NotNull(plate.Filaments);
            foreach (var f in plate.Filaments)
            {
                Assert.True(f.Grams is > 0 and <= MaxGrams, $"{at}: slot {f.SlotId} has {f.Grams} g");
                Assert.True(f.SlotId is >= 1 and <= MaxSlotId, $"{at}: slot id {f.SlotId}");
                Assert.NotNull(f.Type);
                Assert.NotNull(f.ColorHex);
                Assert.NotNull(f.Vendor);
                Assert.NotNull(f.Profile);
            }
            _ = plate.TotalGrams;

            if (plate.Thumbnail is { } png) AssertUsablePng(png, at);
        }
    }

    /// <summary>A thumbnail must at least look like a PNG of a size the invoice code can decode.</summary>
    public static void AssertUsablePng(byte[] png, string label)
    {
        Assert.True(png.Length is >= 33 and <= MaxThumbnailBytes, $"{label}: thumbnail is {png.Length} bytes");
        Assert.True(png.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), $"{label}: thumbnail is not a PNG");
        Assert.True(png.AsSpan(12, 4).SequenceEqual("IHDR"u8), $"{label}: thumbnail has no IHDR");
        int w = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), h = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        Assert.True(w is >= 1 and <= MaxThumbnailSide && h is >= 1 and <= MaxThumbnailSide, $"{label}: thumbnail claims {w} x {h}");
    }
}

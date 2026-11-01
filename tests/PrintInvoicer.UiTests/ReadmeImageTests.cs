using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintInvoicer.App;

namespace PrintInvoicer.UiTests;

/// <summary>
/// Rebuilds the README pictures from the sample shop, offscreen. Every run renders them into the test output's
/// screens\readme folder, so CI proves they still render; they are copied into docs\images only when
/// make-readme-images.ps1 sets PRINTINVOICER_README_IMAGES.
/// </summary>
[Trait("Category", "ReadmeImages")]
public sealed class ReadmeImageTests : IDisposable
{
    // Set only by make-readme-images.ps1. A normal test run and CI never set it, so they never rewrite the committed pictures.
    private const string OutputVariable = "PRINTINVOICER_README_IMAGES";

    // Keeps each committed picture to a few hundred KB.
    private const int MaxBytes = 600_000;

    private readonly SampleShop _shop;

    public ReadmeImageTests()
    {
        _shop = WpfHost.Run(() => new SampleShop());
        WpfHost.TakeBindingErrors();
    }

    public void Dispose() => _shop.Dispose();

    [Fact]
    public void Readme_images_are_rebuilt_from_the_sample_shop() => WpfHost.Run(() =>
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;

        // The Status column and the date pickers follow the culture; pin it so the pictures do not change with the PC's language.
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var jobList = RenderJobList();

            Save("main-jobs.png", jobList);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    });

    /// <summary>
    /// The Jobs tab of the main window at its default 1100 x 680, at 2x. The size is set in code because a window layout
    /// file left by an earlier test run can change the default.
    /// </summary>
    private byte[] RenderJobList()
    {
        var main = WpfHost.Offscreen(new MainWindow());
        main.Width = 1100;
        main.Height = 680;
        WpfHost.Settle(main);

        // The picture must show every state in a fixed order, or it is not worth committing.
        var rows = main.ViewModel.Jobs;
        string[] expectedTitles = ["Regulator clips", "Wire Brush Inserts", "Tube caps", "Dive light mounts"];
        Assert.Equal(expectedTitles, rows.Select(r => r.Title));
        string[] expectedInvoices = ["2026-002", "2026-001", "2026-003", "2026-004"];
        Assert.Equal(expectedInvoices, rows.Select(r => r.Invoice));
        string[] expectedDates = ["2026-10-31", "2026-10-31", "2026-10-30", "2026-10-22"];
        Assert.Equal(expectedDates, rows.Select(r => r.Date));
        string[] expectedStatuses = ["Draft", "Draft", "Invoiced Oct 30", "Paid Oct 27"];
        Assert.Equal(expectedStatuses, rows.Select(r => r.Status));

        byte[] png = WpfHost.RenderPng(main, 2);
        main.Close();
        Assert.Empty(WpfHost.TakeBindingErrors());
        AssertPng(png, 2000, 2200, 1200, 1360);
        AssertFillsFrame(png);
        return png;
    }

    /// <summary>Fails the run, before anything is written, if a picture is not a PNG of the expected size.</summary>
    private static void AssertPng(byte[] png, int minWidth, int maxWidth, int minHeight, int maxHeight)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        Assert.InRange(png.Length, 1, MaxBytes);
        var frame = BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.InRange(frame.PixelWidth, minWidth, maxWidth);
        Assert.InRange(frame.PixelHeight, minHeight, maxHeight);
    }

    /// <summary>
    /// Fails the run, before anything is written, unless the window was drawn out to the right and bottom edges of the
    /// picture. The window background is a solid color, so a full-scale render has opaque pixels along both edges; a window
    /// drawn at 1x in one corner of a 2x canvas leaves them empty, and AssertPng cannot tell, because the pixel size is the
    /// same. It asks for any non-transparent pixel on each edge, not every one, so rounded window corners cannot make it fail.
    /// </summary>
    private static void AssertFillsFrame(byte[] png)
    {
        var frame = BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;

        var column = new byte[height * 4];
        bgra.CopyPixels(new Int32Rect(width - 1, 0, 1, height), column, 4, 0);
        var row = new byte[width * 4];
        bgra.CopyPixels(new Int32Rect(0, height - 1, width, 1), row, width * 4, 0);

        Assert.True(HasOpaquePixel(column), "The picture's right edge is empty, so the window was not drawn at full scale.");
        Assert.True(HasOpaquePixel(row), "The picture's bottom edge is empty, so the window was not drawn at full scale.");
    }

    // True when some pixel in a Bgra32 strip has an alpha byte above zero.
    private static bool HasOpaquePixel(byte[] bgra)
    {
        for (int i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Writes a picture to the test output's screens\readme folder, and also to the folder named by
    /// PRINTINVOICER_README_IMAGES when that is set.
    /// </summary>
    private static void Save(string name, byte[] png)
    {
        // AUDIT: file write. Creates the test output's screens\readme folder if it does not exist.
        string bin = Directory.CreateDirectory(Path.Combine(WpfHost.ScreenshotFolder, "readme")).FullName;
        // AUDIT: file write. The rendered README picture, in the test output's screens\readme folder.
        File.WriteAllBytes(Path.Combine(bin, name), png);

        string? target = Environment.GetEnvironmentVariable(OutputVariable);
        if (string.IsNullOrWhiteSpace(target)) return;

        // AUDIT: file write. Creates the folder make-readme-images.ps1 asked for (docs\images) if it does not exist.
        string folder = Directory.CreateDirectory(target).FullName;
        // AUDIT: file write. The same picture into docs\images, only when make-readme-images.ps1 asks for it.
        File.WriteAllBytes(Path.Combine(folder, name), png);
    }
}

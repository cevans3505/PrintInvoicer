using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrintInvoicer.App.Pdf;

/// <summary>
/// Prints rendered invoice pages through the standard Windows print dialog. Printing from page images works the same
/// whichever PDF reader is installed; the shell "print" verb depends on the reader and often skips the dialog.
/// </summary>
public static class InvoicePrinter
{
    public const int Dpi = 300;

    // US Letter in WPF units (1/96 inch); the rendered pages already include the PDF margins.
    private const double PageWidth = 8.5 * 96;
    private const double PageHeight = 11 * 96;

    /// <summary>Shows one print dialog for all pages. Returns false if the user cancelled.</summary>
    public static bool Print(IReadOnlyList<byte[]> pages, string jobName)
    {
        var dialog = new PrintDialog { UserPageRangeEnabled = true };
        if (dialog.ShowDialog() != true) return false;

        // AUDIT: prints. Sends the pages to the printer the user picked in the Windows print dialog.
        dialog.PrintDocument(BuildDocument(pages).DocumentPaginator, jobName);
        return true;
    }

    /// <summary>One Letter-size page per rendered image.</summary>
    public static FixedDocument BuildDocument(IReadOnlyList<byte[]> pages)
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(PageWidth, PageHeight);
        foreach (var png in pages)
        {
            var image = new Image { Source = Decode(png), Width = PageWidth, Height = PageHeight, Stretch = Stretch.Uniform };
            var page = new FixedPage { Width = PageWidth, Height = PageHeight, Background = Brushes.White };
            page.Children.Add(image);
            var content = new PageContent();
            ((System.Windows.Markup.IAddChild)content).AddChild(page);
            document.Pages.Add(content);
        }
        return document;
    }

    private static BitmapImage Decode(byte[] png)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = new MemoryStream(png);
        img.EndInit();
        img.Freeze();
        return img;
    }
}

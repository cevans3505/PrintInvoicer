using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintInvoicer.Core.Models;
using PrintInvoicer.Core.Slicing;

namespace PrintInvoicer.App.Pdf;

/// <summary>
/// Plate pictures for invoices. Bambu renders only the parts on a transparent background, and resin previews have
/// their background made transparent on import, so nothing but the parts is printed. This picks the sharpest copy
/// available and trims the empty margin so the parts print larger.
/// </summary>
public static class PlateImages
{
    public static byte[]? ForInvoice(JobPlate plate)
    {
        // Jobs imported before 1.6 stored the 128 px preview; prefer the 512 px render while the file is still around.
        var png = plate.Thumbnail;
        bool bambu = !plate.IsManual && plate.Technology == PrintTechnology.Fdm;
        if (bambu && (png is null || PixelWidth(png) < 256))
            png = BambuFileReader.ReadPlateImage(plate.SourceFile, plate.PlateIndex) ?? png;
        return png is null ? null : TrimTransparentMargin(png);
    }

    private static int PixelWidth(byte[] png)
    {
        try { return Decode(png).PixelWidth; }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException) { return 0; }
    }

    private static BitmapSource Decode(byte[] png) =>
        BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];

    /// <summary>Crops to the visible pixels plus a small border. Returns the input unchanged if it cannot be read.</summary>
    public static byte[] TrimTransparentMargin(byte[] png)
    {
        BitmapSource src;
        try { src = new FormatConvertedBitmap(Decode(png), PixelFormats.Bgra32, null, 0); }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException) { return png; }

        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        src.CopyPixels(px, stride, 0);

        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (px[y * stride + x * 4 + 3] < 8) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        if (maxX < 0) return png; // fully transparent

        int pad = Math.Max(4, (maxX - minX) / 25);
        var rect = new Int32Rect(Math.Max(0, minX - pad), Math.Max(0, minY - pad), 0, 0);
        rect.Width = Math.Min(w, maxX + pad + 1) - rect.X;
        rect.Height = Math.Min(h, maxY + pad + 1) - rect.Y;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(new CroppedBitmap(src, rect)));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}

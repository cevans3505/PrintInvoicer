using System.Globalization;

namespace PrintInvoicer.App;

public static class Format
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public static string Money(decimal d) => d.ToString("C2", Us);

    /// <summary>A unit rate: cents as usual, but small rates like $0.002 per gram keep their digits.</summary>
    public static string Rate(decimal d) => Math.Round(d, 2) == d ? Money(d) : "$" + d.ToString("0.00##", CultureInfo.InvariantCulture);

    public static string Ml(decimal ml) => $"{ml:0.##} mL";

    public static string Duration(int seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalHours}:{t.Minutes:00}";
    }

    /// <summary>Accepts "5:56", "5:56:30" or decimal hours like "5.93". Returns seconds, or null if it cannot be read.</summary>
    public static int? ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        var parts = text.Split(':');
        if (parts.Length is 2 or 3 && parts.All(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            int h = int.Parse(parts[0], CultureInfo.InvariantCulture);
            int m = int.Parse(parts[1], CultureInfo.InvariantCulture);
            int s = parts.Length == 3 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : 0;
            return h * 3600 + m * 60 + s;
        }
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) && hours >= 0)
            return (int)Math.Round(hours * 3600);
        return null;
    }
}

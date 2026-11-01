using System.IO;
using System.Text.Json;
using System.Windows;

namespace PrintInvoicer.App;

/// <summary>
/// Remembers each window's size, position and maximized state between runs. Saved per Windows user in
/// %LOCALAPPDATA%\PrintInvoicer\window-layout.json, apart from the job database, since it belongs to this PC's screens.
/// </summary>
public static class WindowLayout
{
    /// <summary>A window's saved position, size and maximized state.</summary>
    public record Placement(double Left, double Top, double Width, double Height, bool Maximized);

    /// <summary>Where layouts are kept; tests point it elsewhere.</summary>
    public static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintInvoicer", "window-layout.json");

    /// <summary>Applies the saved layout for <paramref name="key"/> (call before the window is shown) and saves it again on close.</summary>
    public static void Track(Window window, string key)
    {
        if (Load().TryGetValue(key, out var p) && IsUsable(p))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = p.Left;
            window.Top = p.Top;
            window.Width = Math.Max(p.Width, window.MinWidth);
            window.Height = Math.Max(p.Height, window.MinHeight);
            if (p.Maximized) window.WindowState = WindowState.Maximized;
        }
        window.Closed += (_, _) => Save(key, window);
    }

    // Saves the window's normal size and position (even if it is maximized) when it closes.
    private static void Save(string key, Window window)
    {
        // RestoreBounds is the normal size even while maximized or minimized.
        var r = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, Size(window.Width, window.ActualWidth), Size(window.Height, window.ActualHeight))
            : window.RestoreBounds;
        if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) return;

        var placement = new Placement(r.Left, r.Top, r.Width, r.Height, window.WindowState == WindowState.Maximized);
        if (!IsUsable(placement)) return; // off every screen, so not worth keeping

        var all = Load();
        all[key] = placement;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // AUDIT: file write. %LOCALAPPDATA%\PrintInvoicer\window-layout.json: window positions and sizes only.
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A layout that cannot be saved is not worth interrupting anyone over.
        }
    }

    // The size set on the window, or its actual size if none was set.
    private static double Size(double set, double actual) => double.IsNaN(set) || set <= 0 ? actual : set;

    // Reads all saved layouts; a missing or damaged file means none are saved.
    private static Dictionary<string, Placement> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Dictionary<string, Placement>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new();
    }

    /// <summary>
    /// True when the window would show enough of its title bar on the current screens to be grabbed, so a layout saved
    /// on a monitor that has since been unplugged falls back to the default position instead of opening out of sight.
    /// </summary>
    public static bool IsUsable(Placement p)
    {
        if (p.Width < 200 || p.Height < 150) return false;
        var screens = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var titleBar = new Rect(p.Left, p.Top, p.Width, 40);
        titleBar.Intersect(screens);
        return !titleBar.IsEmpty && titleBar.Width >= 120 && titleBar.Height >= 20;
    }
}

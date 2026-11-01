using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrintInvoicer.App;

// Every test drives the one WPF thread below, so they must not run side by side.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PrintInvoicer.UiTests;

/// <summary>
/// One STA thread running the app's resources (styles, theme, converters) for the whole test run, as App.OnStartup sets
/// them up. Windows are only ever shown far offscreen, without activation or a taskbar button, and nothing that opens a
/// dialog is called, so running the tests never puts anything on the screen or takes focus.
/// </summary>
internal static class WpfHost
{
    private static readonly Lazy<Dispatcher> Ui = new(Start);

    /// <summary>Binding errors seen since the last <see cref="TakeBindingErrors"/>.</summary>
    private static readonly List<string> BindingErrors = new();

    /// <summary>Where screenshots go, for a person to look at; nothing is compared against them.</summary>
    public static string ScreenshotFolder { get; } = Path.Combine(AppContext.BaseDirectory, "screens");

    public static string TempFolder { get; } = Path.Combine(Path.GetTempPath(), "PrintInvoicer UI tests");

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
                QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
                EventManager.RegisterClassHandler(typeof(TextBox), FrameworkElement.LoadedEvent, new RoutedEventHandler(App.App.HideClearButton));

                // Keep the tests away from the network and from this PC's real window and update files.
                Directory.CreateDirectory(TempFolder);
                MainWindow.CheckForUpdatesOnLaunch = false;
                WindowLayout.FilePath = Path.Combine(TempFolder, "window-layout.json");

                // WPF only reports binding errors under a debugger unless tracing is refreshed first.
                PresentationTraceSources.Refresh();
                PresentationTraceSources.DataBindingSource.Listeners.Add(new Collector());
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
                dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            ready.Set();
            if (failure is null) Dispatcher.Run();
        }) { IsBackground = true, Name = "WPF test thread" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher ?? throw new InvalidOperationException("Could not start WPF for the tests.", failure);
    }

    public static void Run(Action action) => Ui.Value.Invoke(action);
    public static T Run<T>(Func<T> func) => Ui.Value.Invoke(func);

    public static List<string> TakeBindingErrors()
    {
        lock (BindingErrors)
        {
            var copy = BindingErrors.Distinct().ToList();
            BindingErrors.Clear();
            return copy;
        }
    }

    /// <summary>Shows a window where no one can see it, and lets layout and bindings finish.</summary>
    public static T Offscreen<T>(T window) where T : Window
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();
        Settle(window);
        return window;
    }

    public static void Settle(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// The window's whole client area (its root visual, so element margins are included) as PNG bytes, at scale x 96 DPI.
    /// No title bar or frame is drawn.
    /// </summary>
    public static byte[] RenderPng(Window window, double scale = 1)
    {
        Settle(window);
        var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        int width = Math.Max(1, (int)(root.ActualWidth * scale));
        int height = Math.Max(1, (int)(root.ActualHeight * scale));
        var rtb = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Saves what the window looks like to the screens folder.</summary>
    public static void Screenshot(Window window, string name)
    {
        // AUDIT: file write. Creates the test output's screens folder if it does not exist.
        Directory.CreateDirectory(ScreenshotFolder);
        // AUDIT: file write. A screenshot for a person to look at, in the test output's screens folder.
        File.WriteAllBytes(Path.Combine(ScreenshotFolder, name + ".png"), RenderPng(window));
    }

    /// <summary>All elements of a type under a root, for checking what is enabled or visible.</summary>
    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    private sealed class Collector : TraceListener
    {
        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (message is null) return;
            lock (BindingErrors) BindingErrors.Add(message);
        }
    }
}

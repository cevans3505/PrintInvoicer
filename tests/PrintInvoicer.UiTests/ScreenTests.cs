using System.IO;
using System.Windows;
using System.Windows.Controls;
using PrintInvoicer.App;
using PrintInvoicer.App.Pdf;
using PrintInvoicer.App.ViewModels;
using PrintInvoicer.Core.Models;
using PrintInvoicer.Core.Pricing;

namespace PrintInvoicer.UiTests;

/// <summary>
/// Opens the real windows on sample data (offscreen) and checks they load, bind, total up and lock as they should.
/// Screenshots of each go to the test output's screens folder for a person to look over.
/// </summary>
public sealed class ScreenTests : IDisposable
{
    private readonly SampleShop _shop;

    public ScreenTests()
    {
        _shop = WpfHost.Run(() => new SampleShop());
        WpfHost.TakeBindingErrors();
    }

    public void Dispose() => _shop.Dispose();

    private static void NoBindingErrors(string where)
    {
        var errors = WpfHost.TakeBindingErrors();
        Assert.True(errors.Count == 0, $"{where} has binding errors:\n" + string.Join("\n", errors.Take(10)));
    }

    /// <summary>Proves the binding-error checks below can fail, so a pass means something.</summary>
    [Fact]
    public void A_broken_binding_is_caught() => WpfHost.Run(() =>
    {
        var text = new TextBlock { DataContext = new { Real = "x" } };
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Missing"));
        var window = WpfHost.Offscreen(new Window { Content = text });
        window.Close();
        Assert.Contains(WpfHost.TakeBindingErrors(), e => e.Contains("Missing"));
    });

    [Fact]
    public void Main_window_tabs_load_without_binding_errors() => WpfHost.Run(() =>
    {
        var main = WpfHost.Offscreen(new MainWindow());
        var tabs = WpfHost.Descendants<TabControl>(main).First();
        string[] names = ["jobs", "materials", "printers", "settings"];
        for (int i = 0; i < names.Length; i++)
        {
            tabs.SelectedIndex = i;
            main.Height = names[i] == "settings" ? 1150 : 680;
            WpfHost.Screenshot(main, "main-" + names[i]);
        }
        Assert.Equal(4, main.ViewModel.Jobs.Count);
        main.Close();
        NoBindingErrors("The main window");
    });

    [Fact]
    public void Job_list_totals_follow_the_filters() => WpfHost.Run(() =>
    {
        var main = WpfHost.Offscreen(new MainWindow());
        var vm = main.ViewModel;
        var book = _shop.Db.LoadPriceBook();
        decimal all = vm.Jobs.Sum(r => r.Total);
        Assert.Equal("Total " + Format.Money(all), vm.VisibleTotal);

        vm.StageFilter = StageFilter.Finalized;
        Assert.Equal("Showing 2 of 4 jobs", vm.VisibleCount);
        decimal sum = 0;
        foreach (var sent in new[] { _shop.Reload(_shop.FinalizedJob), _shop.Reload(_shop.PaidJob) })
            sum += PricingCalculator.Calculate(sent, PriceSnapshot.For(sent, book)).Total;
        Assert.Equal("Total " + Format.Money(sum), vm.VisibleTotal);

        vm.ClearFiltersCommand.Execute(null);
        vm.Keywords = "clips";
        Assert.Equal("Showing 1 of 4 jobs", vm.VisibleCount);
        main.Close();
    });

    private JobEditorWindow OpenEditor(Job job, out JobEditorViewModel vm)
    {
        vm = new JobEditorViewModel(_shop.Db, _shop.Reload(job)) { ShowNotes = _ => { } };
        return WpfHost.Offscreen(new JobEditorWindow(vm));
    }

    /// <summary>Closes a job window without the save prompt, which would be a dialog.</summary>
    private static void CloseQuietly(JobEditorWindow window, JobEditorViewModel vm)
    {
        vm.IsDirty = false;
        window.Close();
    }

    [Fact]
    public void Fdm_job_window_shows_the_same_total_as_the_invoice() => WpfHost.Run(() =>
    {
        var window = OpenEditor(_shop.FdmJob, out var vm);
        WpfHost.Screenshot(window, "editor-fdm");
        var expected = PricingCalculator.Calculate(_shop.Reload(_shop.FdmJob), _shop.Db.LoadPriceBook());
        Assert.Equal(Format.Money(expected.Total), vm.TotalText);
        Assert.True(vm.IsEditable);
        Assert.Equal(5, vm.Parts[0].Plates[0].Slots.Count);

        // Modeling hours keep 2 decimals, as the invoice shows them, so hours x rate multiplies out.
        vm.ModelingHours = 1.333m;
        Assert.Equal(1.33m, vm.ModelingHours);
        Assert.Equal(Format.Money(1.33m * 50m), vm.ModelingText);
        CloseQuietly(window, vm);
        NoBindingErrors("The FDM job window");
    });

    [Fact]
    public void Resin_job_window_asks_for_the_part_count() => WpfHost.Run(() =>
    {
        var window = OpenEditor(_shop.ResinJob, out var vm);
        WpfHost.Screenshot(window, "editor-resin");
        var filePlate = vm.Parts.Single(p => p.Name != "Hose clip").Plates[0];
        var typedPlate = vm.Parts.Single(p => p.Name == "Hose clip").Plates[0];

        Assert.True(filePlate.IsResin);
        Assert.Equal(37.21m, filePlate.Ml);
        Assert.True(filePlate.IsCountUnconfirmed);
        Assert.False(typedPlate.IsCountUnconfirmed);
        Assert.Contains(vm.Warnings, w => w.Contains("how many parts are on the plate"));
        Assert.Contains(vm.ExtraCosts, e => e.Cost.Name == "Wash and cure");

        filePlate.PartsPerPlate = 6;
        Assert.False(filePlate.IsCountUnconfirmed);
        Assert.DoesNotContain(vm.Warnings, w => w.Contains("how many parts are on the plate"));
        CloseQuietly(window, vm);
        NoBindingErrors("The resin job window");
    });

    [Fact]
    public void Finalized_job_window_is_locked_at_its_prices() => WpfHost.Run(() =>
    {
        var sent = _shop.Reload(_shop.FinalizedJob).FinalizedTotal!.Value;
        foreach (var p in _shop.Db.GetPrinters()) { p.MachineRatePerHour = 9m; _shop.Db.SavePrinter(p); }

        var window = OpenEditor(_shop.FinalizedJob, out var vm);
        WpfHost.Screenshot(window, "editor-finalized");
        Assert.False(vm.IsEditable);
        Assert.Equal(Format.Money(sent), vm.TotalText);
        Assert.Empty(vm.Warnings);

        // The parts and labor are disabled; Paid stays usable.
        var parts = WpfHost.Descendants<ItemsControl>(window).First(i => i.ItemsSource == vm.Parts);
        Assert.False(parts.IsEnabled);
        Assert.Contains(WpfHost.Descendants<CheckBox>(window), c => c.Content as string == "Paid" && c.IsEnabled);

        vm.AddFiles([TestFilesPath("Wire_Brush_Insert.gcode.3mf")]);
        Assert.Single(vm.Parts);                 // locked: nothing added
        CloseQuietly(window, vm);
        NoBindingErrors("The finalized job window");
    });

    private static string TestFilesPath(string name) => PrintInvoicer.Tests.TestFiles.Fixture(name);

    [Fact]
    public void Every_invoice_kind_renders_for_every_job() => WpfHost.Run(() =>
    {
        var book = _shop.Db.LoadPriceBook();
        foreach (var job in _shop.Db.GetJobs())
        {
            var jobBook = PriceSnapshot.For(job, book);
            var quote = PricingCalculator.Calculate(job, jobBook);
            foreach (var kind in Enum.GetValues<InvoiceKind>())
            {
                var pages = InvoicePdf.RenderPages(job, quote, jobBook, kind, 60);
                Assert.NotEmpty(pages);
                Assert.All(pages, p => Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, p[..4]));
                File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(WpfHost.ScreenshotFolder).FullName,
                    $"invoice-{job.InvoiceNumber}-{kind}.png".ToLowerInvariant()), pages[0]);
            }

            // Final and internal PDFs go to the invoice folder; previews never do.
            Assert.StartsWith(jobBook.Settings.InvoiceFolder, InvoicePdf.Write(job, quote, jobBook, InvoiceKind.Final));
        }
    });

    [Fact]
    public void Update_bar_shows_only_for_a_newer_release() => WpfHost.Run(() =>
    {
        var main = WpfHost.Offscreen(new MainWindow());
        var bar = WpfHost.Descendants<Border>(main).First(b => b.DataContext is UpdateService);
        Assert.Equal(Visibility.Collapsed, bar.Visibility);

        string state = Path.Combine(_shop.Folder, "update-check.json");
        var updates = new UpdateService(new Version(3, 3, 0), _ => Task.FromResult<ReleaseInfo?>(
            new(new Version(3, 4, 0), $"https://github.com/{UpdateService.Repository}/releases/tag/v3.4.0")), state);
        main.ViewModel.Updates = updates;
        updates.CheckOnStartupAsync(true).GetAwaiter().GetResult();   // the fake answers at once
        WpfHost.Settle(main);
        bar = WpfHost.Descendants<Border>(main).First(b => b.DataContext is UpdateService);
        Assert.Equal(Visibility.Visible, bar.Visibility);
        Assert.Equal("PrintInvoicer 3.4.0 is available. You have 3.3.0.", updates.BarText);
        WpfHost.Screenshot(main, "main-update-bar");

        updates.LaterCommand.Execute(null);
        WpfHost.Settle(main);
        Assert.Equal(Visibility.Collapsed, bar.Visibility);
        main.Close();
    });

    [Fact]
    public void Settings_tab_shows_backups() => WpfHost.Run(() =>
    {
        var settings = new SettingsViewModel(_shop.Db);
        Assert.Equal("No backups yet.", settings.BackupStatus);
        settings.BackUpNowCommand.Execute(null);
        Assert.StartsWith("Saved printinvoicer-", settings.BackupStatus);
        Assert.Single(PrintInvoicer.Core.Data.DatabaseBackup.List(settings.BackupFolder));
        Assert.StartsWith(_shop.Folder, settings.BackupFolder);   // next to the database by default
    });
}

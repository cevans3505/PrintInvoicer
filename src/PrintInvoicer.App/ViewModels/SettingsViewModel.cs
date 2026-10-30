using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintInvoicer.Core.Data;
using PrintInvoicer.Core.Models;

namespace PrintInvoicer.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly Database _db;

    [ObservableProperty] private AppSettings _model;
    [ObservableProperty] private string _savedText = "";

    // The counter as this tab last loaded it. Creating invoices advances the stored counter while this tab is open,
    // so saving writes the counter only when it was edited here; otherwise it would set the sequence back.
    private int _loadedNextInvoice;

    public SettingsViewModel(Database db)
    {
        _db = db;
        _model = db.LoadSettings();
        _loadedNextInvoice = _model.NextInvoiceNumber;
    }

    public string DatabasePath => _db.FilePath;

    public string InvoiceFolder
    {
        get => string.IsNullOrWhiteSpace(Model.InvoiceFolder) ? Pdf.InvoicePdf.DefaultFolder : Model.InvoiceFolder;
        set { Model.InvoiceFolder = value; OnPropertyChanged(); }
    }

    /// <summary>What the next new job will get: past the highest saved number, or this counter if it is higher (once saved).</summary>
    public string NextInvoicePreview => $"Next invoice number: {_db.PeekNextInvoiceNumber(DateTime.Today)}";

    /// <summary>Called when jobs change elsewhere, so the preview stays current.</summary>
    public void RefreshPreview() => OnPropertyChanged(nameof(NextInvoicePreview));

    [RelayCommand]
    private void Save()
    {
        if (Model.NextInvoiceNumber < 1) Model.NextInvoiceNumber = 1;
        if (Model.NextInvoiceNumber == _loadedNextInvoice)
            Model.NextInvoiceNumber = _db.LoadSettings().NextInvoiceNumber;
        _db.SaveSettings(Model);
        _loadedNextInvoice = Model.NextInvoiceNumber;
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(NextInvoicePreview));
        SavedText = $"Saved at {DateTime.Now:h:mm tt}. Open jobs pick up new rates when reopened.";
    }

    [RelayCommand]
    private void Revert()
    {
        Model = _db.LoadSettings();
        _loadedNextInvoice = Model.NextInvoiceNumber;
        OnPropertyChanged(nameof(InvoiceFolder));
        OnPropertyChanged(nameof(NextInvoicePreview));
        SavedText = "";
    }

    [RelayCommand]
    private void OpenDataFolder() => OpenFolder(Path.GetDirectoryName(_db.FilePath)!);

    [RelayCommand]
    private void OpenInvoiceFolder()
    {
        Directory.CreateDirectory(InvoiceFolder);
        OpenFolder(InvoiceFolder);
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Open folder");
        }
    }
}

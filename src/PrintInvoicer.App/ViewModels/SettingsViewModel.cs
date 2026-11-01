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
        RefreshBackupStatus();
    }

    public string DatabasePath => _db.FilePath;

    public string BackupFolder
    {
        get => App.BackupFolder(_db, Model);
        set { Model.BackupFolder = string.Equals(value?.Trim(), DatabaseBackup.DefaultFolder(_db.FilePath), StringComparison.OrdinalIgnoreCase) ? "" : value?.Trim() ?? ""; OnPropertyChanged(); RefreshBackupStatus(); }
    }

    [ObservableProperty] private string _backupStatus = "";

    /// <summary>When the newest backup was made and how many there are, or why the last one failed.</summary>
    public void RefreshBackupStatus()
    {
        try
        {
            var all = DatabaseBackup.List(BackupFolder);
            string latest = all.Count == 0 ? "No backups yet." : $"Newest backup {all[0].LastWriteTime:MMM d, h:mm tt}; {all.Count} kept.";
            BackupStatus = App.BackupProblem is string problem ? $"{problem} {latest}" : latest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BackupStatus = $"Cannot read the backup folder: {ex.Message}";
        }
    }

    [RelayCommand]
    private void BackUpNow()
    {
        try
        {
            var path = DatabaseBackup.Manual(_db.FilePath, BackupFolder, DateTime.Now);
            App.BackupProblem = null;
            RefreshBackupStatus();
            BackupStatus = $"Saved {Path.GetFileName(path)}. " + BackupStatus;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            BackupStatus = $"Backup failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        try { Directory.CreateDirectory(BackupFolder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { BackupStatus = ex.Message; return; }
        OpenFolder(BackupFolder);
    }

    /// <summary>Replaces all data with a backup (saving the current data first), then restarts so every screen reloads.</summary>
    [RelayCommand]
    private void RestoreBackup()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Restore a backup",
            Filter = "PrintInvoicer backups (printinvoicer-*.db)|printinvoicer-*.db|All databases (*.db)|*.db",
            InitialDirectory = Directory.Exists(BackupFolder) ? BackupFolder : Path.GetDirectoryName(_db.FilePath),
        };
        if (dlg.ShowDialog() != true) return;

        var taken = File.GetLastWriteTime(dlg.FileName);
        if (MessageBox.Show($"Replace all jobs, materials, printers and settings with the backup from {taken:MMMM d, yyyy h:mm tt}?\n\n" +
                            "Your current data is backed up first, so this can be undone by restoring that copy. PrintInvoicer restarts afterwards.",
                "Restore a backup", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            DatabaseBackup.Restore(dlg.FileName, _db.FilePath, BackupFolder, DateTime.Now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            MessageBox.Show($"The backup was not restored: {ex.Message}", "Restore a backup", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (Environment.ProcessPath is string exe) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

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
        {
            var stored = _db.LoadSettings();
            Model.NextInvoiceNumber = stored.NextInvoiceNumber;
            Model.NextInvoiceYear = stored.NextInvoiceYear;
        }
        else
        {
            Model.NextInvoiceYear = DateTime.Today.Year;   // a counter typed in now is for this year
        }
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
        OnPropertyChanged(nameof(BackupFolder));
        OnPropertyChanged(nameof(NextInvoicePreview));
        RefreshBackupStatus();
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

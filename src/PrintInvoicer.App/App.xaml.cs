using System.Windows;
using System.Windows.Controls;
using System.IO;
using PrintInvoicer.Core.Data;
using PrintInvoicer.Core.Models;

namespace PrintInvoicer.App;

public partial class App : Application
{
    public static Database Db { get; internal set; } = null!;

    /// <summary>Why the last automatic backup failed, if it did; shown on the Settings tab.</summary>
    public static string? BackupProblem { get; set; }

    /// <summary>The folder backups go to: the one set in Settings, or Backups next to the database.</summary>
    public static string BackupFolder(Database db, AppSettings s) =>
        string.IsNullOrWhiteSpace(s.BackupFolder) ? DatabaseBackup.DefaultFolder(db.FilePath) : s.BackupFolder;

    /// <summary>Today's backup, made in the background so a slow network folder never delays the window.</summary>
    public static Task BackUpTodayAsync(Database db) => Task.Run(() =>
    {
        string folder = BackupFolder(db, db.LoadSettings());
        try
        {
            DatabaseBackup.DailyIfDue(db.FilePath, folder, DateTime.Now);
            BackupProblem = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            BackupProblem = $"The automatic backup to {folder} failed: {ex.Message}";
        }
    });

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        EventManager.RegisterClassHandler(typeof(TextBox), FrameworkElement.LoadedEvent, new RoutedEventHandler(HideClearButton));

        // Anything unexpected is reported plainly, with details saved for the developer, instead of a bare message.
        DispatcherUnhandledException += (_, args) =>
        {
            ErrorReport.Show("PrintInvoicer ran into an unexpected error. Your saved data is not affected; you can keep working.", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) ErrorReport.Show("PrintInvoicer ran into an error it cannot recover from and will close.", ex);
        };

        try
        {
            Db = new Database(Database.DefaultPath);
            var backup = BackUpTodayAsync(Db);
            var main = new MainWindow();
            main.Show();
            backup.ContinueWith(_ => main.Dispatcher.BeginInvoke(() => main.ViewModel.Settings.RefreshBackupStatus()));
        }
        catch (Exception ex)
        {
            ErrorReport.Show($"PrintInvoicer could not start. Its data is in {Database.DefaultPath}; nothing was changed.", ex);
            Shutdown(1);
        }
    }

    /// <summary>
    /// The Fluent TextBox shows a clear-text X while focused, which covers the value in narrow number fields.
    /// A local value beats the template's triggers, so collapsing the button here keeps it hidden.
    /// </summary>
    internal static void HideClearButton(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;
        box.ApplyTemplate();
        if (box.Template?.FindName("DeleteButton", box) is UIElement button)
            button.Visibility = Visibility.Collapsed;
    }
}

using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

namespace PrintInvoicer.App;

/// <summary>
/// Unexpected errors: tells the user plainly that it is a problem in PrintInvoicer, saves the full details to a file,
/// and offers to open the page for reporting it, so they report it instead of assuming they did something wrong.
/// </summary>
public static class ErrorReport
{
    /// <summary>Where error details are saved: %LOCALAPPDATA%\PrintInvoicer\Errors.</summary>
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintInvoicer", "Errors");

    /// <summary>The project's "new issue" page on GitHub, offered in the error message.</summary>
    public static string IssuesPage => $"https://github.com/{UpdateService.Repository}/issues/new";

    /// <summary>Writes the details file and returns its path, or null if it could not be written.</summary>
    public static string? Save(string what, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, $"error-{DateTime.Now:yyyy-MM-dd-HHmmss}.txt");
            string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
            // AUDIT: file write. The error details: app version, Windows version, time and the error itself. Saved only on
            // this PC; nothing is sent anywhere.
            File.WriteAllText(path,
                $"PrintInvoicer {version}\n{RuntimeInformation.OSDescription}\n{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n{what}\n\n{ex}\n");
            return path;
        }
        catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Shows the error. Safe to call from any thread; never throws.</summary>
    public static void Show(string what, Exception ex)
    {
        string? path = Save(what, ex);
        string details = path is null ? "" :
            $"\n\nThe technical details were saved to:\n{path}\nCheck the file for anything private (it lists folder names) before sharing it.";
        string text = $"{what}\n\n{ex.Message}\n\nThis is a problem in PrintInvoicer, not something you did wrong." +
                      details + "\n\nOpen the page for reporting it to the developer?";

        void Ask()
        {
            if (MessageBox.Show(text, "PrintInvoicer: something went wrong", MessageBoxButton.YesNo, MessageBoxImage.Error) != MessageBoxResult.Yes)
                return;
            try
            {
                // AUDIT: opens a program. Only if the user clicks Yes: the GitHub issue page in their browser, and Explorer with
                // the details file selected, so they can attach it themselves.
                Process.Start(new ProcessStartInfo(IssuesPage) { UseShellExecute = true });
                if (path is not null) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            catch (Exception openError) when (openError is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }
        }

        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) Ask();
            else dispatcher.Invoke(Ask);
        }
        catch (Exception)
        {
            // Reporting must never be the thing that crashes; the details file is already written.
        }
    }
}

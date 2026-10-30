using System.Windows;
using System.Windows.Controls;
using PrintInvoicer.Core.Data;

namespace PrintInvoicer.App;

public partial class App : Application
{
    public static Database Db { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        EventManager.RegisterClassHandler(typeof(TextBox), FrameworkElement.LoadedEvent, new RoutedEventHandler(HideClearButton));

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "PrintInvoicer", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            Db = new Database(Database.DefaultPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open the database at {Database.DefaultPath}.\n\n{ex.Message}", "PrintInvoicer",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        new MainWindow().Show();
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

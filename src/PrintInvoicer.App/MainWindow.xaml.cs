using System.Windows;
using System.Windows.Input;
using PrintInvoicer.App.ViewModels;

namespace PrintInvoicer.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        WindowLayout.Track(this, "Main");
        _vm = new MainViewModel(App.Db)
        {
            OpenEditor = job =>
            {
                var editor = new JobEditorWindow(new JobEditorViewModel(App.Db, job)) { Owner = this };
                editor.ShowDialog();
                return job.Id != 0;
            },
        };
        DataContext = _vm;
        Loaded += async (_, _) =>
        {
            if (CheckForUpdatesOnLaunch) await _vm.Updates.CheckOnStartupAsync(App.Db.LoadSettings().CheckForUpdates);
        };
    }

    public MainViewModel ViewModel => _vm;

    /// <summary>Off in test harnesses, which must not reach the network or touch this PC's update state.</summary>
    public static bool CheckForUpdatesOnLaunch { get; set; } = true;

    private void Jobs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        _vm.SetSelection(JobsGrid.SelectedItems.OfType<JobRowViewModel>());

    private void Jobs_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Ignore double clicks on the Paid checkbox or the header.
        if (e.OriginalSource is FrameworkElement { DataContext: JobRowViewModel } && _vm.OpenJobCommand.CanExecute(null))
            _vm.OpenJobCommand.Execute(null);
    }
}

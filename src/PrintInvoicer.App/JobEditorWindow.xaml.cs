using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PrintInvoicer.App.ViewModels;

namespace PrintInvoicer.App;

public partial class JobEditorWindow : Window
{
    private readonly JobEditorViewModel _vm;

    public JobEditorWindow(JobEditorViewModel vm)
    {
        InitializeComponent();
        WindowLayout.Track(this, "JobEditor");
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(JobEditorViewModel.HasParts)) UpdateHint();
        };
        UpdateHint();
    }

    private void UpdateHint() => EmptyHint.Visibility = _vm.HasParts ? Visibility.Collapsed : Visibility.Visible;

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            _vm.AddFiles(files);
    }

    // Enter commits the field being edited, so totals update without tabbing away.
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.OriginalSource is TextBox { AcceptsReturn: false } box)
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // The printer menu opens on a left click too.
    private void ManualPlate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: ContextMenu menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            menu.DataContext = button.DataContext;
            menu.IsOpen = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // Push the focused field into the view model before checking for changes.
        if (Keyboard.FocusedElement is TextBox box)
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (!_vm.IsDirty) return;

        var answer = MessageBox.Show("Save changes to this job?", "PrintInvoicer", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) e.Cancel = true;
        else if (answer == MessageBoxResult.Yes && !_vm.TrySave()) e.Cancel = true;
    }
}

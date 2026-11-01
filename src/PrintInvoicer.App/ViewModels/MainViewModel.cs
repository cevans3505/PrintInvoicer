using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintInvoicer.App.Pdf;
using PrintInvoicer.Core.Data;
using PrintInvoicer.Core.Models;
using PrintInvoicer.Core.Pricing;

namespace PrintInvoicer.App.ViewModels;

/// <summary>
/// The main window: the job list with its filters and totals, the actions on selected jobs, and the Materials,
/// Printers and Settings tabs (each with its own view model).
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly Database _db;

    /// <summary>Every job, and the filtered, sorted view of them that the grid shows.</summary>
    public ObservableCollection<JobRowViewModel> Jobs { get; } = new();
    public ICollectionView JobsView { get; }
    public FilamentsViewModel Filaments { get; }
    public PrintersViewModel Printers { get; }
    public SettingsViewModel Settings { get; }

    /// <summary>New-release notice; replaceable so tests can check without the network.</summary>
    [ObservableProperty] private UpdateService _updates = new();

    [ObservableProperty] private JobRowViewModel? _selectedJob;
    [ObservableProperty] private string _unpaidSummary = "";
    [ObservableProperty] private string _printSummary = "";

    // ---------------- Filters ----------------

    /// <summary>A choice in a filter dropdown: the value, and its wording.</summary>
    public record Option<T>(T Value, string Name);

    public static Option<PaidFilter>[] PaidOptions { get; } =
        { new(PaidFilter.Any, "Paid or unpaid"), new(PaidFilter.Unpaid, "Unpaid"), new(PaidFilter.Paid, "Paid") };
    public static Option<StageFilter>[] StageOptions { get; } =
        { new(StageFilter.Any, "Drafts and finalized"), new(StageFilter.Draft, "Drafts"), new(StageFilter.Finalized, "Finalized") };
    public static Option<PrintedFilter>[] PrintedOptions { get; } =
        { new(PrintedFilter.Any, "Printed or not"), new(PrintedFilter.NotPrinted, "Not printed"), new(PrintedFilter.Printed, "Printed") };
    public static Option<SearchColumn>[] ColumnOptions { get; } =
    {
        new(SearchColumn.Any, "Any column"), new(SearchColumn.Invoice, "Invoice"), new(SearchColumn.Job, "Job"),
        new(SearchColumn.Pieces, "Pieces"), new(SearchColumn.Customer, "Customer"), new(SearchColumn.Notes, "Notes"),
        new(SearchColumn.Status, "Status"),
    };
    public static string[] DateRanges { get; } =
        { "All dates", "This month", "Last month", "Last 30 days", "This year", "Last year", "Custom" };

    [ObservableProperty] private PaidFilter _paidFilter;
    [ObservableProperty] private StageFilter _stageFilter;
    [ObservableProperty] private PrintedFilter _printedFilter;
    [ObservableProperty] private string _keywords = "";
    [ObservableProperty] private SearchColumn _searchColumn;
    [ObservableProperty] private string _dateRange = "All dates";
    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;
    [ObservableProperty] private bool _isFiltered;
    [ObservableProperty] private string _visibleCount = "";
    [ObservableProperty] private string _visibleTotal = "";
    [ObservableProperty] private string _visibleBreakdown = "";

    private readonly JobFilter _filter = new();
    private bool _settingDates;

    /// <summary>Set by the window so the view model can open the editor without knowing about WPF windows.</summary>
    public Func<Job, bool>? OpenEditor { get; set; }

    public MainViewModel(Database db)
    {
        _db = db;
        Filaments = new FilamentsViewModel(db);
        Printers = new PrintersViewModel(db);
        Settings = new SettingsViewModel(db);
        // Printers decide which material lists are shown; rates and costs change every job's total.
        Printers.Changed += () =>
        {
            Filaments.RefreshVisibility();
            Refresh();
        };
        JobsView = CollectionViewSource.GetDefaultView(Jobs);
        JobsView.Filter = o => o is JobRowViewModel row && _filter.Matches(row.Job, row.SearchText);
        Refresh();
    }

    partial void OnPaidFilterChanged(PaidFilter value) => ApplyFilter();
    partial void OnStageFilterChanged(StageFilter value) => ApplyFilter();
    partial void OnPrintedFilterChanged(PrintedFilter value) => ApplyFilter();
    partial void OnKeywordsChanged(string value) => ApplyFilter();
    partial void OnSearchColumnChanged(SearchColumn value) => ApplyFilter();

    // Picking a date by hand switches the range list to Custom.
    partial void OnFromDateChanged(DateTime? value) { if (!_settingDates) DateRange = "Custom"; ApplyFilter(); }
    partial void OnToDateChanged(DateTime? value) { if (!_settingDates) DateRange = "Custom"; ApplyFilter(); }

    partial void OnDateRangeChanged(string value)
    {
        if (value == "Custom") return;
        var (from, to) = JobFilter.RangeFor(value, DateTime.Today);
        _settingDates = true;
        try { FromDate = from; ToDate = to; }
        finally { _settingDates = false; }
        ApplyFilter();
    }

    [RelayCommand]
    // The Clear filters button.
    private void ClearFilters()
    {
        PaidFilter = PaidFilter.Any;
        StageFilter = StageFilter.Any;
        PrintedFilter = PrintedFilter.Any;
        Keywords = "";
        SearchColumn = SearchColumn.Any;
        DateRange = "All dates";
    }

    // Copies the dropdowns and boxes into the filter, re-filters the grid and updates the totals under it.
    private void ApplyFilter()
    {
        _filter.Paid = PaidFilter;
        _filter.Stage = StageFilter;
        _filter.Printed = PrintedFilter;
        _filter.Keywords = Keywords;
        _filter.Column = SearchColumn;
        _filter.From = FromDate;
        _filter.To = ToDate;
        IsFiltered = !_filter.IsEmpty;
        JobsView.Refresh();
        UpdateVisibleTotals();
    }

    /// <summary>Totals for the rows that pass the filters, shown under the list.</summary>
    private void UpdateVisibleTotals()
    {
        var shown = JobsView.Cast<JobRowViewModel>().ToList();
        VisibleCount = IsFiltered ? $"Showing {shown.Count} of {Jobs.Count} jobs" : $"{Jobs.Count} jobs";
        VisibleTotal = "Total " + Format.Money(shown.Sum(r => r.Total));
        decimal paid = shown.Where(r => r.Paid).Sum(r => r.Total);
        decimal owed = shown.Where(r => !r.Paid && r.Job.Finalized).Sum(r => r.Total);
        decimal drafts = shown.Where(r => !r.Paid && !r.Job.Finalized).Sum(r => r.Total);
        VisibleBreakdown = $"Paid {Format.Money(paid)}     Invoiced, unpaid {Format.Money(owed)}     Drafts {Format.Money(drafts)}";
    }

    /// <summary>Reloads every job from the database and prices it (finalized ones at their frozen prices).</summary>
    public void Refresh()
    {
        long? selectedId = SelectedJob?.Job.Id;
        var book = _db.LoadPriceBook();

        Jobs.Clear();
        foreach (var job in _db.GetJobs())
            Jobs.Add(new JobRowViewModel(job, PricingCalculator.Calculate(job, PriceSnapshot.For(job, book)), this));

        SelectedJob = Jobs.FirstOrDefault(j => j.Job.Id == selectedId);
        UpdateSummary();
        Settings.RefreshPreview();
    }

    // The unpaid and to-print summaries at the top right, and the totals under the list.
    internal void UpdateSummary()
    {
        var unpaid = Jobs.Where(j => !j.Paid && j.Job.Finalized).ToList();
        UnpaidSummary = unpaid.Count == 0
            ? "All invoices paid"
            : $"{unpaid.Count} unpaid invoice(s): {Format.Money(unpaid.Sum(j => j.Total))}";
        int toPrint = Jobs.Count(j => !j.Printed);
        PrintSummary = toPrint == 0 ? "All jobs printed" : $"{toPrint} job(s) to print";
        // Ticking Paid or Printed can move a row in or out of the filtered list.
        if (IsFiltered) JobsView.Refresh();
        UpdateVisibleTotals();
    }

    // Saves a Paid or Printed checkbox clicked in the grid straight to the database.
    internal void SetPaid(JobRowViewModel row, bool paid)
    {
        row.Job.Paid = paid;
        row.Job.PaidDate = paid ? DateTime.Today : null;
        _db.SetPaid(row.Job.Id, paid, row.Job.PaidDate);
        UpdateSummary();
    }

    internal void SetPrinted(JobRowViewModel row, bool printed)
    {
        row.Job.Printed = printed;
        row.Job.PrintedDate = printed ? DateTime.Today : null;
        _db.SetPrinted(row.Job.Id, printed, row.Job.PrintedDate);
        UpdateSummary();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MarkPrinted() => MarkSelectedPrinted(true);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MarkNotPrinted() => MarkSelectedPrinted(false);

    // Mark printed / Mark not printed for every selected job.
    private void MarkSelectedPrinted(bool printed)
    {
        // Copy first: refreshing the filter can change the selection while we loop.
        foreach (var row in SelectedJobs.ToList()) row.Printed = printed;
    }

    [RelayCommand]
    // The New job button: starts a job with the default customer, discount and next invoice number.
    private void NewJob()
    {
        var s = _db.LoadSettings();
        var job = new Job
        {
            Customer = s.DefaultCustomer,
            CreatedDate = DateTime.Today,
            // The date is set when the invoice is finalized. The number is a preview, taken when the job is saved.
            InvoiceNumber = _db.PeekNextInvoiceNumber(DateTime.Today),
            DiscountPercent = s.DefaultDiscountPercent,
        };
        bool saved = OpenEditor?.Invoke(job) == true;
        AfterEditor();
        if (saved) Refresh();
        SelectedJob = Jobs.FirstOrDefault(j => j.Job.Id == job.Id) ?? SelectedJob;
    }

    [RelayCommand]
    // Opens the selected job in its own window, on a fresh copy so Cancel leaves the list untouched.
    private void OpenJob()
    {
        if (SelectedJob is null) return;
        // Edit a fresh copy so cancelling leaves the list untouched.
        var job = _db.GetJobs().FirstOrDefault(j => j.Id == SelectedJob.Job.Id);
        if (job is null) return;
        OpenEditor?.Invoke(job);
        AfterEditor();
        Refresh();
    }

    /// <summary>Adding a file can set up a printer, which the Printers and Materials tabs should show.</summary>
    private void AfterEditor()
    {
        Printers.Reload();
        Filaments.RefreshVisibility();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    // Deletes the selected jobs after asking.
    private void DeleteJob()
    {
        var rows = SelectedJobs;
        string label = rows.Count == 1 ? Label(rows[0].Job) : $"these {rows.Count} jobs";
        if (MessageBox.Show($"Delete {label}? This cannot be undone.", "Delete job", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        foreach (var row in rows) _db.DeleteJob(row.Job.Id);
        Refresh();
    }

    // ---------------- Selected-job invoice actions ----------------

    /// <summary>All rows selected in the grid (Ctrl or Shift click); set by the window.</summary>
    public IReadOnlyList<JobRowViewModel> SelectedJobs { get; private set; } = Array.Empty<JobRowViewModel>();

    [ObservableProperty] private string _selectionText = "";

    // The action buttons are enabled only while at least one job is selected.
    public bool HasSelection => SelectedJobs.Count > 0;

    /// <summary>Records which rows are selected and updates the buttons that act on them.</summary>
    public void SetSelection(IEnumerable<JobRowViewModel> rows)
    {
        SelectedJobs = rows.ToList();
        SelectionText = SelectedJobs.Count > 1 ? $"     {SelectedJobs.Count} selected: {Format.Money(SelectedJobs.Sum(r => r.Total))}" : "";
        foreach (var cmd in new IRelayCommand[] { DeleteJobCommand, PreviewInvoicesCommand, FinalizeInvoicesCommand,
                     OpenInternalInvoicesCommand, PrintCustomerInvoicesCommand, PrintInternalInvoicesCommand,
                     RevertToDraftCommand, MarkPrintedCommand, MarkNotPrintedCommand })
            cmd.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    // The Preview, Finalize, Open internal and Print buttons for the selected jobs.
    private void PreviewInvoices() => GenerateInvoices(LoadSelected(), _ => InvoiceKind.Preview, print: false);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void FinalizeInvoices()
    {
        var jobs = LoadSelected();
        if (!FinalizeAll(jobs)) return;
        GenerateInvoices(jobs, _ => InvoiceKind.Final, print: false);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenInternalInvoices() => GenerateInvoices(LoadSelected(), _ => InvoiceKind.Internal, print: false);

    /// <summary>Prints final invoices. Drafts in the selection can be finalized first, or printed as previews.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void PrintCustomerInvoices()
    {
        var jobs = LoadSelected();
        var drafts = jobs.Where(j => !j.Finalized).ToList();
        if (drafts.Count > 0)
        {
            string which = drafts.Count == 1 ? $"{Label(drafts[0])} is a draft." : $"{drafts.Count} of the selected jobs are drafts.";
            var answer = MessageBox.Show(
                $"{which}\n\nFinalize before printing? That dates them today, ready to send.\n\n" +
                "Yes: finalize, then print.\nNo: print drafts as previews.",
                "Print invoices", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;
            if (answer == MessageBoxResult.Yes && !FinalizeAll(drafts)) return;
        }
        GenerateInvoices(jobs, j => j.Finalized ? InvoiceKind.Final : InvoiceKind.Preview, print: true);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void PrintInternalInvoices() => GenerateInvoices(LoadSelected(), _ => InvoiceKind.Internal, print: true);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    // Makes the selected finalized invoices drafts again after asking.
    private void RevertToDraft()
    {
        var jobs = LoadSelected().Where(j => j.Finalized).ToList();
        if (jobs.Count == 0) return;
        string which = jobs.Count == 1 ? Label(jobs[0]) : $"these {jobs.Count} invoices";
        if (MessageBox.Show($"Make {which} a draft again? {InvoiceActions.RevertNote}", "Revert to draft", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        foreach (var job in jobs)
        {
            job.RevertToDraft();
            _db.SaveJob(job);
        }
        Refresh();
    }

    /// <summary>Fresh copies of the selected jobs from the database, in invoice-number order.</summary>
    private List<Job> LoadSelected()
    {
        var saved = _db.GetJobs().ToDictionary(j => j.Id);
        return SelectedJobs
            .Select(r => saved.GetValueOrDefault(r.Job.Id))
            .OfType<Job>()
            .OrderBy(j => j.InvoiceNumber ?? "\uffff", StringComparer.OrdinalIgnoreCase)
            .ThenBy(j => j.CreatedDate)
            .ToList();
    }

    /// <summary>Finalizes and saves each job, asking once about re-dating any finalized on an earlier day.</summary>
    private bool FinalizeAll(IReadOnlyList<Job> jobs)
    {
        if (!InvoiceActions.ConfirmBusinessDetails(_db.LoadSettings())) return false;

        // The same check the job window makes before finalizing.
        var book = _db.LoadPriceBook();
        var warned = jobs.Where(j => !j.Finalized)
            .Select(j => (Job: j, Warnings: PricingCalculator.Calculate(j, book).Warnings))
            .Where(x => x.Warnings.Count > 0)
            .ToList();
        if (warned.Count > 0)
        {
            string list = string.Join("\n\n", warned.Select(x => $"{Label(x.Job)}:\n  " + string.Join("\n  ", x.Warnings)));
            if (MessageBox.Show($"These jobs have warnings:\n\n{list}\n\nFinalize anyway?", "Finalize invoices",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return false;
        }

        var today = DateTime.Today;
        if (InvoiceActions.AskRedate(jobs, today) is not bool redate) return false;
        foreach (var job in jobs)
        {
            InvoiceActions.Finalize(_db, job, today, redate);
            _db.SaveJob(job);
        }
        return true;
    }

    /// <summary>
    /// Writes the PDF for each job from its current data and rates (replacing older copies of that kind), then opens
    /// each one, or prints them all through one Windows print dialog.
    /// </summary>
    private void GenerateInvoices(IReadOnlyList<Job> jobs, Func<Job, InvoiceKind> kindFor, bool print)
    {
        var book = _db.LoadPriceBook();

        var written = new List<(Job Job, JobQuote Quote, PriceBook Book, InvoiceKind Kind, string Path)>();
        var problems = new List<string>();
        var withWarnings = new List<string>();

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            foreach (var job in jobs)
            {
                var jobBook = PriceSnapshot.For(job, book);
                var quote = PricingCalculator.Calculate(job, jobBook);
                var kind = kindFor(job);
                if (quote.Warnings.Count > 0) withWarnings.Add(Label(job));
                try
                {
                    written.Add((job, quote, jobBook, kind, InvoicePdf.Write(job, quote, jobBook, kind)));
                }
                catch (IOException)
                {
                    problems.Add($"{Label(job)}: its PDF is open in another program, so it could not be replaced. Close it and try again.");
                }
            }

            if (print && written.Count > 0)
            {
                var pages = written.SelectMany(w => InvoicePdf.RenderPages(w.Job, w.Quote, w.Book, w.Kind, InvoicePrinter.Dpi)).ToList();
                Mouse.OverrideCursor = null;
                string name = written.Count == 1 ? Path.GetFileNameWithoutExtension(written[0].Path) : $"PrintInvoicer, {written.Count} invoices";
                InvoicePrinter.Print(pages, name);
            }
            else
            {
                foreach (var w in written)
                    // AUDIT: opens a program. Each PDF just created, in the user's default PDF viewer.
                    Process.Start(new ProcessStartInfo(w.Path) { UseShellExecute = true });
            }
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        var notes = new List<string>(problems);
        if (withWarnings.Count > 0)
            notes.Add("These jobs have warnings, usually a slot with no filament picked. Open the job to check:\n  " + string.Join("\n  ", withWarnings));
        if (notes.Count > 0)
            MessageBox.Show(string.Join("\n\n", notes), "Invoices", MessageBoxButton.OK,
                problems.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    // A job's name for messages: its title and invoice number.
    private static string Label(Job job)
    {
        string title = string.IsNullOrWhiteSpace(job.Title) ? "(untitled)" : job.Title;
        return job.InvoiceNumber is null ? title : $"{title} (invoice {job.InvoiceNumber})";
    }
}

/// <summary>One row of the job list: the job, its total, and the text shown in each column.</summary>
public class JobRowViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    public Job Job { get; }
    public decimal Total { get; }

    public JobRowViewModel(Job job, JobQuote quote, MainViewModel owner)
    {
        Job = job;
        Total = quote.Total;
        _owner = owner;
        Pieces = string.Join(", ", quote.Parts.Select(p => $"{p.Quantity} {p.Part.Name.Replace('_', ' ')}"));
        int failed = job.Parts.SelectMany(p => p.Plates).Where(p => p.HasFailures).Sum(p => p.FailedCount);
        if (failed > 0) Pieces += $" ({failed} failed)";
    }

    /// <summary>What the keyword search looks in. Built on each call so Status follows the Paid checkbox.</summary>
    public IReadOnlyDictionary<SearchColumn, string> SearchText => new Dictionary<SearchColumn, string>
    {
        [SearchColumn.Invoice] = Invoice,
        [SearchColumn.Job] = Title,
        [SearchColumn.Pieces] = Pieces,
        [SearchColumn.Customer] = Job.Customer,
        [SearchColumn.Notes] = Job.Notes,
        [SearchColumn.Status] = Status,
    };

    // Column text.
    public string Title => string.IsNullOrWhiteSpace(Job.Title) ? "(untitled)" : Job.Title;
    public string Invoice => Job.InvoiceNumber ?? "";
    public string Date => JobFilter.ListDate(Job).ToString("yyyy-MM-dd");
    public string Pieces { get; }
    public string TotalText => Format.Money(Total);
    public string Status => !Job.Finalized ? "Draft" : Job.Paid ? $"Paid {Job.PaidDate:MMM d}" : $"Invoiced {Job.InvoiceDate:MMM d}";

    public bool Paid
    {
        get => Job.Paid;
        set
        {
            if (value == Job.Paid) return;
            _owner.SetPaid(this, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Status));
        }
    }

    public bool Printed
    {
        get => Job.Printed;
        set
        {
            if (value == Job.Printed) return;
            _owner.SetPrinted(this, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(PrintedTip));
        }
    }

    public string PrintedTip => Job.Printed ? $"Printed {Job.PrintedDate:MMM d, yyyy}" : "Tick when every plate for this job is printed";
}

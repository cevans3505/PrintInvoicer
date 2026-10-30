using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PrintInvoicer.App.Pdf;
using PrintInvoicer.Core.Data;
using PrintInvoicer.Core.Models;
using PrintInvoicer.Core.Pricing;
using PrintInvoicer.Core.Slicing;

namespace PrintInvoicer.App.ViewModels;

public partial class JobEditorViewModel : ObservableObject
{
    private readonly Database _db;
    private readonly Dictionary<string, long> _mappings;
    private PriceBook _book;
    private bool _loading;

    public Job Job { get; }
    public AppSettings Settings => Book.Settings;

    /// <summary>The prices this job is worked out from: frozen ones once finalized, otherwise today's.</summary>
    public PriceBook Book => PriceSnapshot.For(Job, _book);
    public ObservableCollection<PartViewModel> Parts { get; } = new();
    public ObservableCollection<string> Warnings { get; } = new();
    public ObservableCollection<ExtraCostToggleViewModel> ExtraCosts { get; } = new();
    public ObservableCollection<ManualPlateChoice> ManualPlateChoices { get; } = new();

    /// <summary>Choices for slot dropdowns: active filaments plus any inactive one this job already uses.</summary>
    public List<Filament> FilamentOptions { get; private set; } = new();

    /// <summary>Choices for resin dropdowns: active resins plus any inactive one this job already uses.</summary>
    public List<Resin> ResinOptions { get; private set; } = new();

    [ObservableProperty] private JobQuote _quote = null!;
    [ObservableProperty] private bool _isDirty;

    /// <summary>Replaces the notes box shown after adding files; lets tests run without a dialog.</summary>
    public Action<string>? ShowNotes { get; set; }

    public JobEditorViewModel(Database db, Job job)
    {
        _db = db;
        Job = job;
        _book = db.LoadPriceBook();
        _mappings = db.GetMappings();
        LoadOptions();

        RebuildParts();
        Recalculate();
        IsDirty = job.Id == 0;
    }

    private void LoadOptions()
    {
        var usedFilaments = Job.AllPlates.SelectMany(p => p.Filaments).Select(f => f.FilamentId).ToHashSet();
        FilamentOptions = _book.Filaments.Values.Where(f => f.Active || usedFilaments.Contains(f.Id))
            .OrderBy(f => f.Vendor).ThenBy(f => f.Material).ThenBy(f => f.ColorName).ToList();
        var usedResins = Job.AllPlates.Select(p => p.Resin?.ResinId).ToHashSet();
        ResinOptions = _book.Resins.Values.Where(r => r.Active || usedResins.Contains(r.Id))
            .OrderBy(r => r.Vendor).ThenBy(r => r.Name).ThenBy(r => r.ColorName).ToList();

        ManualPlateChoices.Clear();
        var printers = _book.Printers.Values.Where(p => p.Active).OrderBy(p => p.Technology).ThenBy(p => p.Name).ToList();
        foreach (var p in printers) ManualPlateChoices.Add(new ManualPlateChoice($"{p.Name} ({p.TechnologyName})", p, p.Technology));
        foreach (var t in new[] { PrintTechnology.Fdm, PrintTechnology.Resin }.Where(t => printers.All(p => p.Technology != t)))
            ManualPlateChoices.Add(new ManualPlateChoice($"New {Printer.TechnologyLabel(t)} printer", null, t));
    }

    /// <summary>Printers a plate of this kind can be moved to: active ones, plus the plate's current one.</summary>
    internal List<Printer> PrintersFor(PrintTechnology t, long? current) =>
        _book.Printers.Values.Where(p => p.Technology == t && (p.Active || p.Id == current)).OrderBy(p => p.Name).ToList();

    public string WindowTitle => string.IsNullOrWhiteSpace(Job.Title) ? "New job" : Job.Title;

    public string Title
    {
        get => Job.Title;
        set { if (SetProperty(Job.Title, value, Job, (j, v) => j.Title = v)) { OnPropertyChanged(nameof(WindowTitle)); Changed(); } }
    }

    public string Customer
    {
        get => Job.Customer;
        set { if (SetProperty(Job.Customer, value, Job, (j, v) => j.Customer = v)) Changed(); }
    }

    public string Notes
    {
        get => Job.Notes;
        set { if (SetProperty(Job.Notes, value, Job, (j, v) => j.Notes = v)) Changed(); }
    }

    public decimal ModelingHours
    {
        get => Job.ModelingHours;
        set { if (SetProperty(Job.ModelingHours, Math.Max(0, value), Job, (j, v) => j.ModelingHours = v)) Changed(); }
    }

    public bool PostProcessing
    {
        get => Job.PostProcessing;
        set { if (SetProperty(Job.PostProcessing, value, Job, (j, v) => j.PostProcessing = v)) Changed(); }
    }

    public bool Printed
    {
        get => Job.Printed;
        set
        {
            if (SetProperty(Job.Printed, value, Job, (j, v) => j.Printed = v))
            {
                Job.PrintedDate = value ? DateTime.Today : null;
                Changed();
            }
        }
    }

    public bool Paid
    {
        get => Job.Paid;
        set
        {
            if (SetProperty(Job.Paid, value, Job, (j, v) => j.Paid = v))
            {
                Job.PaidDate = value ? DateTime.Today : null;
                Changed();
            }
        }
    }

    /// <summary>Reserved when the job is created; typing a number sets it by hand. A blank number is assigned on finalize.</summary>
    public string InvoiceNumber
    {
        get => Job.InvoiceNumber ?? "";
        set
        {
            string? v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (v == Job.InvoiceNumber) return;
            Job.InvoiceNumber = v;
            OnPropertyChanged();
            Changed();
        }
    }

    public bool IsFinalized => Job.Finalized;

    /// <summary>A finalized invoice is locked so it cannot drift from what was sent; revert to draft to change it.</summary>
    public bool IsEditable => !Job.Finalized;

    public string StatusText => Job.Finalized
        ? $"Finalized {Job.InvoiceDate:MMM d, yyyy}"
        : "Draft, not finalized";

    public DateTime? InvoiceDate
    {
        get => Job.InvoiceDate;
        set
        {
            if (value == Job.InvoiceDate) return;
            Job.InvoiceDate = value?.Date;
            OnPropertyChanged();
            Changed();
        }
    }

    public string ModelingRateText => $"x {Format.Money(Settings.ModelingRatePerHour)}/hr";
    public string PostProcessingText => $"Post-processing ({Format.Money(Settings.PostProcessingFee)} flat)";
    public bool HasParts => Parts.Count > 0;
    public bool HasExtraCosts => ExtraCosts.Count > 0;

    // Summary lines
    public string MaterialText => Format.Money(Quote.Parts.Sum(p => p.Material));
    public string MachineText => Format.Money(Quote.Parts.Sum(p => p.Machine));
    public string ElectricityText => Format.Money(Quote.Parts.Sum(p => p.Electricity));
    public string PlateExtrasText => Format.Money(Quote.Parts.Sum(p => p.Extras));
    public bool HasPlateExtras => Quote.Parts.Any(p => p.Extras > 0);
    public string PartsText => Format.Money(Quote.PartsAmount);
    public string SlicingLabel => $"Slicing ({Quote.SlicingCount} x {Format.Money(Settings.SlicingFeePerPart)})";
    public string SlicingText => Format.Money(Quote.SlicingAmount);
    public string ModelingText => Format.Money(Quote.ModelingAmount);
    public string PostText => Format.Money(Quote.PostProcessingAmount);
    public string JobExtrasText => Format.Money(Quote.JobExtrasAmount);
    public bool HasJobExtras => Quote.JobExtras.Count > 0;
    public string TotalText => Format.Money(Quote.Total);

    internal Filament? FindFilament(long? id) => Book.Filament(id);

    internal void RememberFilament(PlateFilament slot)
    {
        if (slot.FilamentId is not long id) return;
        string key = FilamentMatcher.MappingKey(slot);
        _mappings[key] = id;
        _db.SaveMapping(key, id);
    }

    internal void RememberResin(PlateResin resin)
    {
        if (resin.ResinId is not long id || string.IsNullOrWhiteSpace(resin.SlicerProfile)) return;
        string key = ResinMatcher.MappingKey(resin.SlicerProfile);
        _mappings[key] = id;
        _db.SaveMapping(key, id);
    }

    internal void Changed()
    {
        if (_loading) return;
        IsDirty = true;
        Recalculate();
    }

    private void Recalculate()
    {
        Quote = PricingCalculator.Calculate(Job, Book);
        for (int i = 0; i < Parts.Count && i < Quote.Parts.Count; i++)
            Parts[i].Refresh(Quote.Parts[i]);

        Warnings.Clear();
        foreach (var w in Quote.Warnings) Warnings.Add(w);

        // The extra costs offered follow the kinds of printer the job uses.
        var applicable = PricingCalculator.ApplicableExtras(Job, Book).ToList();
        if (!applicable.Select(e => e.Id).SequenceEqual(ExtraCosts.Select(e => e.Cost.Id)))
        {
            ExtraCosts.Clear();
            foreach (var e in applicable) ExtraCosts.Add(new ExtraCostToggleViewModel(e, this));
        }

        foreach (var name in new[] { nameof(MaterialText), nameof(MachineText), nameof(ElectricityText), nameof(PlateExtrasText),
                     nameof(HasPlateExtras), nameof(PartsText), nameof(SlicingLabel), nameof(SlicingText), nameof(ModelingText),
                     nameof(PostText), nameof(JobExtrasText), nameof(HasJobExtras), nameof(TotalText), nameof(HasParts), nameof(HasExtraCosts) })
            OnPropertyChanged(name);
    }

    private void RebuildParts()
    {
        _loading = true;
        Parts.Clear();
        foreach (var p in Job.Parts) Parts.Add(new PartViewModel(p, this));
        _loading = false;
    }

    // ---------------- Commands ----------------

    [RelayCommand]
    private void AddFiles()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Add sliced files",
            Filter = SliceFileReader.DialogFilter,
            Multiselect = true,
        };
        if (dlg.ShowDialog() == true) AddFiles(dlg.FileNames);
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        if (Job.Finalized)
        {
            (ShowNotes ?? (n => MessageBox.Show(n, "Added files")))("This invoice is finalized, so it is locked. Revert it to draft to add files.");
            return;
        }
        var notes = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var file = SliceFileReader.Read(path);
                var printer = JobBuilder.ChoosePrinter(file, _book.Printers.Values);
                if (printer is null)
                {
                    // First file for this kind of printer: set one up with starting rates the user can change.
                    printer = Printer.NewDefault(file.Technology, string.IsNullOrWhiteSpace(file.PrinterModel) ? null : file.PrinterModel.Trim());
                    _db.SavePrinter(printer);
                    _book = _db.LoadPriceBook();
                    LoadOptions();
                    notes.Add($"Added the printer \"{printer.Name}\" with starting rates ({Format.Money(printer.MachineRatePerHour)}/hr, " +
                              $"{printer.Watts:0} W, {printer.WasteAllowance:P0} waste, {printer.MaterialMarkup:P0} markup). " +
                              "Check them on the Printers tab.");
                }
                notes.AddRange(JobBuilder.AddFile(Job, file, FilamentOptions, _mappings, ResinOptions, printer.Id));
                if (string.IsNullOrWhiteSpace(Job.Title)) Title = Job.Parts.FirstOrDefault()?.Name.Replace('_', ' ') ?? "";
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                notes.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        RebuildParts();
        Changed();
        if (notes.Count == 0) return;
        string text = string.Join("\n\n", notes.Distinct());
        if (ShowNotes != null) ShowNotes(text);
        else MessageBox.Show(text, "Added files", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>A plate typed in by hand, for slicers whose files are not read automatically.</summary>
    [RelayCommand]
    private void AddManualPlate(ManualPlateChoice? choice)
    {
        if (choice is null) return;
        var printer = choice.Printer;
        if (printer is null)
        {
            printer = Printer.NewDefault(choice.Technology);
            _db.SavePrinter(printer);
            _book = _db.LoadPriceBook();
            LoadOptions();
        }

        string name = "New part";
        for (int n = 2; Job.Parts.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); n++) name = $"New part {n}";
        Job.Parts.Add(new JobPart { Name = name, Plates = { JobBuilder.ManualPlate(printer) } });
        if (string.IsNullOrWhiteSpace(Job.Title)) Title = name;
        RebuildParts();
        Changed();
    }

    internal void RemovePart(PartViewModel part)
    {
        Job.Parts.Remove(part.Part);
        Parts.Remove(part);
        Changed();
    }

    internal void RemovePlate(PartViewModel part, PlateViewModel plate)
    {
        part.Part.Plates.Remove(plate.Plate);
        part.Plates.Remove(plate);
        if (part.Part.Plates.Count == 0) RemovePart(part);
        else Changed();
    }

    internal void SetExtraCostIncluded(ExtraCost cost, bool included)
    {
        bool changed = included ? Job.ExcludedExtraCosts.Remove(cost.Id) : Job.ExcludedExtraCosts.Add(cost.Id);
        if (changed) Changed();
    }

    [RelayCommand]
    private void Save() => TrySave();

    /// <summary>Saves the job. Returns false if the user backed out because the invoice number is already used.</summary>
    public bool TrySave()
    {
        if (Job.InvoiceNumber is string number && _db.InvoiceNumberInUse(number, Job.Id) &&
            MessageBox.Show($"Invoice number {number} is already used by another job. Save with this number anyway?",
                "Duplicate invoice number", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return false;

        MergePartsWithSameName();
        _db.SaveJob(Job);
        IsDirty = false;
        return true;
    }

    /// <summary>Renaming a part to match another moves its plates there, which is how plates are regrouped.</summary>
    private void MergePartsWithSameName()
    {
        bool merged = false;
        foreach (var group in Job.Parts.GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList())
        {
            var keep = group.First();
            foreach (var extra in group.Skip(1))
            {
                keep.Plates.AddRange(extra.Plates);
                keep.ChargeSlicing |= extra.ChargeSlicing;
                Job.Parts.Remove(extra);
            }
            merged = true;
        }
        if (merged)
        {
            RebuildParts();
            Recalculate();
        }
    }

    /// <summary>The invoice as it stands, without dating or finalizing it. Saves first so the preview matches the job.</summary>
    [RelayCommand]
    private void PreviewInvoice()
    {
        if (!TrySave()) return;
        OpenPdf(InvoicePdf.Write(Job, Quote, Book, InvoiceKind.Preview));
    }

    /// <summary>Dates the invoice today, marks it finalized, and writes the PDF to send.</summary>
    [RelayCommand]
    private void FinalizeInvoice()
    {
        if (!HasParts && Quote.Total == 0)
        {
            MessageBox.Show("Add at least one sliced file or a charge before finalizing the invoice.", "Finalize invoice");
            return;
        }
        if (!InvoiceActions.ConfirmBusinessDetails(Settings)) return;
        if (Quote.Warnings.Count > 0 &&
            MessageBox.Show("This job has warnings:\n\n" + string.Join("\n", Quote.Warnings) + "\n\nFinalize the invoice anyway?",
                "Finalize invoice", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var today = DateTime.Today;
        if (InvoiceActions.AskRedate(new[] { Job }, today) is not bool redate) return;

        InvoiceActions.Finalize(_db, Job, today, redate);
        RaiseInvoiceState();
        Recalculate();
        if (!TrySave()) return;
        OpenPdf(InvoicePdf.Write(Job, Quote, Book, InvoiceKind.Final));
    }

    [RelayCommand]
    private void RevertToDraft()
    {
        if (!Job.Finalized) return;
        if (MessageBox.Show($"Make this invoice a draft again? {InvoiceActions.RevertNote}", "Revert to draft", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        Job.RevertToDraft();
        RaiseInvoiceState();
        Changed();
    }

    private void RaiseInvoiceState()
    {
        OnPropertyChanged(nameof(InvoiceNumber));
        OnPropertyChanged(nameof(InvoiceDate));
        OnPropertyChanged(nameof(IsFinalized));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ModelingRateText));
        OnPropertyChanged(nameof(PostProcessingText));
    }

    [RelayCommand]
    private void CreateDetail()
    {
        if (!TrySave()) return;
        OpenPdf(InvoicePdf.Write(Job, Quote, Book, InvoiceKind.Internal));
    }

    private static void OpenPdf(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}

/// <summary>One entry in the Add plate by hand menu: an existing printer, or a new one of a kind not set up yet.</summary>
public record ManualPlateChoice(string Label, Printer? Printer, PrintTechnology Technology);

/// <summary>An extra cost that applies to this job, with a checkbox to leave it off this job only.</summary>
public class ExtraCostToggleViewModel : ObservableObject
{
    private readonly JobEditorViewModel _editor;

    public ExtraCost Cost { get; }

    public ExtraCostToggleViewModel(ExtraCost cost, JobEditorViewModel editor)
    {
        Cost = cost;
        _editor = editor;
    }

    public string Label => $"{Cost.Name} ({Format.Rate(Cost.Cost)} {ExtraCost.BasisLabel(Cost.Basis)})";

    public bool Included
    {
        get => !_editor.Job.ExcludedExtraCosts.Contains(Cost.Id);
        set
        {
            if (value == Included) return;
            _editor.SetExtraCostIncluded(Cost, value);
            OnPropertyChanged();
        }
    }
}

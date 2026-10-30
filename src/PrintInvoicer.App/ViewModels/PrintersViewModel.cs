using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintInvoicer.Core.Data;
using PrintInvoicer.Core.Models;

namespace PrintInvoicer.App.ViewModels;

/// <summary>The Printers tab: each printer's rates, and the extra costs you charge on top. Edits save immediately.</summary>
public partial class PrintersViewModel : ObservableObject
{
    private readonly Database _db;

    public ObservableCollection<PrinterRowViewModel> Printers { get; } = new();
    public ObservableCollection<ExtraCostRowViewModel> ExtraCosts { get; } = new();

    [ObservableProperty] private PrinterRowViewModel? _selectedPrinter;
    [ObservableProperty] private ExtraCostRowViewModel? _selectedExtraCost;

    /// <summary>Raised after any edit, so job totals and the Materials tab can follow.</summary>
    public event Action? Changed;

    public PrintersViewModel(Database db)
    {
        _db = db;
        Reload();
    }

    public void Reload()
    {
        Printers.Clear();
        foreach (var p in _db.GetPrinters()) Printers.Add(new PrinterRowViewModel(p, this));
        ExtraCosts.Clear();
        foreach (var e in _db.GetExtraCosts()) ExtraCosts.Add(new ExtraCostRowViewModel(e, this));
    }

    internal void Save(Printer p)
    {
        _db.SavePrinter(p);
        Changed?.Invoke();
    }

    internal void Save(ExtraCost e)
    {
        _db.SaveExtraCost(e);
        Changed?.Invoke();
    }

    [RelayCommand]
    private void AddFdmPrinter() => AddPrinter(PrintTechnology.Fdm);

    [RelayCommand]
    private void AddResinPrinter() => AddPrinter(PrintTechnology.Resin);

    private void AddPrinter(PrintTechnology t)
    {
        var p = Printer.NewDefault(t);
        _db.SavePrinter(p);
        var row = new PrinterRowViewModel(p, this);
        Printers.Add(row);
        SelectedPrinter = row;
        Changed?.Invoke();
    }

    [RelayCommand]
    private void DeletePrinter()
    {
        if (SelectedPrinter is null) return;
        if (MessageBox.Show($"Delete {SelectedPrinter.Printer.Name}?", "Delete printer", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        if (_db.DeletePrinter(SelectedPrinter.Printer.Id))
            Printers.Remove(SelectedPrinter);
        else
        {
            SelectedPrinter.Refresh(false);
            MessageBox.Show("Saved jobs were printed on this printer, so it was marked inactive instead. Those jobs keep its rates.",
                "Delete printer");
        }
        Changed?.Invoke();
    }

    [RelayCommand]
    private void AddExtraCost()
    {
        var e = new ExtraCost { Name = "New cost", Cost = 0.50m, Basis = CostBasis.PerPrint };
        _db.SaveExtraCost(e);
        var row = new ExtraCostRowViewModel(e, this);
        ExtraCosts.Add(row);
        SelectedExtraCost = row;
        Changed?.Invoke();
    }

    [RelayCommand]
    private void DeleteExtraCost()
    {
        if (SelectedExtraCost is null) return;
        if (MessageBox.Show($"Delete {SelectedExtraCost.Cost.Name}? It comes off every job, including invoices you regenerate later. " +
                            "To stop charging it without changing older invoices, untick Active instead.",
                "Delete extra cost", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        _db.DeleteExtraCost(SelectedExtraCost.Cost.Id);
        ExtraCosts.Remove(SelectedExtraCost);
        Changed?.Invoke();
    }
}

public record ChoiceOption<T>(T Value, string Name);

public class PrinterRowViewModel : ObservableObject
{
    private readonly PrintersViewModel _owner;

    public Printer Printer { get; }

    public PrinterRowViewModel(Printer printer, PrintersViewModel owner)
    {
        Printer = printer;
        _owner = owner;
    }

    public static ChoiceOption<PrintTechnology>[] TechnologyOptions { get; } =
        [new(PrintTechnology.Fdm, "FDM"), new(PrintTechnology.Resin, "Resin")];

    private void Set<T>(T current, T value, Action<Printer, T> apply, string name)
    {
        if (SetProperty(current, value, Printer, apply, name)) _owner.Save(Printer);
    }

    public string Name { get => Printer.Name; set => Set(Printer.Name, value ?? "", (p, v) => p.Name = v, nameof(Name)); }
    public PrintTechnology Technology { get => Printer.Technology; set => Set(Printer.Technology, value, (p, v) => p.Technology = v, nameof(Technology)); }
    public decimal MachineRate { get => Printer.MachineRatePerHour; set => Set(Printer.MachineRatePerHour, Math.Max(0, value), (p, v) => p.MachineRatePerHour = v, nameof(MachineRate)); }
    public decimal Watts { get => Printer.Watts; set => Set(Printer.Watts, Math.Max(0, value), (p, v) => p.Watts = v, nameof(Watts)); }

    // Percent views over the stored fractions.
    public decimal WastePercent { get => Printer.WasteAllowance * 100; set => Set(Printer.WasteAllowance, Math.Max(0, value) / 100, (p, v) => p.WasteAllowance = v, nameof(WastePercent)); }
    public decimal MarkupPercent { get => Printer.MaterialMarkup * 100; set => Set(Printer.MaterialMarkup, Math.Max(0, value) / 100, (p, v) => p.MaterialMarkup = v, nameof(MarkupPercent)); }

    public bool Active { get => Printer.Active; set => Set(Printer.Active, value, (p, v) => p.Active = v, nameof(Active)); }
    public string Notes { get => Printer.Notes; set => Set(Printer.Notes, value ?? "", (p, v) => p.Notes = v, nameof(Notes)); }

    internal void Refresh(bool active)
    {
        Printer.Active = active;
        OnPropertyChanged(nameof(Active));
    }
}

public class ExtraCostRowViewModel : ObservableObject
{
    private readonly PrintersViewModel _owner;

    public ExtraCost Cost { get; }

    public ExtraCostRowViewModel(ExtraCost cost, PrintersViewModel owner)
    {
        Cost = cost;
        _owner = owner;
    }

    public static ChoiceOption<CostBasis>[] BasisOptions { get; } = Enum.GetValues<CostBasis>()
        .Select(b => new ChoiceOption<CostBasis>(b, ExtraCost.BasisLabel(b))).ToArray();

    /// <summary>-1 stands for every printer, since a combo box cannot select a null value.</summary>
    public static ChoiceOption<int>[] AppliesToOptions { get; } =
        [new(-1, "All printers"), new((int)PrintTechnology.Fdm, "FDM only"), new((int)PrintTechnology.Resin, "Resin only")];

    private void Set<T>(T current, T value, Action<ExtraCost, T> apply, string name)
    {
        if (SetProperty(current, value, Cost, apply, name)) _owner.Save(Cost);
    }

    public string Name { get => Cost.Name; set => Set(Cost.Name, value ?? "", (c, v) => c.Name = v, nameof(Name)); }
    public decimal Amount { get => Cost.Cost; set => Set(Cost.Cost, Math.Max(0, value), (c, v) => c.Cost = v, nameof(Amount)); }
    public CostBasis Basis { get => Cost.Basis; set => Set(Cost.Basis, value, (c, v) => c.Basis = v, nameof(Basis)); }

    public int AppliesTo
    {
        get => Cost.AppliesTo is PrintTechnology t ? (int)t : -1;
        set => Set(Cost.AppliesTo, value < 0 ? null : (PrintTechnology?)value, (c, v) => c.AppliesTo = v, nameof(AppliesTo));
    }

    public bool Active { get => Cost.Active; set => Set(Cost.Active, value, (c, v) => c.Active = v, nameof(Active)); }
    public string Notes { get => Cost.Notes; set => Set(Cost.Notes, value ?? "", (c, v) => c.Notes = v, nameof(Notes)); }
}

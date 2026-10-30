using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintInvoicer.Core.Data;
using PrintInvoicer.Core.Models;

namespace PrintInvoicer.App.ViewModels;

/// <summary>The Materials tab: filaments for FDM printers and resins for resin printers.</summary>
public partial class FilamentsViewModel : ObservableObject
{
    private readonly Database _db;

    public ObservableCollection<FilamentRowViewModel> Filaments { get; } = new();
    public ObservableCollection<ResinRowViewModel> Resins { get; } = new();

    [ObservableProperty] private FilamentRowViewModel? _selected;
    [ObservableProperty] private ResinRowViewModel? _selectedResin;

    /// <summary>Each list shows when a printer of its kind is set up, or before any printer is.</summary>
    [ObservableProperty] private bool _showFilaments = true;
    [ObservableProperty] private bool _showResins = true;

    public FilamentsViewModel(Database db)
    {
        _db = db;
        foreach (var f in db.GetFilaments()) Filaments.Add(new FilamentRowViewModel(f, db));
        foreach (var r in db.GetResins()) Resins.Add(new ResinRowViewModel(r, db));
        RefreshVisibility();
    }

    public void RefreshVisibility()
    {
        var printers = _db.GetPrinters().Where(p => p.Active).ToList();
        ShowFilaments = printers.Count == 0 || printers.Any(p => p.Technology == PrintTechnology.Fdm);
        ShowResins = printers.Count == 0 || printers.Any(p => p.Technology == PrintTechnology.Resin);
    }

    [RelayCommand]
    private void Add()
    {
        var f = new Filament { Vendor = "New", Material = "PLA", SpoolPrice = 20m, SpoolGrams = 1000m };
        _db.SaveFilament(f);
        var row = new FilamentRowViewModel(f, _db);
        Filaments.Add(row);
        Selected = row;
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        if (MessageBox.Show($"Delete {Selected.Filament.DisplayName}?", "Delete filament", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        if (_db.DeleteFilament(Selected.Filament.Id))
        {
            Filaments.Remove(Selected);
        }
        else
        {
            Selected.Refresh(false);
            MessageBox.Show("Saved jobs use this filament, so it was marked inactive instead. It will no longer be offered for new plates.",
                "Delete filament");
        }
    }

    [RelayCommand]
    private void AddResin()
    {
        var r = new Resin { Vendor = "New", Name = "Standard resin", BottlePrice = 30m, BottleSize = 1000m };
        _db.SaveResin(r);
        var row = new ResinRowViewModel(r, _db);
        Resins.Add(row);
        SelectedResin = row;
    }

    [RelayCommand]
    private void DeleteResin()
    {
        if (SelectedResin is null) return;
        if (MessageBox.Show($"Delete {SelectedResin.Resin.DisplayName}?", "Delete resin", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        if (_db.DeleteResin(SelectedResin.Resin.Id))
        {
            Resins.Remove(SelectedResin);
        }
        else
        {
            SelectedResin.Refresh(false);
            MessageBox.Show("Saved jobs use this resin, so it was marked inactive instead. It will no longer be offered for new plates.",
                "Delete resin");
        }
    }
}

/// <summary>Edits save straight to the database.</summary>
public class FilamentRowViewModel : ObservableObject
{
    private readonly Database _db;

    public Filament Filament { get; }

    public FilamentRowViewModel(Filament filament, Database db)
    {
        Filament = filament;
        _db = db;
    }

    private void Set<T>(T current, T value, Action<Filament, T> apply, string name)
    {
        if (SetProperty(current, value, Filament, apply, name))
        {
            _db.SaveFilament(Filament);
            OnPropertyChanged(nameof(CostPerKg));
        }
    }

    public string Vendor { get => Filament.Vendor; set => Set(Filament.Vendor, value ?? "", (f, v) => f.Vendor = v, nameof(Vendor)); }
    public string Material { get => Filament.Material; set => Set(Filament.Material, value ?? "", (f, v) => f.Material = v, nameof(Material)); }
    public string ColorName { get => Filament.ColorName; set => Set(Filament.ColorName, value ?? "", (f, v) => f.ColorName = v, nameof(ColorName)); }

    public string ColorHex
    {
        get => Filament.ColorHex;
        set => Set(Filament.ColorHex, NormalizeHex(value), (f, v) => f.ColorHex = v, nameof(ColorHex));
    }

    internal static string NormalizeHex(string? value)
    {
        value = (value ?? "").Trim().ToUpperInvariant();
        return value.Length > 0 && !value.StartsWith('#') ? "#" + value : value;
    }

    public decimal SpoolPrice { get => Filament.SpoolPrice; set => Set(Filament.SpoolPrice, Math.Max(0, value), (f, v) => f.SpoolPrice = v, nameof(SpoolPrice)); }
    public decimal SpoolGrams { get => Filament.SpoolGrams; set => Set(Filament.SpoolGrams, Math.Max(0, value), (f, v) => f.SpoolGrams = v, nameof(SpoolGrams)); }
    public bool Active { get => Filament.Active; set => Set(Filament.Active, value, (f, v) => f.Active = v, nameof(Active)); }
    public string Notes { get => Filament.Notes; set => Set(Filament.Notes, value ?? "", (f, v) => f.Notes = v, nameof(Notes)); }

    public string CostPerKg => Format.Money(Filament.CostPerGram * 1000);

    internal void Refresh(bool active)
    {
        Filament.Active = active;
        OnPropertyChanged(nameof(Active));
    }
}

/// <summary>Edits save straight to the database.</summary>
public class ResinRowViewModel : ObservableObject
{
    private readonly Database _db;

    public Resin Resin { get; }

    public ResinRowViewModel(Resin resin, Database db)
    {
        Resin = resin;
        _db = db;
    }

    public static string[] UnitOptions { get; } = ["g", "mL"];

    private void Set<T>(T current, T value, Action<Resin, T> apply, string name)
    {
        if (SetProperty(current, value, Resin, apply, name))
        {
            _db.SaveResin(Resin);
            OnPropertyChanged(nameof(CostPerLiter));
        }
    }

    public string Vendor { get => Resin.Vendor; set => Set(Resin.Vendor, value ?? "", (r, v) => r.Vendor = v, nameof(Vendor)); }
    public string Name { get => Resin.Name; set => Set(Resin.Name, value ?? "", (r, v) => r.Name = v, nameof(Name)); }
    public string ColorName { get => Resin.ColorName; set => Set(Resin.ColorName, value ?? "", (r, v) => r.ColorName = v, nameof(ColorName)); }
    public string ColorHex { get => Resin.ColorHex; set => Set(Resin.ColorHex, FilamentRowViewModel.NormalizeHex(value), (r, v) => r.ColorHex = v, nameof(ColorHex)); }
    public decimal BottlePrice { get => Resin.BottlePrice; set => Set(Resin.BottlePrice, Math.Max(0, value), (r, v) => r.BottlePrice = v, nameof(BottlePrice)); }
    public decimal BottleSize { get => Resin.BottleSize; set => Set(Resin.BottleSize, Math.Max(0, value), (r, v) => r.BottleSize = v, nameof(BottleSize)); }

    public string BottleUnit
    {
        get => Resin.BottleUnit == Core.Models.BottleUnit.Milliliters ? "mL" : "g";
        set => Set(Resin.BottleUnit, value == "mL" ? Core.Models.BottleUnit.Milliliters : Core.Models.BottleUnit.Grams, (r, v) => r.BottleUnit = v, nameof(BottleUnit));
    }

    public decimal Density { get => Resin.Density; set => Set(Resin.Density, Math.Max(0.1m, value), (r, v) => r.Density = v, nameof(Density)); }
    public bool Active { get => Resin.Active; set => Set(Resin.Active, value, (r, v) => r.Active = v, nameof(Active)); }
    public string Notes { get => Resin.Notes; set => Set(Resin.Notes, value ?? "", (r, v) => r.Notes = v, nameof(Notes)); }

    public string CostPerLiter => Format.Money(Resin.CostPerMl * 1000);

    internal void Refresh(bool active)
    {
        Resin.Active = active;
        OnPropertyChanged(nameof(Active));
    }
}

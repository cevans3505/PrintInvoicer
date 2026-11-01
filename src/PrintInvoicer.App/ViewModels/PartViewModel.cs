using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintInvoicer.Core.Models;
using PrintInvoicer.Core.Pricing;

namespace PrintInvoicer.App.ViewModels;

public partial class PartViewModel : ObservableObject
{
    private readonly JobEditorViewModel _editor;

    public JobPart Part { get; }
    public ObservableCollection<PlateViewModel> Plates { get; } = new();

    public PartViewModel(JobPart part, JobEditorViewModel editor)
    {
        Part = part;
        _editor = editor;
        foreach (var p in part.Plates) Plates.Add(new PlateViewModel(p, this, editor));
    }

    public string Name
    {
        get => Part.Name;
        set { if (SetProperty(Part.Name, value, Part, (p, v) => p.Name = v)) _editor.Changed(); }
    }

    public bool ChargeSlicing
    {
        get => Part.ChargeSlicing;
        set { if (SetProperty(Part.ChargeSlicing, value, Part, (p, v) => p.ChargeSlicing = v)) _editor.Changed(); }
    }

    [ObservableProperty] private string _summary = "";

    internal void Refresh(PartQuote q)
    {
        Summary = $"{q.Quantity} pcs x {Format.Rate(q.UnitPrice)} = {Format.Money(q.LineAmount)}";
        for (int i = 0; i < Plates.Count && i < q.Plates.Count; i++)
            Plates[i].Refresh(q.Plates[i]);
    }

    [RelayCommand]
    private void Remove() => _editor.RemovePart(this);

    internal void RemovePlate(PlateViewModel plate) => _editor.RemovePlate(this, plate);
}

public partial class PlateViewModel : ObservableObject
{
    private readonly PartViewModel _part;
    private readonly JobEditorViewModel _editor;

    public JobPlate Plate { get; }
    public ObservableCollection<SlotViewModel> Slots { get; } = new();

    public PlateViewModel(JobPlate plate, PartViewModel part, JobEditorViewModel editor)
    {
        Plate = plate;
        _part = part;
        _editor = editor;
        foreach (var s in plate.Filaments) Slots.Add(new SlotViewModel(s, plate, editor));
    }

    public string Title => Plate.IsManual ? "Entered by hand"
        : Plate.Technology == PrintTechnology.Resin ? Path.GetFileName(Plate.SourceFile)
        : $"{Path.GetFileName(Plate.SourceFile)}, plate {Plate.PlateIndex}";
    public string SourceFile => Plate.IsManual ? "Type the print time and material used" : Plate.SourceFile;
    public byte[]? Thumbnail => Plate.Thumbnail;
    public bool HasThumbnail => Plate.Thumbnail is { Length: > 0 };
    public string SlicedTimeText => Plate.IsManual ? "" : $"sliced {Format.Duration(Plate.SlicedSeconds)}";

    public bool IsResin => Plate.Technology == PrintTechnology.Resin;
    public bool IsFdm => !IsResin;

    public List<Printer> PrinterOptions => _editor.PrintersFor(Plate.Technology, Plate.PrinterId);

    public long? PrinterId
    {
        get => Plate.PrinterId;
        set { if (value is not null && SetProperty(Plate.PrinterId, value, Plate, (p, v) => p.PrinterId = v)) _editor.Changed(); }
    }

    public string LayerText => Plate.LayerCount > 0 ? $"{Plate.LayerCount:N0} layers at {Plate.LayerHeightMm:0.###} mm" : "";

    // ---- Resin plates: one resin for the whole vat ----

    private PlateResin Resin => Plate.Resin ??= new PlateResin();

    public List<Resin> ResinOptions => _editor.ResinOptions;
    public string ResinProfileLabel => Resin.SlicerProfile;

    public long? ResinId
    {
        get => Resin.ResinId;
        set
        {
            if (SetProperty(Resin.ResinId, value, Resin, (r, v) => r.ResinId = v))
            {
                _editor.RememberResin(Resin);
                _editor.Changed();
            }
        }
    }

    public bool ResinCustomerSupplied
    {
        get => Resin.CustomerSupplied;
        set { if (SetProperty(Resin.CustomerSupplied, value, Resin, (r, v) => r.CustomerSupplied = v)) _editor.Changed(); }
    }

    /// <summary>mL per print. Typing a different value overrides the slicer's number; on a plate typed in, it is the value.</summary>
    public decimal Ml
    {
        get => Resin.EffectiveMl;
        set
        {
            value = Math.Max(0, value);
            if (Plate.IsManual)
            {
                if (SetProperty(Resin.SlicedMl, value, Resin, (r, v) => r.SlicedMl = v)) _editor.Changed();
                return;
            }
            decimal? newOverride = value == Resin.SlicedMl ? null : value;
            if (newOverride == Resin.OverrideMl) return;
            Resin.OverrideMl = newOverride;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMlOverridden));
            _editor.Changed();
        }
    }

    public bool IsMlOverridden => Resin.OverrideMl.HasValue;
    public string SlicedMlText => $"{Resin.SlicedMl:0.##} mL sliced";

    [ObservableProperty] private string _resinCostText = "";
    [ObservableProperty] private string? _resinCostTip;

    [RelayCommand]
    private void ResetMl()
    {
        Resin.OverrideMl = null;
        OnPropertyChanged(nameof(Ml));
        OnPropertyChanged(nameof(IsMlOverridden));
        _editor.Changed();
    }

    public int Copies
    {
        get => Plate.Copies;
        set { if (SetProperty(Plate.Copies, Math.Max(0, value), Plate, (p, v) => p.Copies = v)) { OnPropertyChanged(nameof(FailedOfText)); _editor.Changed(); } }
    }

    public int PartsPerPlate
    {
        get => Plate.PartsPerPlate;
        set
        {
            bool changed = SetProperty(Plate.PartsPerPlate, Math.Max(0, value), Plate, (p, v) => p.PartsPerPlate = v);
            // Typing a count, even the same one, means someone has looked at it.
            if (changed || !Plate.PartsCountConfirmed) ConfirmCount();
            else return;
            OnPropertyChanged(nameof(FailedOfText));
        }
    }

    public bool IsCountUnconfirmed => !Plate.PartsCountConfirmed;

    /// <summary>The count shown is right; clears the resin part-count warning.</summary>
    [RelayCommand]
    private void ConfirmCount()
    {
        Plate.PartsCountConfirmed = true;
        OnPropertyChanged(nameof(IsCountUnconfirmed));
        _editor.Changed();
    }

    /// <summary>Print time as h:mm. Typing a different value overrides the slicer estimate.</summary>
    public string TimeText
    {
        get => Format.Duration(Plate.EffectiveSeconds);
        set
        {
            int? seconds = Format.ParseDuration(value);
            if (seconds is null) return;
            if (Plate.IsManual)
            {
                if (seconds == Plate.SlicedSeconds) return;
                Plate.SlicedSeconds = seconds.Value;
                OnPropertyChanged();
                _editor.Changed();
                return;
            }
            int? newOverride = Format.Duration(seconds.Value) == Format.Duration(Plate.SlicedSeconds) ? null : seconds;
            if (newOverride == Plate.OverrideSeconds) return;
            Plate.OverrideSeconds = newOverride;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTimeOverridden));
            _editor.Changed();
        }
    }

    public bool IsTimeOverridden => Plate.OverrideSeconds.HasValue;

    /// <summary>Ticking it starts the count at 1; unticking keeps the count and note in case it was a mis-click.</summary>
    public bool HasFailures
    {
        get => Plate.HasFailures;
        set
        {
            if (!SetProperty(Plate.HasFailures, value, Plate, (p, v) => p.HasFailures = v)) return;
            if (value && Plate.FailedCount < 1) FailedCount = 1;
            OnPropertyChanged(nameof(FailedOfText));
            _editor.Changed();
        }
    }

    public int FailedCount
    {
        get => Plate.FailedCount;
        set
        {
            value = Math.Clamp(value, 0, Math.Max(Plate.TotalObjects, 0));
            if (SetProperty(Plate.FailedCount, value, Plate, (p, v) => p.FailedCount = v))
            {
                OnPropertyChanged(nameof(FailedOfText));
                _editor.Changed();
            }
        }
    }

    public string FailedOfText => $"of {Plate.TotalObjects} objects failed";

    public string FailureNote
    {
        get => Plate.FailureNote;
        set { if (SetProperty(Plate.FailureNote, value ?? "", Plate, (p, v) => p.FailureNote = v)) _editor.Changed(); }
    }

    [ObservableProperty] private string _costText = "";

    internal void Refresh(PlateQuote q)
    {
        string amount = q.Resin is ResinQuote r ? $"{r.Ml:0.##} mL" : $"{q.Slots.Sum(s => s.Grams):0.##} g";
        string extras = q.Extras.Count > 0 ? $" + extras {Format.Money(q.ExtrasAmount)}" : "";
        CostText = $"{amount}, {Format.Duration(Plate.EffectiveSeconds)} per print. "
                 + $"Material {Format.Money(q.Material)} + machine {Format.Money(q.Machine)} + power {Format.Money(q.Electricity)}{extras} "
                 + $"= {Format.Money(q.Total)} for {Plate.Copies} print(s)";
        for (int i = 0; i < Slots.Count && i < q.Slots.Count; i++)
            Slots[i].Refresh(q.Slots[i], q.Printer);

        if (q.Resin is ResinQuote rq)
        {
            ResinCostText = rq.Plate.CustomerSupplied ? "$0" : rq.Resin is null ? "pick a resin" : Format.Money(rq.Material);
            ResinCostTip = rq.Resin is null || rq.Plate.CustomerSupplied ? null
                : $"{Format.Money(rq.CostPerMl * 1000)}/L before waste and markup ({q.Printer.MaterialFactor:0.###}x on this printer), for all copies";
        }
    }

    [RelayCommand]
    private void ResetTime()
    {
        Plate.OverrideSeconds = null;
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(IsTimeOverridden));
        _editor.Changed();
    }

    [RelayCommand]
    private void Remove() => _part.RemovePlate(this);
}

public partial class SlotViewModel : ObservableObject
{
    private readonly JobEditorViewModel _editor;

    private readonly JobPlate _plate;

    public PlateFilament Slot { get; }

    public SlotViewModel(PlateFilament slot, JobPlate plate, JobEditorViewModel editor)
    {
        Slot = slot;
        _plate = plate;
        _editor = editor;
    }

    public List<Filament> FilamentOptions => _editor.FilamentOptions;
    public string ColorHex => Slot.SlicerColorHex;
    public string SlotLabel => $"Slot {Slot.SlotId}";
    public string SlicerLabel => _plate.IsManual ? "Filament" : string.IsNullOrEmpty(Slot.SlicerProfile) ? Slot.SlicerType : Slot.SlicerProfile;
    public string SlicedGramsText => $"{Slot.SlicedGrams:0.##} g sliced";

    public long? FilamentId
    {
        get => Slot.FilamentId;
        set
        {
            if (SetProperty(Slot.FilamentId, value, Slot, (s, v) => s.FilamentId = v))
            {
                _editor.RememberFilament(Slot);
                _editor.Changed();
            }
        }
    }

    public bool CustomerSupplied
    {
        get => Slot.CustomerSupplied;
        set { if (SetProperty(Slot.CustomerSupplied, value, Slot, (s, v) => s.CustomerSupplied = v)) _editor.Changed(); }
    }

    /// <summary>Grams actually used. Typing a different value overrides the slicer's number.</summary>
    public decimal Grams
    {
        get => Slot.EffectiveGrams;
        set
        {
            value = Math.Max(0, value);
            if (_plate.IsManual)
            {
                if (SetProperty(Slot.SlicedGrams, value, Slot, (s, v) => s.SlicedGrams = v)) _editor.Changed();
                return;
            }
            decimal? newOverride = value == Slot.SlicedGrams ? null : value;
            if (newOverride == Slot.OverrideGrams) return;
            Slot.OverrideGrams = newOverride;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGramsOverridden));
            _editor.Changed();
        }
    }

    public bool IsGramsOverridden => Slot.OverrideGrams.HasValue;

    [ObservableProperty] private string _costText = "";
    [ObservableProperty] private string? _costTip;

    internal void Refresh(SlotQuote q, Printer printer)
    {
        CostText = Slot.CustomerSupplied ? "$0"
            : q.Filament is null ? "pick a filament"
            : Format.Money(q.Material);
        CostTip = q.Filament is null || Slot.CustomerSupplied ? null
            : $"{Format.Money(q.CostPerGram * 1000)}/kg before waste and markup ({printer.MaterialFactor:0.###}x on this printer), for all copies";
    }

    [RelayCommand]
    private void ResetGrams()
    {
        Slot.OverrideGrams = null;
        OnPropertyChanged(nameof(Grams));
        OnPropertyChanged(nameof(IsGramsOverridden));
        _editor.Changed();
    }
}

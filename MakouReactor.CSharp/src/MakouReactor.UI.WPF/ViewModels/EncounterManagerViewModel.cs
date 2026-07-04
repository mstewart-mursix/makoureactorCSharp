using CommunityToolkit.Mvvm.ComponentModel;

using MakouReactor.Core.Models;

namespace MakouReactor.UI.WPF.ViewModels;

public sealed partial class EncounterManagerViewModel : ObservableObject
{
    private readonly FieldPC _field;
    private readonly EncounterFile _encounters;

    [ObservableProperty]
    private bool _isModified;

    public EncounterManagerViewModel(string fieldName, FieldPC field)
    {
        if (field.Encounters == null)
            throw new ArgumentException("Field does not contain parsed encounter data.", nameof(field));

        _field = field;
        _encounters = field.Encounters;
        FieldName = fieldName;
        Table1 = new EncounterTableViewModel(0, "Encounters 1", _encounters.Table1);
        Table2 = new EncounterTableViewModel(1, "Encounters 2", _encounters.Table2);
        Table1.PropertyChanged += (_, _) => IsModified = true;
        Table2.PropertyChanged += (_, _) => IsModified = true;
    }

    public string FieldName { get; }

    public EncounterTableViewModel Table1 { get; }

    public EncounterTableViewModel Table2 { get; }

    public string StatusText =>
        IsModified ? "Encounter changes are unapplied." : "Encounter tables loaded.";

    partial void OnIsModifiedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    public void ApplyChanges()
    {
        _encounters.SetTable(0, Table1.ToModel());
        _encounters.SetTable(1, Table2.ToModel());
        _field.ApplyEncounterChanges();
        IsModified = false;
    }
}

public sealed partial class EncounterTableViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private byte _rate;

    public EncounterTableViewModel(int index, string name, EncounterTable table)
    {
        Index = index;
        Name = name;
        _enabled = table.Enabled;
        _rate = table.Rate;
        Rows = table.StandardBattles
            .Select((battle, slot) => new EncounterBattleViewModel("Standard", slot, battle))
            .Concat(table.SpecialBattles.Select((battle, slot) => new EncounterBattleViewModel("Special", slot, battle)))
            .ToArray();
        foreach (var row in Rows)
            row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Rows));
    }

    public int Index { get; }

    public string Name { get; }

    public IReadOnlyList<EncounterBattleViewModel> Rows { get; }

    public EncounterTable ToModel() =>
        new(
            Enabled,
            Rate,
            Rows.Where(static row => row.Type == "Standard").Select(static row => row.ToModel()).ToArray(),
            Rows.Where(static row => row.Type == "Special").Select(static row => row.ToModel()).ToArray());
}

public sealed partial class EncounterBattleViewModel : ObservableObject
{
    [ObservableProperty]
    private ushort _battleId;

    [ObservableProperty]
    private byte _probability;

    public EncounterBattleViewModel(string type, int slot, EncounterBattle battle)
    {
        Type = type;
        Slot = slot;
        _battleId = battle.BattleId;
        _probability = battle.Probability;
    }

    public string Type { get; }

    public int Slot { get; }

    public EncounterBattle ToModel() => new(BattleId, Probability);
}

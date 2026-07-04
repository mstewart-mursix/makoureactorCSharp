using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using MakouReactor.Core.Models;

namespace MakouReactor.UI.WPF.ViewModels;

public sealed partial class ScriptManagerViewModel : ObservableObject
{
    private Section1File? _section;

    public ObservableCollection<GroupScriptViewModel> Groups { get; } = [];
    public ObservableCollection<ScriptEntryViewModel> Scripts { get; } = [];
    public ObservableCollection<OpcodeEntryViewModel> Opcodes { get; } = [];

    [ObservableProperty]
    private GroupScriptViewModel? _selectedGroup;

    [ObservableProperty]
    private ScriptEntryViewModel? _selectedScript;

    [ObservableProperty]
    private OpcodeEntryViewModel? _selectedOpcode;

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private string _dirtySummary = string.Empty;

    public void SetDirtyState(bool isModified, string? fieldName = null)
    {
        IsModified = isModified;
        DirtySummary = isModified
            ? string.IsNullOrWhiteSpace(fieldName)
                ? "Script data modified"
                : $"{fieldName} script data modified"
            : string.Empty;
    }

    public void LoadSection(Section1File? section)
    {
        _section = section;
        Groups.Clear();
        Scripts.Clear();
        Opcodes.Clear();
        SelectedGroup = null;
        SelectedScript = null;
        SelectedOpcode = null;

        if (section == null)
            return;

        foreach (var group in section.GrpScripts.Select((group, index) =>
                     new GroupScriptViewModel(index, group.Name, group.TypeString)))
        {
            Groups.Add(group);
        }
    }

    public void SelectGroup(int groupIndex)
    {
        Scripts.Clear();
        Opcodes.Clear();
        SelectedScript = null;
        SelectedOpcode = null;

        if (_section == null || groupIndex < 0 || groupIndex >= _section.GrpScripts.Count)
        {
            SelectedGroup = null;
            return;
        }

        SelectedGroup = Groups.FirstOrDefault(group => group.Index == groupIndex);
        foreach (var script in _section.GrpScripts[groupIndex].Scripts
                     .Select((script, index) => new ScriptEntryViewModel(index, ScriptName(index), script.Size)))
        {
            Scripts.Add(script);
        }
    }

    public void SelectScript(int scriptIndex)
    {
        Opcodes.Clear();
        SelectedOpcode = null;

        if (_section == null ||
            SelectedGroup == null ||
            scriptIndex < 0 ||
            scriptIndex >= _section.GrpScripts[SelectedGroup.Index].Scripts.Count)
        {
            SelectedScript = null;
            return;
        }

        var script = _section.GrpScripts[SelectedGroup.Index].Scripts[scriptIndex];
        SelectedScript = Scripts.FirstOrDefault(item => item.Index == scriptIndex);
        foreach (var opcode in script.RawOpcodes.Select(static opcode =>
                     new OpcodeEntryViewModel(
                         opcode.Offset,
                         opcode.Name,
                         opcode.Arguments,
                         opcode.Warning)))
        {
            Opcodes.Add(opcode);
        }
    }

    public static string ScriptName(int index) => index switch
    {
        0 => "S0 - Init",
        1 => "S0 - Main",
        2 => "S1 - Talk",
        3 => "S2 - Contact",
        _ => $"Script {index - 1}",
    };
}

public sealed record GroupScriptViewModel(int Index, string Name, string Type);
public sealed record ScriptEntryViewModel(int Index, string Name, int Size);
public sealed record OpcodeEntryViewModel(int Offset, string Name, string Arguments, string Warning);

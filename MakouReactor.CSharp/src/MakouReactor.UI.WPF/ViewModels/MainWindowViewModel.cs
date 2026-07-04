using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MakouReactor.Core.Models;
using MakouReactor.Core.Services;

namespace MakouReactor.UI.WPF.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isArchiveOpen;

    [ObservableProperty]
    private bool _isFieldSelected;

    [ObservableProperty]
    private bool _isCurrentFieldModified;

    [ObservableProperty]
    private bool _isFf7Configured;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage = string.Empty;

    [ObservableProperty]
    private string _archiveName = "No archive open";

    [ObservableProperty]
    private string _archivePath = string.Empty;

    [ObservableProperty]
    private ShellArchiveType _archiveType;

    [ObservableProperty]
    private FieldPC? _currentField;

    [ObservableProperty]
    private string _currentFieldName = string.Empty;

    [ObservableProperty]
    private int? _selectedGroupIndex;

    [ObservableProperty]
    private string _selectedGroupName = string.Empty;

    [ObservableProperty]
    private int? _selectedScriptIndex;

    [ObservableProperty]
    private string _selectedScriptName = string.Empty;

    [ObservableProperty]
    private int? _selectedOpcodeOffset;

    [ObservableProperty]
    private string _selectedOpcodeName = string.Empty;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private ShellNavigationTarget? _pendingNavigationTarget;

    public MainWindowViewModel()
    {
        FieldList = new FieldListViewModel();
        Preview = new PreviewViewModel();
        ScriptManager = new ScriptManagerViewModel();
        ArchiveManager = new ArchiveManagerViewModel();
        StatusBar = new StatusBarViewModel();
        SaveCommand = new RelayCommand(static () => { }, () => CanSave);
        SaveAsCommand = new RelayCommand(static () => { }, () => IsArchiveOpen);
        CloseCommand = new RelayCommand(static () => { }, () => IsArchiveOpen || IsFieldSelected);
        ExportCurrentMapCommand = new RelayCommand(static () => { }, () => IsArchiveOpen && IsFieldSelected);
        ImportCurrentMapCommand = new RelayCommand(static () => { }, () => IsArchiveOpen && IsFieldSelected);
        MassExportCommand = new RelayCommand(static () => { }, () => IsArchiveOpen);
        RunFf7Command = new RelayCommand(static () => { }, () => IsFf7Configured);
        FieldToolsCommand = new RelayCommand(static () => { }, () => IsFieldSelected);
        LlmGenerateCommand = new RelayCommand(static () => { }, () => IsFieldSelected);
    }

    public IRelayCommand SaveCommand { get; }
    public IRelayCommand SaveAsCommand { get; }
    public IRelayCommand CloseCommand { get; }
    public IRelayCommand ExportCurrentMapCommand { get; }
    public IRelayCommand ImportCurrentMapCommand { get; }
    public IRelayCommand MassExportCommand { get; }
    public IRelayCommand RunFf7Command { get; }
    public IRelayCommand FieldToolsCommand { get; }
    public IRelayCommand LlmGenerateCommand { get; }

    public FieldListViewModel FieldList { get; }
    public PreviewViewModel Preview { get; }
    public ScriptManagerViewModel ScriptManager { get; }
    public ArchiveManagerViewModel ArchiveManager { get; }
    public StatusBarViewModel StatusBar { get; }

    public bool CanSave => IsArchiveOpen && IsCurrentFieldModified;

    public void SetArchiveMetadata(string? path, ShellArchiveType archiveType)
    {
        ArchivePath = path ?? string.Empty;
        ArchiveType = archiveType;
        ArchiveName = string.IsNullOrWhiteSpace(path)
            ? "No archive open"
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
    }

    public void ClearScriptSelection()
    {
        SelectedGroupIndex = null;
        SelectedGroupName = string.Empty;
        SelectedScriptIndex = null;
        SelectedScriptName = string.Empty;
        SelectedOpcodeOffset = null;
        SelectedOpcodeName = string.Empty;
    }

    public void NavigateToField(string fieldName) =>
        PendingNavigationTarget = new ShellNavigationTarget(
            fieldName,
            null,
            null,
            null,
            null);

    public void NavigateToScript(string fieldName, int groupIndex, int? scriptIndex = null, int? opcodeOffset = null) =>
        PendingNavigationTarget = new ShellNavigationTarget(
            fieldName,
            groupIndex,
            scriptIndex,
            opcodeOffset,
            null);

    public void NavigateToText(string fieldName, int textIndex) =>
        PendingNavigationTarget = new ShellNavigationTarget(
            fieldName,
            null,
            null,
            null,
            textIndex);

    public void NavigateTo(FieldSearchResult result)
    {
        if (result.TextIndex is int textIndex)
        {
            NavigateToText(result.FieldName, textIndex);
            return;
        }

        if (result.GroupIndex is int groupIndex)
        {
            NavigateToScript(result.FieldName, groupIndex, result.ScriptIndex, result.OpcodeOffset);
            return;
        }

        NavigateToField(result.FieldName);
    }

    partial void OnIsArchiveOpenChanged(bool value) => NotifyCommandStateChanged();
    partial void OnIsFieldSelectedChanged(bool value) => NotifyCommandStateChanged();
    partial void OnIsCurrentFieldModifiedChanged(bool value) => NotifyCommandStateChanged();
    partial void OnIsFf7ConfiguredChanged(bool value) => NotifyCommandStateChanged();

    private void NotifyCommandStateChanged()
    {
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        CloseCommand.NotifyCanExecuteChanged();
        ExportCurrentMapCommand.NotifyCanExecuteChanged();
        ImportCurrentMapCommand.NotifyCanExecuteChanged();
        MassExportCommand.NotifyCanExecuteChanged();
        RunFf7Command.NotifyCanExecuteChanged();
        FieldToolsCommand.NotifyCanExecuteChanged();
        LlmGenerateCommand.NotifyCanExecuteChanged();
    }
}

public sealed record ShellNavigationTarget(
    string FieldName,
    int? GroupIndex,
    int? ScriptIndex,
    int? OpcodeOffset,
    int? TextIndex);

public enum ShellArchiveType
{
    None,
    Lgp,
    LoosePcDirectory,
    LoosePlayStationDirectory,
    StandalonePcField,
}

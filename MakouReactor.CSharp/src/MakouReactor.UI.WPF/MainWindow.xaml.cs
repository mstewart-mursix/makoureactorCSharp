using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;
using MakouReactor.Core.Services;
using MakouReactor.UI.WPF.Archive;
using MakouReactor.UI.WPF.Batch;
using MakouReactor.UI.WPF.Background;
using MakouReactor.UI.WPF.Encounters;
using MakouReactor.UI.WPF.Fields;
using MakouReactor.UI.WPF.Models;
using MakouReactor.UI.WPF.Misc;
using MakouReactor.UI.WPF.PlayStation;
using MakouReactor.UI.WPF.Search;
using MakouReactor.UI.WPF.Services;
using MakouReactor.UI.WPF.Settings;
using MakouReactor.UI.WPF.Shell;
using MakouReactor.UI.WPF.Scripts;
using MakouReactor.UI.WPF.Shared;
using MakouReactor.UI.WPF.Texts;
using MakouReactor.UI.WPF.Tutorials;
using MakouReactor.UI.WPF.Variables;
using MakouReactor.UI.WPF.ViewModels;
using MakouReactor.UI.WPF.Walkmesh;

using Microsoft.Win32;

namespace MakouReactor.UI.WPF;

/// <summary>
/// Main Makou Reactor shell. The original Qt application uses this surface for
/// archive browsing, field script editing, previews, and tool dialogs; C# ports
/// those features incrementally while keeping the same application shape.
/// </summary>
public partial class MainWindow : Window
{
    private readonly List<FieldArchiveEntry> _allFields = [];
    private readonly HashSet<string> _unavailableFields = new(StringComparer.OrdinalIgnoreCase);
    private readonly IShellSessionService _shellSession;
    private readonly IRecentFilesService _recentFilesService;
    private readonly IArchiveSaveAsService _archiveSaveAsService;
    private readonly AppSettings _settings;
    private readonly MainWindowViewModel _viewModel = new();
    private FieldArchive? _archive;
    private string? _archivePath;
    private FieldPC? _currentField;
    private FieldPS? _currentPlayStationField;
    private Section1File? _currentSection1;
    private string? _currentFieldName;
    private string? _archiveSortProperty;
    private ListSortDirection _archiveSortDirection = ListSortDirection.Ascending;
    private string? _fieldSortProperty;
    private ListSortDirection _fieldSortDirection = ListSortDirection.Ascending;
    private bool _fieldListVisible = true;
    private bool _previewVisible = true;
    private string _previewMode = "Background";
    private bool _scriptTreeMode;
    private CancellationTokenSource? _busyCancellation;
    private FieldEditSnapshot? _lastLlmApplySnapshot;

    public MainWindow()
        : this(new ShellSessionService())
    {
    }

    public MainWindow(IShellSessionService shellSession)
        : this(shellSession, new ArchiveSaveAsService(new WpfFilePickerService()))
    {
    }

    public MainWindow(IShellSessionService shellSession, IArchiveSaveAsService archiveSaveAsService)
    {
        ArgumentNullException.ThrowIfNull(shellSession);
        ArgumentNullException.ThrowIfNull(archiveSaveAsService);

        InitializeComponent();
        _shellSession = shellSession;
        _archiveSaveAsService = archiveSaveAsService;
        _settings = _shellSession.Settings;
        _recentFilesService = _shellSession.RecentFiles;
        DataContext = _viewModel;
        RegisterShellCommands();
        WireFieldListView();
        WireArchiveManagerView();
        WireScriptManagerView();
        ApplySettings();
        RefreshRecentFilesMenu();
        SetArchiveOpenState(false);
    }

    private void WireFieldListView()
    {
        FieldListView.SelectionChanged += FieldListView_SelectionChanged;
        FieldListView.SearchGotFocus += FieldSearchBox_GotFocus;
        FieldListView.SearchTextChanged += FieldSearchBox_TextChanged;
        FieldListView.ColumnHeaderClicked += FieldColumnHeader_Click;
        FieldListView.FieldContextMenuOpened += FieldContextMenu_Opened;
        FieldListView.OpenSelectedFieldRequested += OpenSelectedFieldContext_Click;
        FieldListView.ImportSelectedFieldRequested += ImportSelectedFieldContext_Click;
        FieldListView.ExportSelectedFieldRequested += ExportSelectedFieldContext_Click;
        FieldListView.CreateFieldRequested += CreateField_Click;
        FieldListView.DeleteSelectedFieldRequested += DeleteSelectedField_Click;
        FieldListView.RenameSelectedFieldRequested += RenameSelectedField_Click;
    }

    private void WireArchiveManagerView()
    {
        ArchiveManagerView.SelectionChanged += ArchiveEntriesListView_SelectionChanged;
        ArchiveManagerView.ColumnHeaderClicked += ArchiveColumnHeader_Click;
        ArchiveManagerView.ExtractCurrentRequested += ExtractCurrentArchiveEntry_Click;
        ArchiveManagerView.ExtractAllRequested += ExtractAllArchiveEntries_Click;
        ArchiveManagerView.ReplaceCurrentRequested += ReplaceCurrentArchiveEntry_Click;
        ArchiveManagerView.AddRequested += AddArchiveEntry_Click;
        ArchiveManagerView.RemoveCurrentRequested += RemoveCurrentArchiveEntry_Click;
        ArchiveManagerView.RenameCurrentRequested += RenameCurrentArchiveEntry_Click;
    }

    private void WireScriptManagerView()
    {
        ScriptManagerView.GroupSelectionChanged += GroupListView_SelectionChanged;
        ScriptManagerView.ScriptSelectionChanged += ScriptListBox_SelectionChanged;
        ScriptManagerView.OpcodeSelectionChanged += OpcodeListView_SelectionChanged;
        ScriptManagerView.OpcodeMouseDoubleClick += OpcodeListView_MouseDoubleClick;
        ScriptManagerView.OpcodeKeyDown += OpcodeListView_KeyDown;
        ScriptManagerView.ExpandTreeRequested += ExpandTree_Click;
        ScriptManagerView.DisableTreeRequested += DisableTree_Click;
    }

    private void RegisterShellCommands()
    {
        Bind(ShellCommands.OpenFile, OpenFile_Click);
        Bind(ShellCommands.OpenDirectory, OpenDirectory_Click);
        Bind(ShellCommands.Save, Save_Click, () => _viewModel.CanSave);
        Bind(ShellCommands.SaveAs, SaveAs_Click, () => _viewModel.IsArchiveOpen);
        Bind(ShellCommands.ExportCurrentMap, ExportCurrentMap_Click, () => _viewModel.IsArchiveOpen && _viewModel.IsFieldSelected);
        Bind(ShellCommands.ExportChunks, ExportChunks_Click, () => _viewModel.IsFieldSelected);
        Bind(ShellCommands.MassExport, MassExport_Click, () => _viewModel.IsArchiveOpen);
        Bind(ShellCommands.ImportCurrentMap, ImportCurrentMap_Click, () => _viewModel.IsArchiveOpen && _viewModel.IsFieldSelected);
        Bind(ShellCommands.RunFf7, RunFf7_Click, () => _viewModel.IsFf7Configured);
        Bind(ShellCommands.CloseArchive, CloseArchive_Click, () => _viewModel.IsArchiveOpen || _viewModel.IsFieldSelected);
        Bind(ShellCommands.Exit, Exit_Click);
        Bind(ShellCommands.OpenTexts, OpenTextManager_Click, () => _viewModel.IsFieldSelected);
        Bind(ShellCommands.OpenModels, OpenModelManager_Click, () => _viewModel.IsFieldSelected || _currentPlayStationField?.ModelLoaderPS != null);
        Bind(ShellCommands.OpenEncounters, OpenEncounterDialog_Click, () => _viewModel.IsFieldSelected);
        Bind(ShellCommands.OpenTutorials, OpenTutorialsDialog_Click, () => _viewModel.IsFieldSelected);
        Bind(ShellCommands.OpenWalkmesh, OpenWalkmeshManager_Click, () => _viewModel.IsFieldSelected);
        Bind(ShellCommands.OpenBackground, OpenBackgroundDialog_Click, () => _viewModel.IsFieldSelected);
        Bind(ShellCommands.OpenMiscellaneous, OpenMiscellaneousDialog_Click, () => _viewModel.IsFieldSelected);
        Bind(ShellCommands.OpenPlayStationSections, OpenPlayStationSections_Click, () => _currentPlayStationField != null);
        Bind(ShellCommands.OpenVariableManager, OpenVariableManager_Click);
        Bind(ShellCommands.OpenFind, OpenSearchDialog_Click);
        Bind(ShellCommands.OpenBatchProcessing, OpenBatchProcessingDialog_Click, () => _viewModel.IsArchiveOpen);
        Bind(ShellCommands.OpenLlmGenerator, OpenLlmGenerator_Click, () => _viewModel.IsFieldSelected);
        Bind(ShellCommands.RevertLlmApply, RevertLlmApply_Click, () => _lastLlmApplySnapshot != null);
        Bind(ShellCommands.ToggleJapaneseCharacters, JapaneseText_Click);
        Bind(ShellCommands.OpenConfiguration, OpenSettingsDialog_Click);
        Bind(ShellCommands.ToggleFieldList, ToggleFieldList_Click);
        Bind(ShellCommands.TogglePreview, TogglePreview_Click);
        Bind(ShellCommands.SetBackgroundPreviewMode, SetBackgroundPreviewMode_Click);
        Bind(ShellCommands.SetModelPreviewMode, SetModelPreviewMode_Click);
        Bind(ShellCommands.About, About_Click);
    }

    private void Bind(ICommand command, ExecutedRoutedEventHandler executed, Func<bool>? canExecute = null)
    {
        CommandBindings.Add(new CommandBinding(
            command,
            executed,
            (_, e) =>
            {
                e.CanExecute = canExecute?.Invoke() ?? true;
                e.Handled = true;
            }));
    }

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptSaveCurrentFieldIfNeeded())
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Open Final Fantasy VII field file or archive",
            Filter = FileDialogFilters.OpenFieldArchiveOrFile,
        };

        if (dialog.ShowDialog(this) != true)
            return;

        await OpenPathAsync(dialog.FileName);
    }

    private async void OpenDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptSaveCurrentFieldIfNeeded())
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder containing the Final Fantasy VII field files",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var result = MessageBox.Show(this,
            "What type of field files should Makou Reactor look for?\n\nYes: PC field files (example)\nNo: PlayStation field files (EXAMPLE.DAT)",
            "File Type",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel)
            return;
        if (result == MessageBoxResult.No)
        {
            await OpenPlayStationDirectoryAsync(dialog.FolderName);
            return;
        }

        await OpenPcDirectoryAsync(dialog.FolderName);
    }

    private Task OpenPathAsync(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".lgp", StringComparison.OrdinalIgnoreCase))
            return OpenArchiveAsync(path);

        if (extension.Equals(".iso", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".img", StringComparison.OrdinalIgnoreCase))
            return OpenPlayStationImageAsync(path);

        return OpenStandaloneFieldAsync(path);
    }

    private async Task OpenPcDirectoryAsync(string path)
    {
        try
        {
            using var cancellation = new CancellationTokenSource();
            SetBusy(true, $"Opening {path}...", cancellation);
            var archive = await Task.Run(() => FieldArchive.OpenPcDirectory(path, cancellation.Token), cancellation.Token);
            _archive = archive;
            _archivePath = path;
            ClearLlmApplySnapshot();
            _viewModel.SetArchiveMetadata(path, ShellArchiveType.LoosePcDirectory);
            _recentFilesService.Add(path);
            RefreshRecentFilesMenu();

            _allFields.Clear();
            _allFields.AddRange(_archive.FieldEntries);
            _unavailableFields.Clear();

            ArchiveManagerView.ItemsSource = _archive.ArchiveEntries;
            ArchiveManagerView.ExtractAllEnabled = _archive.ArchiveEntries.Count > 0;
            ScriptManagerView.GroupsSource = Array.Empty<GroupListItem>();
            ScriptManagerView.ScriptsSource = Array.Empty<string>();
            ScriptManagerView.OpcodesSource = Array.Empty<OpcodeListItem>();
            ScriptManagerView.OpcodeTreeSource = Array.Empty<ScriptTreeItem>();
            SetOpcodeToolbarState(false);
            _currentFieldName = null;
            _currentField = null;
            _currentPlayStationField = null;
            _currentSection1 = null;
            var selectedFieldName = LastSelectedFieldForArchive(path);
            RefreshFieldListItems(selectedFieldName);

            ArchiveLabel.Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            ShowScriptPlaceholder(_allFields.Count == 0
                ? "Directory opened. No PC field files were detected."
                : "Select a field to inspect scripts.");
            ShowArchiveTextPreview($"{_archive.ArchiveEntries.Count} loose file entries loaded.");
            UpdatePreviewLabel();

            SetArchiveOpenState(true);
            SaveAsMenuItem.IsEnabled = false;
            StatusLabel.Text = $"Opened PC field directory {path} - {_allFields.Count} fields, {_archive.ArchiveEntries.Count} files.";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Open Directory cancelled.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Open Directory", ex, path);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task OpenPlayStationDirectoryAsync(string path)
    {
        try
        {
            using var cancellation = new CancellationTokenSource();
            SetBusy(true, $"Opening {path}...", cancellation);
            var archive = await Task.Run(() => FieldArchive.OpenPlayStationDirectory(path, cancellation.Token), cancellation.Token);
            _archive = archive;
            _archivePath = path;
            ClearLlmApplySnapshot();
            _viewModel.SetArchiveMetadata(path, ShellArchiveType.LoosePlayStationDirectory);
            _recentFilesService.Add(path);
            RefreshRecentFilesMenu();

            _allFields.Clear();
            _allFields.AddRange(_archive.FieldEntries);
            _unavailableFields.Clear();

            ArchiveManagerView.ItemsSource = _archive.ArchiveEntries;
            ArchiveManagerView.ExtractAllEnabled = _archive.ArchiveEntries.Count > 0;
            ScriptManagerView.GroupsSource = Array.Empty<GroupListItem>();
            ScriptManagerView.ScriptsSource = Array.Empty<string>();
            ScriptManagerView.OpcodesSource = Array.Empty<OpcodeListItem>();
            ScriptManagerView.OpcodeTreeSource = Array.Empty<ScriptTreeItem>();
            SetOpcodeToolbarState(false);
            _currentFieldName = null;
            _currentField = null;
            _currentPlayStationField = null;
            _currentSection1 = null;
            var selectedFieldName = LastSelectedFieldForArchive(path);
            RefreshFieldListItems(selectedFieldName);

            ArchiveLabel.Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            ShowScriptPlaceholder(_allFields.Count == 0
                ? "Directory opened. No PlayStation field DAT files were detected."
                : "Select a PlayStation field to inspect DAT sections, model-loader records, and raw archive data.");
            ShowArchiveTextPreview($"{_archive.ArchiveEntries.Count} loose PlayStation file entries loaded.");
            UpdatePreviewLabel();

            SetArchiveOpenState(true);
            SaveAsMenuItem.IsEnabled = false;
            StatusLabel.Text =
                $"Opened PlayStation field directory {path} - {_allFields.Count} fields, {_archive.ArchiveEntries.Count} files.";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Open Directory cancelled.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Open Directory", ex, path);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task OpenPlayStationImageAsync(string path)
    {
        try
        {
            using var cancellation = new CancellationTokenSource();
            SetBusy(true, $"Opening {path}...", cancellation);
            var archive = await Task.Run(() => FieldArchive.OpenPlayStationImage(path, cancellation.Token), cancellation.Token);
            _archive = archive;
            _archivePath = path;
            ClearLlmApplySnapshot();
            _viewModel.SetArchiveMetadata(path, ShellArchiveType.LoosePlayStationDirectory);
            _recentFilesService.Add(path);
            RefreshRecentFilesMenu();

            _allFields.Clear();
            _allFields.AddRange(_archive.FieldEntries);
            _unavailableFields.Clear();

            ArchiveManagerView.ItemsSource = _archive.ArchiveEntries;
            ArchiveManagerView.ExtractAllEnabled = _archive.ArchiveEntries.Count > 0;
            ArchiveManagerView.ReplaceCurrentEnabled = false;
            ArchiveManagerView.RemoveCurrentEnabled = false;
            ArchiveManagerView.RenameCurrentEnabled = false;
            ArchiveManagerView.AddEnabled = false;
            ScriptManagerView.GroupsSource = Array.Empty<GroupListItem>();
            ScriptManagerView.ScriptsSource = Array.Empty<string>();
            ScriptManagerView.OpcodesSource = Array.Empty<OpcodeListItem>();
            ScriptManagerView.OpcodeTreeSource = Array.Empty<ScriptTreeItem>();
            SetOpcodeToolbarState(false);
            _currentFieldName = null;
            _currentField = null;
            _currentPlayStationField = null;
            _currentSection1 = null;
            RefreshFieldListItems(LastSelectedFieldForArchive(path));

            ArchiveLabel.Text = Path.GetFileName(path);
            ShowScriptPlaceholder(_allFields.Count == 0
                ? "Disc image opened. No PlayStation field DAT files were detected."
                : "Select a PlayStation field to inspect DAT sections, model-loader records, and raw archive data.");
            ShowArchiveTextPreview($"{_archive.ArchiveEntries.Count} PlayStation disc image file entries loaded.");
            UpdatePreviewLabel();

            SetArchiveOpenState(true);
            SaveMenuItem.IsEnabled = false;
            SaveToolButton.IsEnabled = false;
            SaveAsMenuItem.IsEnabled = false;
            MassExportMenuItem.IsEnabled = false;
            ImportCurrentMapMenuItem.IsEnabled = false;
            ExportCurrentMapMenuItem.IsEnabled = false;
            BatchMenuItem.IsEnabled = false;
            ArchiveManagerView.AddEnabled = false;
            ArchiveManagerView.ReplaceCurrentEnabled = false;
            ArchiveManagerView.RemoveCurrentEnabled = false;
            ArchiveManagerView.RenameCurrentEnabled = false;
            StatusLabel.Text =
                $"Opened PlayStation disc image {path} - {_allFields.Count} fields, {_archive.ArchiveEntries.Count} files.";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Open cancelled.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Open", ex, path);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task OpenArchiveAsync(string path)
    {
        try
        {
            using var cancellation = new CancellationTokenSource();
            SetBusy(true, $"Opening {path}...", cancellation);
            var archive = await Task.Run(() => FieldArchive.Open(path, cancellation.Token), cancellation.Token);
            _archive = archive;
            _archivePath = path;
            ClearLlmApplySnapshot();
            _viewModel.SetArchiveMetadata(path, ShellArchiveType.Lgp);
            _recentFilesService.Add(path);
            RefreshRecentFilesMenu();

            _allFields.Clear();
            _allFields.AddRange(_archive.FieldEntries);
            _unavailableFields.Clear();

            ArchiveManagerView.ItemsSource = _archive.ArchiveEntries;
            ArchiveManagerView.ExtractAllEnabled = _archive.ArchiveEntries.Count > 0;
            ScriptManagerView.GroupsSource = Array.Empty<GroupListItem>();
            ScriptManagerView.ScriptsSource = Array.Empty<string>();
            ScriptManagerView.OpcodesSource = Array.Empty<OpcodeListItem>();
            ScriptManagerView.OpcodeTreeSource = Array.Empty<ScriptTreeItem>();
            SetOpcodeToolbarState(false);
            _currentFieldName = null;
            _currentField = null;
            _currentPlayStationField = null;
            _currentSection1 = null;
            var selectedFieldName = LastSelectedFieldForArchive(path);
            RefreshFieldListItems(selectedFieldName);

            ArchiveLabel.Text = Path.GetFileName(path);
            ShowScriptPlaceholder(_allFields.Count == 0
                ? "Archive opened. No field entries were detected."
                : "Select a field to inspect scripts.");
            ShowArchiveTextPreview($"{_archive.ArchiveEntries.Count} archive entries loaded.");
            UpdatePreviewLabel();

            SetArchiveOpenState(true);
            if (!string.IsNullOrWhiteSpace(selectedFieldName) && FieldListView.SelectedItem == null)
                StatusLabel.Text = $"Opened {Path.GetFileName(path)} - previous field '{selectedFieldName}' was not found.";
            else
                StatusLabel.Text = $"Opened {Path.GetFileName(path)} - {_allFields.Count} fields, {_archive.ArchiveEntries.Count} archive entries.";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Open cancelled.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Open", ex, path);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task OpenStandaloneFieldAsync(string path)
    {
        try
        {
            using var cancellation = new CancellationTokenSource();
            SetBusy(true, $"Opening {path}...", cancellation);
            var fieldName = Path.GetFileNameWithoutExtension(path);
            var result = await Task.Run(() =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var data = File.ReadAllBytes(path);
                cancellation.Token.ThrowIfCancellationRequested();
                var field = StandaloneFieldLoader.Open(fieldName, path, data);
                return new StandaloneOpenResult(data, field);
            }, cancellation.Token);

            _archive = null;
            _archivePath = path;
            ClearLlmApplySnapshot();
            _viewModel.SetArchiveMetadata(path, ShellArchiveType.StandalonePcField);
            _recentFilesService.Add(path);
            RefreshRecentFilesMenu();
            _allFields.Clear();
            _allFields.Add(new FieldArchiveEntry(0, result.Field.Name, result.Data.Length));
            _unavailableFields.Clear();
            RefreshFieldListItems();
            FieldListView.SelectedIndex = -1;
            ArchiveManagerView.ItemsSource = null;
            ArchiveManagerView.ExtractCurrentEnabled = false;
            ArchiveManagerView.ReplaceCurrentEnabled = false;
            ArchiveManagerView.RemoveCurrentEnabled = false;
            ArchiveManagerView.RenameCurrentEnabled = false;
            ArchiveManagerView.ExtractAllEnabled = false;
            ShowArchiveTextPreview("Standalone field file opened.");
            ArchiveLabel.Text = Path.GetFileName(path);

            DisplayField(result.Field);
            SetArchiveOpenState(false);
            SetFieldSelectedState(true);
            SaveAsMenuItem.IsEnabled = false;
            MassExportMenuItem.IsEnabled = false;
            ImportCurrentMapMenuItem.IsEnabled = false;
            ExportCurrentMapMenuItem.IsEnabled = false;
            StatusLabel.Text = $"Opened standalone field {result.Field.Name} - {result.Field.Data.Length:N0} decompressed bytes.";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Open cancelled.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Open", ex, path);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OpenRecentFile_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptSaveCurrentFieldIfNeeded())
            return;

        if (sender is not MenuItem { Tag: string path })
            return;

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            _recentFilesService.Remove(path);
            RefreshRecentFilesMenu();
            MessageBox.Show(this,
                $"The recent file or directory no longer exists:\n{path}",
                "Recent file unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (Directory.Exists(path))
            await OpenPcDirectoryAsync(path);
        else
            await OpenPathAsync(path);
    }

    private void ClearRecentFiles_Click(object sender, RoutedEventArgs e)
    {
        _recentFilesService.Clear();
        RefreshRecentFilesMenu();
    }

    private void CloseArchive_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptSaveCurrentFieldIfNeeded())
            return;

        _archive = null;
        _archivePath = null;
        ClearLlmApplySnapshot();
        _viewModel.SetArchiveMetadata(null, ShellArchiveType.None);
        _currentFieldName = null;
        _currentField = null;
        _currentPlayStationField = null;
        _viewModel.CurrentField = null;
        _viewModel.ScriptManager.SetDirtyState(false);
        _allFields.Clear();
        _unavailableFields.Clear();
        FieldListView.ItemsSource = null;
        ArchiveManagerView.ItemsSource = null;
        ArchiveManagerView.ExtractCurrentEnabled = false;
        ArchiveManagerView.ReplaceCurrentEnabled = false;
        ArchiveManagerView.RemoveCurrentEnabled = false;
        ArchiveManagerView.RenameCurrentEnabled = false;
        ArchiveManagerView.ExtractAllEnabled = false;
        ScriptManagerView.GroupsSource = null;
        ScriptManagerView.ScriptsSource = null;
        ScriptManagerView.OpcodesSource = null;
        ScriptManagerView.OpcodeTreeSource = null;
        SetOpcodeToolbarState(false);
        _currentSection1 = null;
        ArchiveLabel.Text = "No archive open";
        _viewModel.ArchiveName = ArchiveLabel.Text;
        _viewModel.CurrentFieldName = string.Empty;
        ShowScriptPlaceholder("Open an archive and select a field to inspect scripts.");
        ShowArchiveTextPreview("Archive preview will appear here.");
        UpdatePreviewLabel();
        SetArchiveOpenState(false);
        StatusLabel.Text = "Closed archive.";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_currentField == null && _currentPlayStationField == null)
        {
            MessageBox.Show(this,
                "Select a field before saving.",
                "Save",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var fieldName = _currentField?.Name ?? _currentPlayStationField!.Name;
        try
        {
            using var cancellation = new CancellationTokenSource();
            SetBusy(true, $"Saving {fieldName}...", cancellation);
            if (_currentField != null)
            {
                var archive = _archive;
                var standalonePath = _archivePath;
                await Task.Run(() =>
                {
                    if (archive != null)
                        archive.SaveField(_currentField, cancellation.Token);
                    else if (!string.IsNullOrWhiteSpace(standalonePath))
                        StandaloneFieldLoader.Save(standalonePath, _currentField);
                    else
                        throw new InvalidOperationException("No save target is available for the current field.");
                    cancellation.Token.ThrowIfCancellationRequested();
                }, cancellation.Token);
            }
            else
            {
                if (_archive == null)
                    throw new InvalidOperationException("No archive is available for the current PlayStation field.");
                await Task.Run(() => _archive.SaveField(_currentPlayStationField!, cancellation.Token), cancellation.Token);
            }
            if (_archive != null)
                RefreshArchiveViews();
            UpdateDirtyState();
            StatusLabel.Text = _archive == null || _archive.IsLooseDirectory
                ? $"Saved {fieldName}."
                : $"Saved {fieldName}. Backup written beside the archive.";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Save cancelled.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Save", ex, fieldName);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (_archive == null || string.IsNullOrWhiteSpace(_archivePath))
            return;

        var destinationPath = _archiveSaveAsService.PickDestination(this, _archivePath);
        if (destinationPath == null)
            return;

        try
        {
            StatusLabel.Text = $"Saving archive as {destinationPath}...";
            _archiveSaveAsService.CopyArchive(_archivePath, destinationPath);
            await OpenArchiveAsync(destinationPath);
            StatusLabel.Text = $"Saved archive as {destinationPath}.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Save As", ex, destinationPath);
        }
    }

    private void ExportCurrentMap_Click(object sender, RoutedEventArgs e)
    {
        if (_archive == null || _currentFieldName == null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export current map",
            FileName = _currentFieldName,
            Filter = FileDialogFilters.ExportPcField,
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllBytes(dialog.FileName, _archive.ReadRawFile(_currentFieldName));
            StatusLabel.Text = $"Exported {_currentFieldName} to {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Export", ex, dialog.FileName);
        }
    }

    private void ExportChunks_Click(object sender, RoutedEventArgs e)
    {
        if (_currentField == null)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Choose a directory where to create chunks",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            Directory.CreateDirectory(dialog.FolderName);
            foreach (var section in _currentField.Sections)
            {
                var fileName = $"{_currentField.Name}.chunk.{section.Index}";
                File.WriteAllBytes(
                    Path.Combine(dialog.FolderName, fileName),
                    _currentField.GetSectionData(section.Section));
            }

            StatusLabel.Text = $"Exported {_currentField.Sections.Count} chunk(s) for {_currentField.Name}.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Export chunks", ex, dialog.FolderName);
        }
    }

    private void ImportCurrentMap_Click(object sender, RoutedEventArgs e)
    {
        if (_archive == null || _currentFieldName == null)
            return;

        var dialog = new OpenFileDialog
        {
            Title = $"Import to {_currentFieldName}",
            Filter = FileDialogFilters.ImportField,
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            StatusLabel.Text = $"Importing {dialog.FileName} to {_currentFieldName}...";
            _archive.ReplaceRawFile(_currentFieldName, File.ReadAllBytes(dialog.FileName));
            RefreshArchiveViews(_currentFieldName);
            StatusLabel.Text = $"Imported {dialog.FileName} to {_currentFieldName}. Backup written beside the archive.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Import", ex, dialog.FileName);
        }
    }

    private async void MassExport_Click(object sender, RoutedEventArgs e)
    {
        if (_archive == null)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Choose mass export directory",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            using var cancellation = new CancellationTokenSource();
            SetBusy(true, "Mass exporting archive entries...", cancellation);
            var count = await Task.Run(() => _archive.ExtractAll(dialog.FolderName, cancellation.Token), cancellation.Token);
            StatusLabel.Text = $"Mass exported {count} archive entr{(count == 1 ? "y" : "ies")} to {dialog.FolderName}.";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Mass Export cancelled.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Mass Export", ex, dialog.FolderName);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void FieldListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FieldListView.SelectedItem is not ShellFieldListItem item)
        {
            UpdateFieldListActionState();
            SetFieldSelectedState(false);
            _currentFieldName = null;
            _currentField = null;
            _currentPlayStationField = null;
            _currentSection1 = null;
            _viewModel.ScriptManager.SetDirtyState(false);
            ScriptManagerView.GroupsSource = Array.Empty<GroupListItem>();
            ScriptManagerView.ScriptsSource = Array.Empty<SectionListItem>();
            ScriptManagerView.OpcodesSource = Array.Empty<OpcodeListItem>();
            ScriptManagerView.OpcodeTreeSource = Array.Empty<ScriptTreeItem>();
            SetOpcodeToolbarState(false);
            ShowScriptPlaceholder("Select a field to inspect scripts.");
            return;
        }

        UpdateFieldListActionState();
        var field = item.Entry;
        SetFieldSelectedState(true);
        _currentFieldName = field.Name;
        RememberSelectedField(field.Name);
        if (_archive == null)
            return;

        try
        {
            if (_archive.Platform == FieldArchivePlatform.PlayStation)
            {
                var openedField = _archive.OpenPlayStationField(field.Name);
                DisplayField(openedField);
                StatusLabel.Text = $"Loaded PlayStation field {openedField.Name} - {openedField.Data.Length:N0} decompressed bytes.";
            }
            else
            {
                var openedField = _archive.OpenField(field.Name);
                DisplayField(openedField);
                StatusLabel.Text = $"Loaded field {openedField.Name} - {openedField.Data.Length:N0} decompressed bytes.";
            }
        }
        catch (Exception ex)
        {
            ScriptManagerView.GroupsSource = Array.Empty<GroupListItem>();
            ScriptManagerView.ScriptsSource = Array.Empty<SectionListItem>();
            ScriptManagerView.OpcodesSource = Array.Empty<OpcodeListItem>();
            ScriptManagerView.OpcodeTreeSource = Array.Empty<ScriptTreeItem>();
            SetOpcodeToolbarState(false);
            _currentField = null;
            _currentPlayStationField = null;
            _currentSection1 = null;
            _viewModel.ScriptManager.SetDirtyState(false);
            _unavailableFields.Add(field.Name);
            RefreshFieldListItems(field.Name);
            ShowScriptPlaceholder($"Unable to load field '{field.Name}'.\n\n{ex.Message}");
            PreviewPanel.ShowLoadFailed(field.Name);
            StatusLabel.Text = $"Field load failed: {ex.Message}";
        }
    }

    private void DisplayField(FieldPC openedField)
    {
        _currentField = openedField;
        _currentPlayStationField = null;
        _currentFieldName = openedField.Name;
        _viewModel.CurrentField = openedField;

        if (openedField.ScriptsAndTexts != null)
        {
            openedField.ScriptsAndTexts.SetJapaneseText(_settings.JapaneseText);
            ScriptManagerView.GroupsSource = openedField.ScriptsAndTexts.GrpScripts
                .Select((group, index) => new GroupListItem(index, group.Name, group.TypeString))
                .ToArray();
            _currentSection1 = openedField.ScriptsAndTexts;
            ScriptManagerView.SelectedGroupIndex = openedField.ScriptsAndTexts.GrpScripts.Count > 0 ? 0 : -1;
            UpdateScriptListForSelectedGroup();
            ShowScriptPlaceholder(
                $"Field '{openedField.Name}' loaded.\n\n" +
                $"Author: {openedField.ScriptsAndTexts.Author}\n" +
                $"Map name: {openedField.ScriptsAndTexts.MapName}\n" +
                $"Scale: {openedField.ScriptsAndTexts.Scale}\n" +
                $"Groups: {openedField.ScriptsAndTexts.GrpScriptCount}\n" +
                $"Texts: {openedField.ScriptsAndTexts.TextCount}\n\n" +
                "Select a script to inspect raw opcodes.");
        }
        else
        {
            _currentSection1 = null;
            ScriptManagerView.GroupsSource = openedField.Sections
                .Select(section => new GroupListItem(section.Index - 1, section.Section.ToString(), $"#{section.Index}"))
                .ToArray();
            ScriptManagerView.ScriptsSource = openedField.Sections
                .Select(section => new SectionListItem(section.Section.ToString(), section.Size, null))
                .ToArray();
            ScriptManagerView.OpcodesSource = Array.Empty<OpcodeListItem>();
            ScriptManagerView.OpcodeTreeSource = Array.Empty<ScriptTreeItem>();
            SetOpcodeToolbarState(false);
            ShowScriptPlaceholder(
                $"Field '{openedField.Name}' loaded.\n\n" +
                $"Decompressed size: {openedField.Data.Length:N0} bytes\n" +
                $"Sections: {openedField.Sections.Count}\n\n" +
                "Section 1 could not be parsed for this field.");
        }

        UpdatePreviewLabel();
        UpdateDirtyState();
    }

    private void DisplayField(FieldPS openedField)
    {
        _currentField = null;
        _currentPlayStationField = openedField;
        _currentSection1 = null;
        _currentFieldName = openedField.Name;
        _viewModel.CurrentField = null;
        _viewModel.CurrentFieldName = openedField.Name;
        _viewModel.IsFieldSelected = false;
        _viewModel.ScriptManager.SetDirtyState(false);

        ScriptManagerView.GroupsSource = openedField.Sections
            .Select(section => new GroupListItem(section.Index - 1, section.Section.ToString(), $"PS #{section.Index}"))
            .ToArray();
        ScriptManagerView.ScriptsSource = openedField.Sections
            .Select(section => new SectionListItem(section.Section.ToString(), section.Size, null))
            .ToArray();
        ScriptManagerView.OpcodesSource = Array.Empty<OpcodeListItem>();
        ScriptManagerView.OpcodeTreeSource = Array.Empty<ScriptTreeItem>();
        SetOpcodeToolbarState(false);

        var modelSummary = openedField.ModelLoaderPS == null
            ? "Model loader: unavailable"
            : $"Model loader: {openedField.ModelLoaderPS.ModelCount} model(s)";
        ApplyShellCommandState(
            ShellCommandStatePolicy.ForPlayStationField(
                _archive != null,
                _archive?.IsReadOnlyImage != true,
                !string.IsNullOrWhiteSpace(_settings.Ff7ExecutablePath),
                openedField.ModelLoaderPS != null,
                _lastLlmApplySnapshot != null));
        ShowScriptPlaceholder(
            $"PlayStation field '{openedField.Name}' loaded.\n\n" +
            $"Decompressed size: {openedField.Data.Length:N0} bytes\n" +
            $"Sections: {openedField.Sections.Count}\n" +
            $"{modelSummary}\n\n" +
            "Use PlayStation Sections for raw DAT section replacement or Model Manager for parsed model-loader edits.");
        PreviewPanel.ShowPlayStationField(openedField);
        UpdateDirtyState();
        CommandManager.InvalidateRequerySuggested();
    }

    private void FieldSearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (FieldListView.SearchText == "Search...")
            FieldListView.SearchText = string.Empty;
    }

    private void FieldSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_allFields.Count == 0 || FieldListView.SearchText == "Search...")
            return;

        RefreshFieldListItems(_currentFieldName);
    }

    private void FieldContextMenu_Opened(object? sender, EventArgs e)
    {
        UpdateFieldListActionState();
    }

    private void UpdateFieldListActionState()
    {
        var hasField = FieldListView.SelectedItem is ShellFieldListItem;
        var canMutatePcField = hasField &&
                               _archive?.Platform == FieldArchivePlatform.PC &&
                               _archive.IsReadOnlyImage != true;
        FieldListView.OpenContextActionEnabled = hasField;
        FieldListView.ExportContextActionEnabled = hasField && _archive != null;
        FieldListView.ImportContextActionEnabled = canMutatePcField;
        FieldListView.CreateFieldActionEnabled = _archive?.Platform == FieldArchivePlatform.PC &&
                                                _archive.IsReadOnlyImage != true;
        FieldListView.DeleteFieldActionEnabled = canMutatePcField;
        FieldListView.RenameFieldActionEnabled = canMutatePcField;
    }

    private void OpenSelectedFieldContext_Click(object? sender, EventArgs e)
    {
        if (FieldListView.SelectedItem is ShellFieldListItem field)
            FieldListView.ScrollIntoView(field);
    }

    private void ExportSelectedFieldContext_Click(object? sender, EventArgs e)
    {
        ExportCurrentMap_Click(this, new RoutedEventArgs());
    }

    private void ImportSelectedFieldContext_Click(object? sender, EventArgs e)
    {
        ImportCurrentMap_Click(this, new RoutedEventArgs());
    }

    private void CreateField_Click(object? sender, EventArgs e)
    {
        if (_archive?.Platform != FieldArchivePlatform.PC)
            return;

        var dialog = new FieldNameDialog("Create field", string.Empty)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        var newName = dialog.FieldName.Trim();
        if (newName.Length > 20)
            newName = newName[..20];
        if (!ValidateFieldName(newName, currentName: string.Empty))
            return;

        try
        {
            var field = _archive.CreatePcField(newName);
            _currentFieldName = field.Name;
            _currentField = field;
            _currentPlayStationField = null;
            RefreshArchiveViews(field.Name);
            DisplayField(field);
            StatusLabel.Text = $"Created field {field.Name}.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Create field", ex, newName);
        }
    }

    private void DeleteSelectedField_Click(object? sender, EventArgs e)
    {
        if (_archive?.Platform != FieldArchivePlatform.PC ||
            FieldListView.SelectedItem is not ShellFieldListItem field)
        {
            return;
        }

        if (MessageBox.Show(this,
                $"Are you sure you want to remove '{field.Name}'?\nOther maps can refer to it.",
                "Delete field",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (_currentFieldName != null &&
                _currentFieldName.Equals(field.Name, StringComparison.OrdinalIgnoreCase) &&
                !PromptSaveCurrentFieldIfNeeded())
            {
                return;
            }

            _archive.RemoveRawFile(field.Name);
            if (_currentFieldName != null &&
                _currentFieldName.Equals(field.Name, StringComparison.OrdinalIgnoreCase))
            {
                _currentFieldName = null;
                _currentField = null;
                _currentPlayStationField = null;
                _currentSection1 = null;
            }

            RefreshArchiveViews(preserveArchiveSelection: false);
            FieldListView.SelectedIndex = -1;
            SetFieldSelectedState(false);
            StatusLabel.Text = $"Deleted field {field.Name}.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Delete field", ex, field.Name);
        }
    }

    private void RenameSelectedField_Click(object? sender, EventArgs e)
    {
        if (_archive?.Platform != FieldArchivePlatform.PC ||
            FieldListView.SelectedItem is not ShellFieldListItem field)
        {
            return;
        }

        var dialog = new FieldNameDialog("Rename field", field.Name)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        var newName = dialog.FieldName.Trim();
        if (newName.Length > 20)
            newName = newName[..20];
        if (!ValidateFieldName(newName, field.Name))
            return;

        try
        {
            _archive.RenameRawFile(field.Name, newName);
            if (_currentFieldName != null &&
                _currentFieldName.Equals(field.Name, StringComparison.OrdinalIgnoreCase))
            {
                _currentFieldName = newName;
                if (_currentField != null)
                    _currentField = _archive.OpenField(newName);
            }

            RefreshArchiveViews(newName);
            StatusLabel.Text = $"Renamed field {field.Name} to {newName}.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Rename field", ex, field.Name);
        }
    }

    private bool ValidateFieldName(string newName, string currentName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            MessageBox.Show(this,
                "Please set a new field name.",
                "Name not filled",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            newName.Contains('/') ||
            newName.Contains('\\'))
        {
            MessageBox.Show(this,
                "Field names cannot contain path separators or invalid file name characters.",
                "Invalid field name",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (newName.Equals(currentName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (_allFields.Any(field => field.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this,
                "Please choose another name.",
                "Name already present in archive",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private void ScriptListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScriptManagerView.SelectedScript is SectionListItem script)
        {
            StatusLabel.Text = $"Selected {script.Name} - {script.Size} byte(s).";
            if (script.Script == null)
            {
                ScriptManagerView.OpcodesSource = Array.Empty<OpcodeListItem>();
                ScriptManagerView.OpcodeTreeSource = Array.Empty<ScriptTreeItem>();
                SetOpcodeToolbarState(false);
                _viewModel.ClearScriptSelection();
                ShowScriptPlaceholder("This item is section metadata, not a parsed script.");
                return;
            }

        _viewModel.SelectedScriptIndex = ScriptManagerView.SelectedScriptIndex >= 0 ? ScriptManagerView.SelectedScriptIndex : null;
        _viewModel.SelectedScriptName = script.Name;
        _viewModel.SelectedOpcodeOffset = null;
        _viewModel.SelectedOpcodeName = string.Empty;
        var opcodes = script.Script.RawOpcodes
            .Select(static opcode => new OpcodeListItem(opcode))
            .ToArray();
            ScriptManagerView.OpcodesSource = opcodes;
            ScriptManagerView.OpcodeTreeSource = ScriptOpcodeTreeBuilder.Build(
                script.Name,
                opcodes,
                static opcode => opcode.OffsetHex,
                static opcode => opcode.Name,
                static opcode => opcode.Arguments);
            SetOpcodeToolbarState(opcodes.Length > 0);
            if (opcodes.Length == 0)
                ShowScriptPlaceholder("This script is empty.");
            else
                HideScriptPlaceholder();
        }
    }

    private void GroupListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateScriptListForSelectedGroup();
    }

    private void OpcodeListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScriptManagerView.SelectedOpcode is OpcodeListItem opcode)
        {
            _viewModel.SelectedOpcodeOffset = opcode.Opcode.Offset;
            _viewModel.SelectedOpcodeName = opcode.Name;
            return;
        }

        _viewModel.SelectedOpcodeOffset = null;
        _viewModel.SelectedOpcodeName = string.Empty;
    }

    private void OpcodeListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedRawOpcodeDialog();
    }

    private void OpcodeListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        OpenSelectedRawOpcodeDialog();
    }

    private void OpenSelectedRawOpcodeDialog()
    {
        if (ScriptManagerView.SelectedOpcode is not OpcodeListItem item)
            return;

        var dialog = new RawOpcodeDialog(item.Opcode)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        if (ScriptManagerView.SelectedScript is not SectionListItem { Script: { } script })
            return;

        if (!script.ReplaceRawOpcodeBytes(item.Opcode.Offset, dialog.EditedBytes))
        {
            StatusLabel.Text = "Raw opcode edit was not applied.";
            return;
        }

        var selectedScriptIndex = ScriptManagerView.SelectedScriptIndex;
        _currentSection1?.MarkModified();
        _currentField?.SetModified();
        UpdateScriptListForSelectedGroup();
        if (selectedScriptIndex >= 0)
            ScriptManagerView.SelectedScriptIndex = selectedScriptIndex;
        SelectOpcodeOffset(item.Opcode.Offset);
        UpdateDirtyState();
        StatusLabel.Text = $"Edited opcode at {item.OffsetHex}. Use Save to write the archive.";
    }

    private void UpdateScriptListForSelectedGroup()
    {
        if (_currentSection1 == null || ScriptManagerView.SelectedGroup is not GroupListItem group)
        {
            ScriptManagerView.OpcodesSource = Array.Empty<OpcodeListItem>();
            ScriptManagerView.OpcodeTreeSource = Array.Empty<ScriptTreeItem>();
            SetOpcodeToolbarState(false);
            _viewModel.ClearScriptSelection();
            ShowScriptPlaceholder("Select a field with parsed section 1 scripts.");
            return;
        }

        if (group.Index < 0 || group.Index >= _currentSection1.GrpScripts.Count)
            return;

        _viewModel.SelectedGroupIndex = group.Index;
        _viewModel.SelectedGroupName = group.Name;
        _viewModel.SelectedScriptIndex = null;
        _viewModel.SelectedScriptName = string.Empty;
        _viewModel.SelectedOpcodeOffset = null;
        _viewModel.SelectedOpcodeName = string.Empty;
        ScriptManagerView.ScriptsSource = _currentSection1.GrpScripts[group.Index].Scripts
            .Select((script, index) => new SectionListItem(ScriptManagerViewModel.ScriptName(index), script.Size, script))
            .ToArray();
        ScriptManagerView.OpcodesSource = Array.Empty<OpcodeListItem>();
        ScriptManagerView.OpcodeTreeSource = Array.Empty<ScriptTreeItem>();
        SetOpcodeToolbarState(false);
        ShowScriptPlaceholder("Select a script to inspect raw opcodes.");
    }

    private void ExpandTree_Click(object sender, RoutedEventArgs e)
    {
        _scriptTreeMode = true;
        UpdateScriptViewModeState();
        StatusLabel.Text = "Tree mode selected.";
    }

    private void DisableTree_Click(object sender, RoutedEventArgs e)
    {
        _scriptTreeMode = false;
        UpdateScriptViewModeState();
        StatusLabel.Text = "Flat opcode mode selected.";
    }

    private void SetOpcodeToolbarState(bool hasOpcodes)
    {
        ScriptManagerView.TreeButtonsEnabled = hasOpcodes;
        if (!hasOpcodes)
            _scriptTreeMode = false;
        UpdateScriptViewModeState();
    }

    private void UpdateScriptViewModeState()
    {
        ScriptManagerView.ExpandTreeFontWeight = _scriptTreeMode ? FontWeights.SemiBold : FontWeights.Normal;
        ScriptManagerView.DisableTreeFontWeight = _scriptTreeMode ? FontWeights.Normal : FontWeights.SemiBold;
        ScriptManagerView.IsTreeMode = _scriptTreeMode;
    }

    private void HeaderTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, HeaderTabControl))
            return;

        MainTabControl.SelectedIndex = HeaderTabControl.SelectedIndex;
    }

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabControl))
            return;

        HeaderTabControl.SelectedIndex = MainTabControl.SelectedIndex;
    }

    private void ArchiveEntriesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelectedEntry = _archive != null && ArchiveManagerView.SelectedItem is LgpArchiveEntry;
        ArchiveManagerView.ExtractCurrentEnabled = hasSelectedEntry;
        ArchiveManagerView.ReplaceCurrentEnabled = hasSelectedEntry;
        ArchiveManagerView.RemoveCurrentEnabled = hasSelectedEntry;
        ArchiveManagerView.RenameCurrentEnabled = hasSelectedEntry;

        if (_archive != null && ArchiveManagerView.SelectedItem is LgpArchiveEntry entry)
            UpdateArchivePreview(entry);
        else
            ShowArchiveTextPreview(_archive == null
                ? "Archive preview will appear here."
                : "Select an archive entry to preview or extract.");
    }

    private void RefreshArchiveViews(string? selectedArchiveEntryPath = null, bool preserveArchiveSelection = true)
    {
        if (_archive == null)
            return;

        var selectedFieldName = _currentFieldName;
        if (preserveArchiveSelection)
        {
            selectedArchiveEntryPath ??= ArchiveManagerView.SelectedItem is LgpArchiveEntry selectedEntry
                ? selectedEntry.FullPath
                : null;
        }

        _archive = _archive.IsReadOnlyImage
            ? FieldArchive.OpenPlayStationImage(_archive.ArchivePath)
            : _archive.IsLooseDirectory
                ? _archive.Platform == FieldArchivePlatform.PlayStation
                    ? FieldArchive.OpenPlayStationDirectory(_archive.ArchivePath)
                    : FieldArchive.OpenPcDirectory(_archive.ArchivePath)
                : FieldArchive.Open(_archive.ArchivePath);
        _allFields.Clear();
        _allFields.AddRange(_archive.FieldEntries);
        _unavailableFields.Clear();
        RefreshFieldListItems(selectedFieldName);
        var archiveEntries = _archive.ArchiveEntries;
        ArchiveManagerView.ItemsSource = archiveEntries;
        ArchiveManagerView.ExtractAllEnabled = _archive.ArchiveEntries.Count > 0;

        if (!string.IsNullOrWhiteSpace(selectedArchiveEntryPath))
            ArchiveManagerView.SelectedItem = archiveEntries.FirstOrDefault(entry =>
                entry.FullPath.Equals(selectedArchiveEntryPath, StringComparison.OrdinalIgnoreCase));
    }

    private void FieldColumnHeader_Click(object? sender, FieldColumnHeaderClickedEventArgs e)
    {
        var propertyName = e.PropertyName;

        _fieldSortDirection = _fieldSortProperty == propertyName &&
                              _fieldSortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        _fieldSortProperty = propertyName;

        RefreshFieldListItems(_currentFieldName);
        StatusLabel.Text = $"Fields sorted by {propertyName} {_fieldSortDirection.ToString().ToLowerInvariant()}.";
    }

    private void RefreshFieldListItems(string? selectedFieldName = null)
    {
        var search = FieldListView.SearchText == "Search..."
            ? string.Empty
            : FieldListView.SearchText.Trim();
        FieldListView.ItemsSource = ShellFieldListService.BuildItems(
            _allFields,
            search,
            _fieldSortProperty,
            _fieldSortDirection,
            _unavailableFields);

        if (string.IsNullOrWhiteSpace(selectedFieldName))
            return;

        FieldListView.SelectedItem = FieldListView.Items
            .OfType<ShellFieldListItem>()
            .FirstOrDefault(field => field.Name.Equals(selectedFieldName, StringComparison.OrdinalIgnoreCase));
    }

    private string? LastSelectedFieldForArchive(string archivePath)
    {
        return ShellFieldListService.LastSelectedFieldForArchive(
            _settings.LastSelectedFields,
            archivePath);
    }

    private void RememberSelectedField(string fieldName)
    {
        if (ShellFieldListService.RememberSelectedField(_settings.LastSelectedFields, _archivePath, fieldName))
            _shellSession.Save();
    }

    private void ArchiveColumnHeader_Click(object? sender, ArchiveColumnHeaderClickedEventArgs e)
    {
        var propertyName = e.PropertyName;

        _archiveSortDirection = _archiveSortProperty == propertyName &&
                                _archiveSortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        _archiveSortProperty = propertyName;

        var view = CollectionViewSource.GetDefaultView(ArchiveManagerView.ItemsSource);
        if (view == null)
            return;

        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(propertyName, _archiveSortDirection));
        view.Refresh();
        StatusLabel.Text = $"Archive sorted by {propertyName} {_archiveSortDirection.ToString().ToLowerInvariant()}.";
    }

    private void ExtractCurrentArchiveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_archive == null || ArchiveManagerView.SelectedItem is not LgpArchiveEntry entry)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Extract archive entry",
            FileName = Path.GetFileName(entry.FullPath),
            Filter = FileDialogFilters.ArchiveAnyFile,
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllBytes(dialog.FileName, _archive.ReadRawFile(entry.FullPath));
            StatusLabel.Text = $"Extracted {entry.FullPath} to {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Extract", ex, dialog.FileName);
        }
    }

    private async void ReplaceCurrentArchiveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_archive == null || ArchiveManagerView.SelectedItem is not LgpArchiveEntry entry)
            return;

        var dialog = new OpenFileDialog
        {
            Title = $"Replace {entry.FullPath}",
            Filter = FileDialogFilters.ArchiveAnyFile,
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SetBusy(true, $"Replacing {entry.FullPath}...");
            await Task.Run(() =>
            {
                var replacement = File.ReadAllBytes(dialog.FileName);
                _archive.ReplaceRawFile(entry.FullPath, replacement);
            });
            RefreshArchiveViews(entry.FullPath);
            UpdateArchivePreview(
                _archive.ArchiveEntries.First(item => item.FullPath.Equals(entry.FullPath, StringComparison.OrdinalIgnoreCase)));
            StatusLabel.Text = $"Replaced {entry.FullPath}. Backup written beside the archive.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Replace", ex, dialog.FileName);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void AddArchiveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_archive == null)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Add archive entry",
            Filter = FileDialogFilters.ArchiveAnyFile,
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var entryName = Path.GetFileName(dialog.FileName);
        try
        {
            SetBusy(true, $"Adding {entryName}...");
            await Task.Run(() =>
            {
                var data = File.ReadAllBytes(dialog.FileName);
                _archive.ReplaceRawFile(entryName, data);
            });
            RefreshArchiveViews(entryName);
            StatusLabel.Text = $"Added {entryName}. Backup written beside the archive.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Add", ex, dialog.FileName);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RemoveCurrentArchiveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_archive == null || ArchiveManagerView.SelectedItem is not LgpArchiveEntry entry)
            return;

        var answer = MessageBox.Show(this,
            $"Remove {entry.FullPath} from the archive?",
            "Remove archive entry",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            SetBusy(true, $"Removing {entry.FullPath}...");
            await Task.Run(() => _archive.RemoveRawFile(entry.FullPath));
            RefreshArchiveViews(preserveArchiveSelection: false);
            ShowArchiveTextPreview("Select an archive entry to preview or extract.");
            StatusLabel.Text = $"Removed {entry.FullPath}. Backup written beside the archive.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Remove", ex, entry.FullPath);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RenameCurrentArchiveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_archive == null || ArchiveManagerView.SelectedItem is not LgpArchiveEntry entry)
            return;

        var dialog = new Archive.RenameArchiveEntryDialog(entry.FullPath)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            SetBusy(true, $"Renaming {entry.FullPath}...");
            await Task.Run(() => _archive.RenameRawFile(entry.FullPath, dialog.NewArchivePath));
            RefreshArchiveViews(dialog.NewArchivePath);
            StatusLabel.Text = $"Renamed {entry.FullPath} to {dialog.NewArchivePath}. Backup written beside the archive.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Rename", ex, dialog.NewArchivePath);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ExtractAllArchiveEntries_Click(object sender, RoutedEventArgs e)
    {
        if (_archive == null)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Extract all archive entries",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            using var cancellation = new CancellationTokenSource();
            SetBusy(true, "Extracting archive entries...", cancellation);
            var count = await Task.Run(() => _archive.ExtractAll(dialog.FolderName, cancellation.Token), cancellation.Token);
            StatusLabel.Text = $"Extracted {count} archive entr{(count == 1 ? "y" : "ies")} to {dialog.FolderName}.";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Extract all cancelled.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Extract all", ex, dialog.FolderName);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private string BuildArchivePreview(LgpArchiveEntry entry)
    {
        if (_archive == null)
            return "Archive preview will appear here.";

        return ArchiveEntryPreviewBuilder.Build(entry, _archive.ReadRawFile);
    }

    private void UpdateArchivePreview(LgpArchiveEntry entry)
    {
        if (_archive == null)
            return;

        try
        {
            var data = _archive.ReadRawFile(entry.FullPath);
            if (ArchiveImagePreviewLoader.TryLoad(entry.FullPath, data, out var image))
            {
                ArchiveManagerView.PreviewImageSource = image;
                ArchiveManagerView.PreviewImageVisibility = Visibility.Visible;
                ArchiveManagerView.PreviewTextVisibility = Visibility.Collapsed;
                return;
            }
        }
        catch
        {
            // Fall back to the text/hex preview builder below, which reports errors.
        }

        ShowArchiveTextPreview(BuildArchivePreview(entry));
    }

    private void ShowArchiveTextPreview(string text)
    {
        ArchiveManagerView.PreviewImageSource = null;
        ArchiveManagerView.PreviewImageVisibility = Visibility.Collapsed;
        ArchiveManagerView.PreviewTextVisibility = Visibility.Visible;
        ArchiveManagerView.PreviewText = text;
    }

    private void ToggleFieldList_Click(object sender, RoutedEventArgs e)
    {
        _fieldListVisible = FieldListViewMenuItem.IsChecked;
        LeftPanel.Visibility = _fieldListVisible ? Visibility.Visible : Visibility.Collapsed;
        LeftPanelColumn.Width = _fieldListVisible ? new GridLength(220) : new GridLength(0);
        SaveSettings();
    }

    private void TogglePreview_Click(object sender, RoutedEventArgs e)
    {
        if (_previewVisible)
            _settings.Window.PreviewPanelHeight = CurrentPreviewPanelHeightForToggle();

        _previewVisible = BackgroundPreviewMenuItem.IsChecked;
        ApplyPreviewPanelVisibility();
        SaveSettings();
    }

    private double CurrentPreviewPanelHeightForToggle()
    {
        if (PreviewRow.ActualHeight > 0)
            return PreviewRow.ActualHeight;

        return PreviewRow.Height.Value > 0
            ? PreviewRow.Height.Value
            : _settings.Window.PreviewPanelHeight;
    }

    private void SetBackgroundPreviewMode_Click(object sender, RoutedEventArgs e)
    {
        SetPreviewMode("Background");
    }

    private void SetModelPreviewMode_Click(object sender, RoutedEventArgs e)
    {
        SetPreviewMode("Model");
    }

    private void SetPreviewMode(string previewMode)
    {
        _previewMode = previewMode.Equals("Model", StringComparison.OrdinalIgnoreCase)
            ? "Model"
            : "Background";
        BackgroundPreviewModeMenuItem.IsChecked = _previewMode == "Background";
        ModelPreviewModeMenuItem.IsChecked = _previewMode == "Model";
        UpdatePreviewLabel();
        SaveSettings();
    }

    private void PreviewPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 1 || _currentField == null)
            return;

        OpenBackgroundDialog_Click(sender, e);
    }

    private void OpenLlmGenerator_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AI.LLMSceneDialog(_currentField)
        {
            Owner = this,
        };
        dialog.PlanApplying += CaptureLlmApplySnapshot;
        dialog.PlanApplied += ApplyLlmPlanResult;
        dialog.ShowDialog();
    }

    private void CaptureLlmApplySnapshot()
    {
        if (_currentField == null)
            return;

        _lastLlmApplySnapshot = FieldEditSnapshot.Capture(_currentField);
        SetLlmRevertState(true);
    }

    private void ApplyLlmPlanResult(ApplyResult result)
    {
        if (!result.Ok)
            return;

        UpdateDirtyState();
        StatusLabel.Text = $"LLM plan applied to {_currentFieldName}. Use Save to write the archive.";
    }

    private void RevertLlmApply_Click(object sender, RoutedEventArgs e)
    {
        if (_lastLlmApplySnapshot == null)
        {
            StatusLabel.Text = "No LLM apply is available to revert.";
            return;
        }

        var restored = _lastLlmApplySnapshot.Restore();
        _lastLlmApplySnapshot = null;
        DisplayField(restored);
        SetFieldSelectedState(true);
        SetLlmRevertState(false);
        StatusLabel.Text = $"Reverted last LLM apply for {restored.Name}.";
    }

    private void OpenTextManager_Click(object sender, RoutedEventArgs e)
    {
        OpenTextManager();
    }

    private void OpenTextManager(int? selectedTextIndex = null)
    {
        if (_currentSection1 == null)
        {
            MessageBox.Show(this,
                "The selected field does not have parsed Section 1 text data yet.",
                "Texts",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new TextManagerDialog(_currentFieldName ?? "Current field", _currentSection1, selectedTextIndex)
        {
            Owner = this,
        };
        dialog.TextApplied += ApplyTextEdit;
        dialog.TextInserted += InsertText;
        dialog.TextDeleted += DeleteText;
        dialog.ShowDialog();
    }

    private void ApplyTextEdit(int textIndex, string value)
    {
        if (_currentField == null)
            return;

        _currentField.SetText(textIndex, new FF7String(value));
        _currentSection1 = _currentField.ScriptsAndTexts;
        UpdateDirtyState();
        StatusLabel.Text = $"Text {textIndex} modified. Use Save to write the archive.";
    }

    private void InsertText(int textIndex, string value)
    {
        if (_currentField == null)
            return;

        _currentField.InsertText(textIndex, new FF7String(value));
        _currentSection1 = _currentField.ScriptsAndTexts;
        UpdateScriptListForSelectedGroup();
        UpdateDirtyState();
        StatusLabel.Text = $"Text {textIndex} inserted. Script text references were updated.";
    }

    private void DeleteText(int textIndex)
    {
        if (_currentField == null)
            return;

        _currentField.DeleteText(textIndex);
        _currentSection1 = _currentField.ScriptsAndTexts;
        UpdateScriptListForSelectedGroup();
        UpdateDirtyState();
        StatusLabel.Text = $"Text {textIndex} removed. Script text references were updated.";
    }

    private void OpenSearchDialog_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSection1 == null)
        {
            MessageBox.Show(this,
                "Select a field with parsed scripts/texts before searching.",
                "Find",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SearchDialog(
            _currentFieldName ?? "Current field",
            _currentSection1,
            _archive == null ? null : SearchAllFields)
        {
            Owner = this,
        };
        dialog.ResultActivated += NavigateToSearchResult;
        dialog.ShowDialog();
    }

    private void NavigateToSearchResult(FieldSearchResult result)
    {
        _viewModel.NavigateTo(result);

        if (!string.IsNullOrWhiteSpace(result.FieldName)
            && !result.FieldName.Equals(_currentFieldName, StringComparison.OrdinalIgnoreCase))
        {
            if (!SelectFieldByName(result.FieldName))
                return;
        }

        if (result.TextIndex is int textIndex)
        {
            OpenTextManager(textIndex);
            StatusLabel.Text = $"Opened {result.Location}.";
            return;
        }

        if (result.GroupIndex is not int groupIndex)
            return;

        HeaderTabControl.SelectedIndex = 0;
        MainTabControl.SelectedIndex = 0;
        ScriptManagerView.SelectedGroupIndex = groupIndex;
        UpdateScriptListForSelectedGroup();

        if (result.ScriptIndex is int scriptIndex)
        {
            ScriptManagerView.SelectedScriptIndex = scriptIndex;

            if (result.OpcodeOffset is int opcodeOffset)
                SelectOpcodeOffset(opcodeOffset);
        }

        StatusLabel.Text = $"Navigated to {result.Location}.";
    }

    private IReadOnlyList<FieldSearchResult> SearchAllFields(string query)
    {
        if (_archive == null || string.IsNullOrWhiteSpace(query))
            return [];

        var searchService = new FieldSearchService();
        var results = new List<FieldSearchResult>();
        foreach (var entry in _allFields)
        {
            try
            {
                var field = _archive.OpenField(entry.Name);
                if (field.ScriptsAndTexts == null)
                    continue;

                results.AddRange(searchService.SearchCurrentField(entry.Name, field.ScriptsAndTexts, query));
            }
            catch
            {
                // Some LGP entries are not valid PC fields; skip them for archive-wide search.
            }
        }

        return results;
    }

    private bool SelectFieldByName(string fieldName)
    {
        var target = FieldListView.Items.OfType<ShellFieldListItem>().FirstOrDefault(field =>
            field.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            StatusLabel.Text = $"Field not found: {fieldName}.";
            return false;
        }

        FieldListView.SelectedItem = target;
        FieldListView.ScrollIntoView(target);
        return _currentSection1 != null
            && fieldName.Equals(_currentFieldName, StringComparison.OrdinalIgnoreCase);
    }

    private void OpenVariableManager_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSection1 == null)
        {
            MessageBox.Show(this,
                "Select a field with parsed scripts before opening the Variable Manager.",
                "Variable Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new VariableManagerDialog(_currentFieldName ?? "Current field", _currentSection1)
        {
            Owner = this,
        };
        dialog.ReferenceActivated += NavigateToVariableReference;
        dialog.ShowDialog();
    }

    private void NavigateToVariableReference(VariableReference reference)
    {
        HeaderTabControl.SelectedIndex = 0;
        MainTabControl.SelectedIndex = 0;
        ScriptManagerView.SelectedGroupIndex = reference.GroupIndex;
        UpdateScriptListForSelectedGroup();
        ScriptManagerView.SelectedScriptIndex = reference.ScriptIndex;
        SelectOpcodeOffset(reference.OpcodeOffset);
        StatusLabel.Text = $"Navigated to {reference.Location}.";
    }

    private void OpenWalkmeshManager_Click(object sender, RoutedEventArgs e)
    {
        if (_currentField?.Walkmesh == null)
        {
            MessageBox.Show(this,
                "The selected field does not have parsed walkmesh data yet.",
                "Walkmesh",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new WalkmeshManagerDialog(_currentFieldName ?? "Current field", _currentField)
        {
            Owner = this,
        };
        dialog.ShowDialog();
        UpdateDirtyState();
    }

    private void OpenEncounterDialog_Click(object sender, RoutedEventArgs e)
    {
        if (_currentField?.Encounters == null)
        {
            MessageBox.Show(this,
                "The selected field does not have parsed encounter data yet.",
                "Encounters",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new EncounterDialog(_currentFieldName ?? "Current field", _currentField)
        {
            Owner = this,
        };
        dialog.ShowDialog();
        UpdateDirtyState();
    }

    private void OpenModelManager_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlayStationField?.ModelLoaderPS != null)
        {
            var playStationDialog = new PlayStationModelManagerDialog(
                _currentPlayStationField.Name,
                _currentPlayStationField.ModelLoaderPS,
                _currentPlayStationField,
                ReadPlayStationModelDataOrEmpty(_currentPlayStationField.Name))
            {
                Owner = this,
            };
            playStationDialog.ShowDialog();
            UpdateDirtyState();
            UpdatePreviewLabel();
            return;
        }

        if (_currentField?.ModelLoader == null)
        {
            MessageBox.Show(this,
                "The selected field does not have parsed model loader data yet.",
                "Map Models",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new ModelManagerDialog(_currentFieldName ?? "Current field", _currentField)
        {
            Owner = this,
        };
        dialog.ShowDialog();
        UpdateDirtyState();
        UpdatePreviewLabel();
    }

    private void OpenPlayStationSections_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlayStationField == null)
        {
            MessageBox.Show(this,
                "Select a PlayStation field to inspect its DAT sections.",
                "PlayStation Sections",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new PlayStationSectionManagerDialog(_currentPlayStationField)
        {
            Owner = this,
        };
        dialog.ShowDialog();
        UpdateDirtyState();
        UpdatePreviewLabel();
    }

    private void OpenBackgroundDialog_Click(object sender, RoutedEventArgs e)
    {
        if (_currentField?.Background == null)
        {
            MessageBox.Show(this,
                "The selected field does not have parsed background data yet.",
                "Background",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new BackgroundDialog(
            _currentFieldName ?? "Current field",
            _currentField.Background,
            data =>
            {
                _currentField.ReplaceBackgroundSection(data);
                UpdateDirtyState();
                UpdatePreviewLabel();
            })
        {
            Owner = this,
        };
        dialog.ShowDialog();
        UpdateDirtyState();
        UpdatePreviewLabel();
    }

    private void OpenMiscellaneousDialog_Click(object sender, RoutedEventArgs e)
    {
        if (_currentField?.Inf == null)
        {
            MessageBox.Show(this,
                "The selected field does not have parsed miscellaneous/INF data yet.",
                "Miscellaneous",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new MiscellaneousDialog(_currentFieldName ?? "Current field", _currentField, _currentSection1)
        {
            Owner = this,
        };
        dialog.ShowDialog();
        UpdateDirtyState();
    }

    private byte[] ReadPlayStationModelDataOrEmpty(string fieldName)
    {
        if (_archive == null)
            return [];

        try
        {
            return _archive.ReadPlayStationModelData(fieldName);
        }
        catch
        {
            return [];
        }
    }

    private void OpenTutorialsDialog_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSection1?.TutorialsAndSounds == null)
        {
            MessageBox.Show(this,
                "The selected field does not have parsed musics/tutorials data yet.",
                "Musics/Tutorials",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new TutorialsDialog(_currentFieldName ?? "Current field", _currentSection1.TutorialsAndSounds)
        {
            Owner = this,
        };
        dialog.ShowDialog();
    }

    private async void OpenBatchProcessingDialog_Click(object sender, RoutedEventArgs e)
    {
        var archive = _archive;
        if (archive == null)
            return;

        var dialog = new BatchProcessingDialog(
            isPcArchive: archive.Platform == FieldArchivePlatform.PC && !archive.IsReadOnlyImage)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            using var cancellation = new CancellationTokenSource();
            SetBusy(true, "Applying batch operations...", cancellation);
            var processor = new ArchiveBatchProcessor();
            var activeArchive = archive;
            var result = await Task.Run(() =>
                processor.Apply(activeArchive, dialog.SelectedOperations, cancellation.Token), cancellation.Token);

            RefreshArchiveViews(_currentFieldName);
            if (_currentFieldName != null)
                SelectFieldByName(_currentFieldName);
            StatusLabel.Text =
                $"Batch complete: {result.FieldsChanged}/{result.FieldsVisited} field(s) changed, " +
                $"{result.TextEntriesChanged} text entr{(result.TextEntriesChanged == 1 ? "y" : "ies")} updated, " +
                $"{result.TextWindowsAutosized} text window{(result.TextWindowsAutosized == 1 ? string.Empty : "s")} autosized, " +
                $"{result.EncounterTablesChanged} encounter table{(result.EncounterTablesChanged == 1 ? string.Empty : "s")} disabled, " +
                $"{result.ModelLoaderEntriesChanged} model-loader entr{(result.ModelLoaderEntriesChanged == 1 ? "y" : "ies")} cleaned, " +
                $"{result.BackgroundSectionsRemoved} background tile section{(result.BackgroundSectionsRemoved == 1 ? string.Empty : "s")} removed, " +
                $"{result.BackgroundResizeTilesAdded} background resize tile{(result.BackgroundResizeTilesAdded == 1 ? string.Empty : "s")} added, " +
                $"{result.BackgroundTilesRepaired} background tile{(result.BackgroundTilesRepaired == 1 ? string.Empty : "s")} repaired.";
            if (result.Errors.Count > 0)
            {
                MessageBox.Show(this,
                    string.Join(Environment.NewLine, result.Errors.Take(12)),
                    "Batch processing skipped fields",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Batch processing cancelled.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Batch processing", ex, _archivePath);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenSettingsDialog_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        var dialog = new SettingsDialog(_settings)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
            return;

        ApplySettings();
        SaveSettings();
        SetArchiveOpenState(_archive != null);
        if (_currentFieldName != null)
            SetFieldSelectedState(true);
        StatusLabel.Text = "Configuration saved.";
    }

    private void LanguageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string language })
            return;

        _settings.Language = ShellLocalization.NormalizeLanguage(language);
        ApplyShellLocalization();
        SaveSettings();
        var strings = ShellLocalization.ForLanguage(_settings.Language);
        StatusLabel.Text = string.Format(
            strings["LanguageApplied"],
            LanguageDisplayName(_settings.Language));
    }

    private void RunFf7_Click(object sender, RoutedEventArgs e)
    {
        var executable = _settings.Ff7ExecutablePath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            MessageBox.Show(this,
                "Configure the FF7 executable path in Settings > Configuration before running FF7.",
                "Run FF7",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!File.Exists(executable))
        {
            MessageBox.Show(this,
                $"The configured FF7 executable does not exist:\n{executable}",
                "Run FF7",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
                UseShellExecute = true,
            });
            StatusLabel.Text = "FF7 launched.";
        }
        catch (Exception ex)
        {
            ShowFileActionError("Run FF7", ex, executable);
        }
    }

    private void ShowFileActionError(string action, Exception exception, string? targetPath = null)
    {
        var error = FileActionErrorFormatter.Format(action, exception, targetPath);
        MessageBox.Show(this, error.Message, error.Title, MessageBoxButton.OK, MessageBoxImage.Error);
        StatusLabel.Text = error.StatusText;
    }

    private void JapaneseText_Click(object sender, RoutedEventArgs e)
    {
        var enabled = JapaneseTextMenuItem.IsChecked;
        _settings.JapaneseText = enabled;
        _currentSection1?.SetJapaneseText(enabled);
        if (_currentSection1 != null)
        {
            var selectedScriptIndex = ScriptManagerView.SelectedScriptIndex;
            UpdateScriptListForSelectedGroup();
            if (selectedScriptIndex >= 0)
                ScriptManagerView.SelectedScriptIndex = selectedScriptIndex;
        }

        SaveSettings();
        StatusLabel.Text = JapaneseTextMenuItem.IsChecked
            ? "Japanese character mode enabled."
            : "Japanese character mode disabled.";
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Makou Reactor\nFinal Fantasy VII field archive editor\n\nC# GUI port in progress.",
            "About Makou Reactor",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!PromptSaveCurrentFieldIfNeeded())
        {
            e.Cancel = true;
            return;
        }

        SaveSettings();
    }

    private void ApplySettings()
    {
        var window = _settings.Window;
        Width = Math.Max(MinWidth, window.Width);
        Height = Math.Max(MinHeight, window.Height);
        if (window.Left >= 0 && window.Top >= 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = window.Left;
            Top = window.Top;
        }

        _fieldListVisible = window.FieldListVisible;
        _previewVisible = window.PreviewVisible;
        _previewMode = window.PreviewMode.Equals("Model", StringComparison.OrdinalIgnoreCase)
            ? "Model"
            : "Background";
        FieldListViewMenuItem.IsChecked = _fieldListVisible;
        BackgroundPreviewMenuItem.IsChecked = _previewVisible;
        BackgroundPreviewModeMenuItem.IsChecked = _previewMode == "Background";
        ModelPreviewModeMenuItem.IsChecked = _previewMode == "Model";
        LeftPanel.Visibility = _fieldListVisible ? Visibility.Visible : Visibility.Collapsed;
        LeftPanelColumn.Width = _fieldListVisible
            ? new GridLength(Math.Max(160, window.LeftPanelWidth))
            : new GridLength(0);
        PreviewRow.Height = _previewVisible
            ? new GridLength(Math.Max(100, window.PreviewPanelHeight))
            : new GridLength(0);
        ApplyPreviewPanelVisibility();
        JapaneseTextMenuItem.IsChecked = _settings.JapaneseText;
        MainTabControl.SelectedIndex = Math.Clamp(window.SelectedMainTab, 0, 1);
        HeaderTabControl.SelectedIndex = MainTabControl.SelectedIndex;
        ApplyShellLocalization();
        UpdatePreviewLabel();
    }

    private void SaveSettings()
    {
        ShellWindowSettingsPolicy.Apply(_settings,
            new ShellWindowStateSnapshot(
                Width,
                Height,
                Left,
                Top,
                RestoreBounds.Width,
                RestoreBounds.Height,
                RestoreBounds.Left,
                RestoreBounds.Top,
                MinWidth,
                MinHeight,
                LeftPanelColumn.ActualWidth,
                LeftPanelColumn.Width.Value,
                PreviewRow.ActualHeight,
                PreviewRow.Height.Value,
                _settings.Window.PreviewPanelHeight,
                _fieldListVisible,
                _previewVisible,
                _previewMode,
                MainTabControl.SelectedIndex,
                JapaneseTextMenuItem.IsChecked));
        _shellSession.Save();
    }

    private void ApplyShellLocalization()
    {
        _settings.Language = ShellLocalization.NormalizeLanguage(_settings.Language);
        var strings = ShellLocalization.ForLanguage(_settings.Language);

        FileMenuItem.Header = strings["File"];
        ToolsMenuItem.Header = strings["Tools"];
        SettingsMenuItem.Header = strings["Settings"];
        ViewMenuItem.Header = strings["View"];
        HelpMenuItem.Header = strings["Help"];
        JapaneseTextMenuItem.Header = strings["JapaneseCharacters"];
        LanguageMenuItem.Header = strings["Language"];
        EnglishLanguageMenuItem.Header = strings["English"];
        FrenchLanguageMenuItem.Header = strings["French"];
        JapaneseLanguageMenuItem.Header = strings["Japanese"];
        ConfigurationMenuItem.Header = strings["Configuration"];
        FieldListViewMenuItem.Header = strings["FieldList"];
        BackgroundPreviewMenuItem.Header = strings["BackgroundPreview"];
        PreviewModeMenuItem.Header = strings["PreviewMode"];
        BackgroundPreviewModeMenuItem.Header = strings["Background"];
        ModelPreviewModeMenuItem.Header = strings["Model"];

        OpenToolButton.ToolTip = strings["OpenFileTip"];
        SaveToolButton.ToolTip = strings["SaveTip"];
        FindToolButton.ToolTip = strings["FindTip"];
        RunFf7ToolButton.ToolTip = strings["RunFf7Tip"];
        TextsToolButton.ToolTip = strings["TextsTip"];
        ModelsToolButton.ToolTip = strings["ModelsTip"];
        WalkmeshToolButton.ToolTip = strings["WalkmeshTip"];
        LlmGenerateToolButton.ToolTip = strings["LlmTip"];
        RevertLlmApplyToolButton.ToolTip = strings["RevertLlmTip"];

        EnglishLanguageMenuItem.IsChecked = _settings.Language == "en";
        FrenchLanguageMenuItem.IsChecked = _settings.Language == "fr";
        JapaneseLanguageMenuItem.IsChecked = _settings.Language == "ja";

        if (string.IsNullOrWhiteSpace(StatusLabel.Text) ||
            StatusLabel.Text.Equals("Ready", StringComparison.Ordinal) ||
            StatusLabel.Text.Equals("Pret", StringComparison.Ordinal) ||
            StatusLabel.Text.Equals("準備完了", StringComparison.Ordinal))
        {
            StatusLabel.Text = strings["Ready"];
        }
    }

    private static string LanguageDisplayName(string language) => language switch
    {
        "fr" => "Francais",
        "ja" => "日本語",
        _ => "English",
    };

    private void RefreshRecentFilesMenu()
    {
        RecentFilesMenu.Items.Clear();
        var files = _recentFilesService.RecentFiles.ToArray();
        RecentFilesMenu.IsEnabled = files.Length > 0;

        if (files.Length == 0)
            return;

        foreach (var path in files)
        {
            var item = new MenuItem
            {
                Header = path,
                Tag = path,
            };
            item.Click += OpenRecentFile_Click;
            RecentFilesMenu.Items.Add(item);
        }

        RecentFilesMenu.Items.Add(new Separator());
        var clearItem = new MenuItem
        {
            Header = "Clear Recent Files",
        };
        clearItem.Click += ClearRecentFiles_Click;
        RecentFilesMenu.Items.Add(clearItem);
    }

    private void SetArchiveOpenState(bool isOpen)
    {
        var canRunFf7 = !string.IsNullOrWhiteSpace(_settings.Ff7ExecutablePath);
        _viewModel.IsArchiveOpen = isOpen;
        _viewModel.IsFieldSelected = false;
        if (!isOpen && _currentField == null)
        {
            _viewModel.CurrentField = null;
            _viewModel.ClearScriptSelection();
        }
        _viewModel.IsFf7Configured = canRunFf7;
        ApplyShellCommandState(
            ShellCommandStatePolicy.ForArchiveOpen(
                isOpen,
                canRunFf7,
                _lastLlmApplySnapshot != null,
                _archive?.Platform == FieldArchivePlatform.PC && _archive.IsReadOnlyImage != true));
        CommandManager.InvalidateRequerySuggested();
    }

    private void SetFieldSelectedState(bool isSelected)
    {
        var hasArchive = _archive != null;
        _viewModel.IsArchiveOpen = hasArchive;
        _viewModel.IsFieldSelected = isSelected;
        _viewModel.CurrentFieldName = isSelected ? _currentFieldName ?? string.Empty : string.Empty;
        if (!isSelected)
        {
            _viewModel.CurrentField = null;
            _viewModel.ScriptManager.SetDirtyState(false);
            _viewModel.ClearScriptSelection();
        }
        ApplyShellCommandState(
            ShellCommandStatePolicy.ForPcFieldSelection(
                isSelected,
                hasArchive,
                !string.IsNullOrWhiteSpace(_settings.Ff7ExecutablePath),
                _lastLlmApplySnapshot != null));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ApplyShellCommandState(ShellCommandState state)
    {
        SaveMenuItem.IsEnabled = state.Save;
        SaveToolButton.IsEnabled = state.Save;
        SaveAsMenuItem.IsEnabled = state.SaveAs;
        ExportCurrentMapMenuItem.IsEnabled = state.ExportCurrentMap;
        ExportChunksMenuItem.IsEnabled = state.ExportChunks;
        MassExportMenuItem.IsEnabled = state.MassExport;
        ImportCurrentMapMenuItem.IsEnabled = state.ImportCurrentMap;
        CloseArchiveMenuItem.IsEnabled = state.CloseArchive;
        ArchiveManagerView.AddEnabled = state.ArchiveAdd;
        ArchiveManagerView.ReplaceCurrentEnabled = state.ArchiveReplaceCurrent;
        ArchiveManagerView.RemoveCurrentEnabled = state.ArchiveRemoveCurrent;
        ArchiveManagerView.RenameCurrentEnabled = state.ArchiveRenameCurrent;
        RunFf7MenuItem.IsEnabled = state.RunFf7;
        RunFf7ToolButton.IsEnabled = state.RunFf7;

        TextsMenuItem.IsEnabled = state.Texts;
        ModelsMenuItem.IsEnabled = state.Models;
        EncountersMenuItem.IsEnabled = state.Encounters;
        TutorialsMenuItem.IsEnabled = state.Tutorials;
        WalkmeshMenuItem.IsEnabled = state.Walkmesh;
        BackgroundMenuItem.IsEnabled = state.Background;
        MiscMenuItem.IsEnabled = state.Misc;
        PlayStationSectionsMenuItem.IsEnabled = state.PlayStationSections;
        BatchMenuItem.IsEnabled = state.Batch;

        TextsToolButton.IsEnabled = state.TextsTool;
        ModelsToolButton.IsEnabled = state.ModelsTool;
        WalkmeshToolButton.IsEnabled = state.WalkmeshTool;
        LlmGenerateMenuItem.IsEnabled = state.LlmGenerate;
        LlmGenerateToolButton.IsEnabled = state.LlmGenerate;
        SetLlmRevertState(state.LlmRevert);
    }

    private void SetLlmRevertState(bool enabled)
    {
        RevertLlmApplyMenuItem.IsEnabled = enabled;
        RevertLlmApplyToolButton.IsEnabled = enabled;
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearLlmApplySnapshot()
    {
        _lastLlmApplySnapshot = null;
        SetLlmRevertState(false);
    }

    private bool PromptSaveCurrentFieldIfNeeded()
    {
        var currentField = _currentField;
        var currentPlayStationField = _currentPlayStationField;
        var isModified = currentField?.IsModified == true || currentPlayStationField?.IsModified == true;
        if (!isModified)
            return true;

        var modifiedName = currentField?.Name ?? currentPlayStationField!.Name;
        var result = MessageBox.Show(this,
            $"Save changes to {modifiedName}?",
            "Unsaved changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        var choice = result switch
        {
            MessageBoxResult.Yes => DialogResultChoice.Yes,
            MessageBoxResult.No => DialogResultChoice.No,
            MessageBoxResult.Cancel => DialogResultChoice.Cancel,
            _ => DialogResultChoice.None,
        };

        var archive = _archive;
        var action = UnsavedChangesPromptPolicy.Decide(isModified, archive != null, choice);
        if (action == UnsavedChangesAction.Cancel)
            return false;
        if (action == UnsavedChangesAction.Continue)
            return true;

        try
        {
            if (currentField != null)
            {
                if (archive != null)
                    archive.SaveField(currentField);
                else if (!string.IsNullOrWhiteSpace(_archivePath))
                    StandaloneFieldLoader.Save(_archivePath, currentField);
                else
                    throw new InvalidOperationException("No save target is available for the current field.");
            }
            else
            {
                if (archive == null)
                    throw new InvalidOperationException("No archive is available for the current PlayStation field.");
                archive!.SaveField(currentPlayStationField!);
            }
            if (archive != null)
                RefreshArchiveViews(modifiedName);
            UpdateDirtyState();
            StatusLabel.Text = archive == null || archive.IsLooseDirectory
                ? $"Saved {modifiedName}."
                : $"Saved {modifiedName}. Backup written beside the archive.";
            return true;
        }
        catch (Exception ex)
        {
            ShowFileActionError("Save", ex, modifiedName);
            return false;
        }
    }

    private void UpdateDirtyState()
    {
        var isModified = _currentField?.IsModified == true || _currentPlayStationField?.IsModified == true;
        _viewModel.IsCurrentFieldModified = isModified;
        _viewModel.ScriptManager.SetDirtyState(_currentSection1?.IsModified == true, _currentFieldName);
        _viewModel.ArchiveName = string.IsNullOrWhiteSpace(_archivePath)
            ? "No archive open"
            : Path.GetFileName(_archivePath);
        _viewModel.ArchivePath = _archivePath ?? string.Empty;
        _viewModel.CurrentField = _currentField;
        _viewModel.CurrentFieldName = _currentFieldName ?? string.Empty;
        Title = isModified ? "Makou Reactor *" : "Makou Reactor";
        if (!string.IsNullOrWhiteSpace(_archivePath))
        {
            ArchiveLabel.Text = isModified
                ? $"{Path.GetFileName(_archivePath)} - {_currentFieldName} *"
                : Path.GetFileName(_archivePath);
        }

        var commandState = ShellCommandStatePolicy.ForDirtyState(
            isModified,
            _archive != null || !string.IsNullOrWhiteSpace(_archivePath));
        SaveMenuItem.IsEnabled = commandState.Save;
        SaveToolButton.IsEnabled = commandState.Save;
        CommandManager.InvalidateRequerySuggested();
    }

    private void ApplyPreviewPanelVisibility()
    {
        PreviewPanel.Visibility = _previewVisible ? Visibility.Visible : Visibility.Collapsed;
        PreviewGridSplitter.Visibility = _previewVisible ? Visibility.Visible : Visibility.Collapsed;
        PreviewSplitterRow.Height = _previewVisible ? new GridLength(5) : new GridLength(0);
        if (_previewVisible && PreviewRow.Height.Value <= 0)
            PreviewRow.Height = new GridLength(Math.Max(100, _settings.Window.PreviewPanelHeight));
        if (!_previewVisible)
            PreviewRow.Height = new GridLength(0);
    }

    private void UpdatePreviewLabel()
    {
        if (_currentPlayStationField != null)
            PreviewPanel.ShowPlayStationField(_currentPlayStationField);
        else
            PreviewPanel.ShowField(_currentField, _previewMode);
    }

    private void SetBusy(bool isBusy, string? message = null, CancellationTokenSource? cancellation = null)
    {
        if (!isBusy)
            _busyCancellation = null;
        else
            _busyCancellation = cancellation;

        BusyOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        BusyText.Text = message ?? string.Empty;
        BusyCancelButton.Visibility = isBusy && cancellation != null ? Visibility.Visible : Visibility.Collapsed;
        BusyCancelButton.IsEnabled = isBusy && cancellation != null && !cancellation.IsCancellationRequested;
        _viewModel.IsBusy = isBusy;
        _viewModel.BusyMessage = message ?? string.Empty;
        Mouse.OverrideCursor = isBusy ? Cursors.Wait : null;
        if (isBusy && !string.IsNullOrWhiteSpace(message))
            StatusLabel.Text = message;
    }

    private void BusyCancelButton_Click(object sender, RoutedEventArgs e)
    {
        var cancellation = _busyCancellation;
        if (cancellation == null || cancellation.IsCancellationRequested)
            return;

        cancellation.Cancel();
        BusyCancelButton.IsEnabled = false;
        BusyText.Text = "Cancelling...";
        StatusLabel.Text = "Cancelling...";
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private sealed record StandaloneOpenResult(byte[] Data, FieldPC Field);

    private sealed record GroupListItem(int Index, string Name, string Type);

    private sealed record SectionListItem(string Name, int Size, Script? Script);

    private sealed record OpcodeListItem(RawOpcode Opcode)
    {
        public string OffsetHex => Opcode.OffsetHex;
        public string Name => Opcode.Name;
        public string IdHex => Opcode.IdHex;
        public string SizeLabel => Opcode.IsTruncated
            ? $"{Opcode.Size}/{Opcode.DeclaredSize}"
            : Opcode.Size.ToString();
        public string Arguments => Opcode.Arguments;
        public string Warning => Opcode.Warning;
        public string RawBytesHex => Opcode.RawBytesHex;
    }

    private void ShowScriptPlaceholder(string text)
    {
        ScriptManagerView.PlaceholderText = text;
        ScriptManagerView.PlaceholderVisibility = Visibility.Visible;
    }

    private void HideScriptPlaceholder()
    {
        ScriptManagerView.PlaceholderVisibility = Visibility.Collapsed;
    }

    private void SelectOpcodeOffset(int opcodeOffset)
    {
        foreach (var item in ScriptManagerView.Opcodes)
        {
            if (item is not OpcodeListItem opcode || opcode.Opcode.Offset != opcodeOffset)
                continue;

            ScriptManagerView.SelectedOpcode = item;
            ScriptManagerView.ScrollOpcodeIntoView(item);
            ScriptManagerView.FocusOpcodes();
            HideScriptPlaceholder();
            return;
        }
    }

}

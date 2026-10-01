using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using MakouReactor.AI;
using MakouReactor.AI.Backends;
using MakouReactor.AI.History;
using MakouReactor.AI.Mapping;
using MakouReactor.Core.Models;
using MakouReactor.Core.Services;

namespace MakouReactor.UI.WPF.AI;

/// <summary>
/// Scene generation tool hosted from the main Makou Reactor shell: generate from a description,
/// refine the result, browse past generations, preview exactly what will change, then apply.
/// </summary>
public partial class LLMSceneDialog : Window
{
    private readonly LLMConfig _config;
    private readonly Field? _field;
    private readonly GenerationHistoryStore _history;
    private readonly DispatcherTimer _elapsedTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _stopwatch = new();
    private SceneGenerationService? _activeService;
    private SceneGenerationResult? _latestResult;
    private ScenePrompt? _latestPrompt;
    private bool _restoringHistory;

    public event Action? PlanApplying;
    public event Action<ApplyResult>? PlanApplied;

    public LLMSceneDialog(Field? field = null)
        : this(field, new GenerationHistoryStore())
    {
    }

    public LLMSceneDialog(Field? field, GenerationHistoryStore history)
    {
        InitializeComponent();
        _field = field;
        _history = history;
        _config = LoadConfiguredBackend();
        BackendLabel.Text = _config.Backend == "http"
            ? $"Backend: http ({_config.Endpoint})"
            : $"Backend: codex (subscription{(string.IsNullOrEmpty(_config.CodexModel) ? "" : $", model {_config.CodexModel}")})";
        FieldContextLabel.Text = BuildFieldContextText(field);
        _elapsedTimer.Tick += (_, _) => ElapsedLabel.Text = FormatElapsed(_stopwatch.Elapsed);
        Closed += (_, _) => _elapsedTimer.Stop();
        RefreshHistory();
    }

    private static LLMConfig LoadConfiguredBackend()
    {
        var settings = new JsonAppSettingsService().Load();
        return new LLMConfig
        {
            Backend = settings.Llm.Backend,
            CodexExecutable = settings.Llm.CodexExecutable,
            CodexModel = settings.Llm.CodexModel,
            TimeoutMs = settings.Llm.TimeoutMs,
            MaxRepairAttempts = Math.Max(0, settings.Llm.RepairAttempts),
            Endpoint = settings.Llm.Endpoint,
            ApiKey = settings.Llm.ApiKey,
            Model = settings.Llm.Model,
        };
    }

    // -----------------------------------------------------------------------
    // Generate / refine
    // -----------------------------------------------------------------------

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        var userText = PromptBox.Text.Trim();
        if (userText.Length == 0)
        {
            StatusLabel.Text = "Enter a scene description first.";
            return;
        }

        var prompt = new ScenePrompt
        {
            UserText = userText,
            GenDialog = GenDialogCheck.IsChecked == true,
            GenLayout = GenLayoutCheck.IsChecked == true,
            GenScripts = GenScriptsCheck.IsChecked == true,
        };

        await RunAsync(prompt, refinement: null,
            (service, progress) => service.GenerateAsync(prompt, _field, progress: progress));
    }

    private async void RefineButton_Click(object sender, RoutedEventArgs e)
    {
        var instruction = RefineBox.Text.Trim();
        if (_latestResult is not { Ok: true } previous || _latestPrompt == null)
        {
            StatusLabel.Text = "Generate a scene before refining it.";
            return;
        }

        if (instruction.Length == 0)
        {
            StatusLabel.Text = "Enter what should change first.";
            return;
        }

        var prompt = _latestPrompt;
        await RunAsync(prompt, instruction,
            (service, progress) => service.RefineAsync(previous, instruction, prompt, _field, progress: progress));
    }

    private async System.Threading.Tasks.Task RunAsync(
        ScenePrompt prompt,
        string? refinement,
        Func<SceneGenerationService, IProgress<LLMProgressEvent>, System.Threading.Tasks.Task<SceneGenerationResult>> run)
    {
        SetBusy(true);
        OutputBox.Clear();
        PreviewSummaryBox.Clear();
        IssuesList.Items.Clear();
        StatusLabel.Text = refinement == null
            ? $"Generating via '{_config.Backend}' backend..."
            : $"Refining via '{_config.Backend}' backend...";

        var service = new SceneGenerationService(_config);
        _activeService = service;
        var progress = new Progress<LLMProgressEvent>(e => StatusLabel.Text = DescribeProgress(e));

        try
        {
            var result = await run(service, progress);
            SaveToHistory(prompt, result, refinement);

            if (!result.Ok)
            {
                _latestResult = null;
                StatusLabel.Text = $"Failed: {result.Error}";
                OutputBox.Text = result.RawJson;
                return;
            }

            _latestPrompt = prompt;
            ShowResult(result);

            var repairs = result.Attempts.Count - 1;
            var repairNote = repairs > 0 && result.Attempts[^1].Length == 0
                ? $" (after {repairs} repair attempt{(repairs == 1 ? string.Empty : "s")})"
                : string.Empty;
            StatusLabel.Text = result.Validation!.HasErrors
                ? $"Generated with validation errors ({result.Validation.Issues.Count} issue(s)){repairNote}"
                : $"Done - {result.Plan!.Actors.Count} actors, {result.Plan.Dialog.Count} dialog lines, {result.Plan.Events.Count} events{repairNote}";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Unexpected error: {ex.Message}";
        }
        finally
        {
            _activeService = null;
            service.Dispose();
            SetBusy(false);
            RefreshHistory();
        }
    }

    /// <summary>Show a successful result: JSON, validation/layout notes and the change preview.</summary>
    private void ShowResult(SceneGenerationResult result)
    {
        _latestResult = result;
        OutputBox.Text = result.RawJson;

        IssuesList.Items.Clear();
        foreach (var note in result.Layout?.Notes ?? new List<string>())
            IssuesList.Items.Add($"[Layout] {note}");
        foreach (var issue in result.Validation!.Issues)
            IssuesList.Items.Add(issue.ToString());

        RefreshPreview();
        ApplyButton.IsEnabled = !result.Validation.HasErrors && _field != null;
        RefineBox.IsEnabled = true;
        RefineButton.IsEnabled = true;
    }

    private void RefreshPreview()
    {
        if (_latestResult?.Plan == null)
            return;

        var preview = ScenePlanMapper.ApplyToField(
            _latestResult.Plan,
            _field,
            new ApplyOptions { PreviewOnly = true, WalkmeshMode = SelectedWalkmeshMode(), ScriptMode = SelectedScriptMode() });
        PreviewSummaryBox.Text = preview.Ok ? preview.Summary : preview.Error;
    }

    private WalkmeshMode SelectedWalkmeshMode() => WalkmeshModeBox.SelectedIndex switch
    {
        1 => WalkmeshMode.Merge,
        2 => WalkmeshMode.Replace,
        _ => WalkmeshMode.Ignore,
    };

    private ScriptMode SelectedScriptMode() =>
        WriteScriptsCheck.IsChecked == true ? ScriptMode.AppendGroup : ScriptMode.None;

    private void WriteScriptsCheck_Changed(object sender, RoutedEventArgs e)
    {
        // Also fires while the window is being constructed, before any result exists.
        if (_latestResult == null)
            return;

        RefreshPreview();
    }

    private void WalkmeshModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Also fires while the window is being constructed, before any result exists.
        if (_latestResult == null)
            return;

        RefreshPreview();
    }

    private void SetBusy(bool busy)
    {
        GenerateButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        GenerationProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.IsEnabled = !busy;

        if (busy)
        {
            ApplyButton.IsEnabled = false;
            RefineButton.IsEnabled = false;
            RefineBox.IsEnabled = false;
            _stopwatch.Restart();
            ElapsedLabel.Text = FormatElapsed(TimeSpan.Zero);
            _elapsedTimer.Start();
        }
        else
        {
            _elapsedTimer.Stop();
            _stopwatch.Stop();
            ElapsedLabel.Text = $"Took {FormatElapsed(_stopwatch.Elapsed)}";
        }
    }

    private static string FormatElapsed(TimeSpan elapsed) => $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";

    private string DescribeProgress(LLMProgressEvent e)
    {
        var attempt = e.Attempt > 1 ? $" (attempt {e.Attempt})" : string.Empty;
        return e.Phase switch
        {
            LLMProgressPhase.Starting => $"Starting via '{_config.Backend}' backend...",
            LLMProgressPhase.Waiting => $"Waiting for the model{attempt}...",
            LLMProgressPhase.Responding => string.IsNullOrEmpty(e.Detail) ? $"Model is responding{attempt}..." : $"Model: {e.Detail}",
            LLMProgressPhase.Parsing => $"Reading the model's answer{attempt}...",
            LLMProgressPhase.Validating => $"Checking the plan{attempt}...",
            LLMProgressPhase.Repairing => $"Output had problems - asking the model to fix them{attempt}: {e.Detail}",
            _ => StatusLabel.Text,
        };
    }

    // -----------------------------------------------------------------------
    // History
    // -----------------------------------------------------------------------

    private void SaveToHistory(ScenePrompt prompt, SceneGenerationResult result, string? refinement)
    {
        try
        {
            _history.Add(GenerationHistoryEntry.From(prompt, result, _field?.Name, refinement));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // History is a convenience; never let a disk problem hide the generated plan.
        }
    }

    private void RefreshHistory()
    {
        _restoringHistory = true;
        try
        {
            HistoryList.ItemsSource = _history.List();
        }
        finally
        {
            _restoringHistory = false;
        }
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringHistory || HistoryList.SelectedItem is not GenerationHistoryEntry entry)
            return;

        PromptBox.Text = entry.Prompt;
        GenDialogCheck.IsChecked = entry.GenDialog;
        GenLayoutCheck.IsChecked = entry.GenLayout;
        GenScriptsCheck.IsChecked = entry.GenScripts;

        if (!entry.Succeeded)
        {
            _latestResult = null;
            OutputBox.Text = entry.RawJson;
            IssuesList.Items.Clear();
            PreviewSummaryBox.Clear();
            ApplyButton.IsEnabled = false;
            RefineButton.IsEnabled = false;
            RefineBox.IsEnabled = false;
            StatusLabel.Text = $"Failed run: {entry.Error}";
            return;
        }

        // Re-analyse against the current field so the validation reflects what is open now.
        var result = SceneGenerationService.FromJson(entry.RawJson, _field);
        if (!result.Ok)
        {
            StatusLabel.Text = $"Could not load this entry: {result.Error}";
            return;
        }

        _latestPrompt = new ScenePrompt
        {
            UserText = entry.Prompt,
            GenDialog = entry.GenDialog,
            GenLayout = entry.GenLayout,
            GenScripts = entry.GenScripts,
        };
        ShowResult(result);
        StatusLabel.Text = $"Restored generation from {entry.CreatedUtc.ToLocalTime():g}.";
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Delete all saved generations?", "History",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            _history.Clear();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            StatusLabel.Text = $"Could not clear history: {ex.Message}";
            return;
        }

        RefreshHistory();
        StatusLabel.Text = "History cleared.";
    }

    // -----------------------------------------------------------------------
    // Apply / cancel
    // -----------------------------------------------------------------------

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_latestResult?.Plan == null || _field == null)
        {
            StatusLabel.Text = "Generate a valid scene plan before applying.";
            return;
        }

        if (_latestResult.Validation?.HasErrors == true)
        {
            StatusLabel.Text = "Resolve validation errors before applying.";
            ApplyButton.IsEnabled = false;
            return;
        }

        PlanApplying?.Invoke();
        var apply = ScenePlanMapper.ApplyToField(
            _latestResult.Plan,
            _field,
            new ApplyOptions { PreviewOnly = false, WalkmeshMode = SelectedWalkmeshMode(), ScriptMode = SelectedScriptMode() });
        if (!apply.Ok)
        {
            StatusLabel.Text = $"Apply failed: {apply.Error}";
            return;
        }

        PreviewSummaryBox.Text = apply.Summary;
        ApplyButton.IsEnabled = false;
        PlanApplied?.Invoke(apply);
        StatusLabel.Text = $"Applied preview to field model as {apply.GroupName}. Use Save in the main window to write the archive (a backup is kept).";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _activeService?.CancelAll();
        StatusLabel.Text = "Cancelling...";
    }

    private static string BuildFieldContextText(Field? field)
    {
        if (field == null)
            return "Field context: none";

        var parts = new List<string> { $"Field context: {field.Name}" };
        if (field is FieldPC pcField)
            parts.Add($"{pcField.Data.Length:N0} bytes, {pcField.Sections.Count} sections");
        if (field.ScriptsAndTexts != null)
            parts.Add($"{field.ScriptsAndTexts.GrpScriptCount} groups, {field.ScriptsAndTexts.TextCount} texts");
        if (field.Walkmesh != null)
            parts.Add($"{field.Walkmesh.Triangles.Count} walkmesh triangles");
        if (field.ModelLoader != null)
            parts.Add($"{field.ModelLoader.Models.Count} models");

        return string.Join("; ", parts);
    }
}

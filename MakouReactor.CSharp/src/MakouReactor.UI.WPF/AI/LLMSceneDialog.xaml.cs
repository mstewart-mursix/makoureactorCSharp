using System;
using System.Windows;

using MakouReactor.AI;
using MakouReactor.AI.Mapping;
using MakouReactor.Core.Models;
using MakouReactor.Core.Services;

namespace MakouReactor.UI.WPF.AI;

/// <summary>
/// Scene generation tool hosted from the main Makou Reactor shell.
/// </summary>
public partial class LLMSceneDialog : Window
{
    private readonly LLMConfig _config;
    private readonly Field? _field;
    private SceneGenerationService? _activeService;
    private SceneGenerationResult? _latestResult;

    public event Action? PlanApplying;
    public event Action<ApplyResult>? PlanApplied;

    public LLMSceneDialog(Field? field = null)
    {
        InitializeComponent();
        _field = field;
        _config = LoadConfiguredBackend();
        BackendLabel.Text = _config.Backend == "http"
            ? $"Backend: http ({_config.Endpoint})"
            : $"Backend: codex (subscription{(string.IsNullOrEmpty(_config.CodexModel) ? "" : $", model {_config.CodexModel}")})";
        FieldContextLabel.Text = BuildFieldContextText(field);
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

        GenerateButton.IsEnabled = false;
        ApplyButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        GenerationProgress.Visibility = Visibility.Visible;
        OutputBox.Clear();
        PreviewSummaryBox.Clear();
        IssuesList.Items.Clear();
        _latestResult = null;
        StatusLabel.Text = $"Generating via '{_config.Backend}' backend...";

        var service = new SceneGenerationService(_config);
        _activeService = service;

        try
        {
            var result = await service.GenerateAsync(prompt, _field);

            if (!result.Ok)
            {
                StatusLabel.Text = $"Failed: {result.Error}";
                OutputBox.Text = result.RawJson;
                return;
            }

            OutputBox.Text = result.RawJson;
            _latestResult = result;

            foreach (var issue in result.Validation!.Issues)
                IssuesList.Items.Add(issue.ToString());

            var preview = ScenePlanMapper.ApplyToField(
                result.Plan!,
                _field,
                new ApplyOptions { PreviewOnly = true });
            PreviewSummaryBox.Text = preview.Ok
                ? preview.Summary
                : preview.Error;
            ApplyButton.IsEnabled = !result.Validation.HasErrors && _field != null;

            StatusLabel.Text = result.Validation.HasErrors
                ? $"Generated with validation errors ({result.Validation.Issues.Count} issue(s))"
                : $"Done - {result.Plan!.Actors.Count} actors, {result.Plan.Dialog.Count} dialog lines, {result.Plan.Events.Count} events";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Unexpected error: {ex.Message}";
        }
        finally
        {
            _activeService = null;
            service.Dispose();
            GenerateButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            GenerationProgress.Visibility = Visibility.Collapsed;
        }
    }

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
            new ApplyOptions { PreviewOnly = false });
        if (!apply.Ok)
        {
            StatusLabel.Text = $"Apply failed: {apply.Error}";
            return;
        }

        PreviewSummaryBox.Text = apply.Summary;
        ApplyButton.IsEnabled = false;
        PlanApplied?.Invoke(apply);
        StatusLabel.Text = $"Applied preview to field model as {apply.GroupName}. Use Save in the main window to write the archive.";
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

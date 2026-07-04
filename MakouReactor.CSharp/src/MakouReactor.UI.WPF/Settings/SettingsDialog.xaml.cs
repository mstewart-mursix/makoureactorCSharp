using System.Windows;
using System.Windows.Controls;

using MakouReactor.Core.Services;

using Microsoft.Win32;

namespace MakouReactor.UI.WPF.Settings;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        LoadSettings();
    }

    private void LoadSettings()
    {
        Ff7ExecutableBox.Text = _settings.Ff7ExecutablePath ?? string.Empty;
        Ff7DataPathBox.Text = _settings.Ff7DataPath ?? string.Empty;
        JapaneseTextCheckBox.IsChecked = _settings.JapaneseText;
        FieldListCheckBox.IsChecked = _settings.Window.FieldListVisible;
        PreviewCheckBox.IsChecked = _settings.Window.PreviewVisible;
        SelectPreviewMode(_settings.Window.PreviewMode);
        SelectLanguage(_settings.Language);

        BackendComboBox.SelectedIndex = _settings.Llm.Backend.Equals("http", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        CodexExecutableBox.Text = _settings.Llm.CodexExecutable;
        CodexModelBox.Text = _settings.Llm.CodexModel;
        TimeoutBox.Text = _settings.Llm.TimeoutMs.ToString();
        RepairAttemptsBox.Text = _settings.Llm.RepairAttempts.ToString();
        EndpointBox.Text = _settings.Llm.Endpoint;
        ModelBox.Text = _settings.Llm.Model;
        ApiKeyBox.Text = _settings.Llm.ApiKey;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildLlmSettings(out var llmSettings))
        {
            MessageBox.Show(this,
                "Timeout and repair attempts must be non-negative integers.",
                "Configuration",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _settings.Ff7ExecutablePath = EmptyToNull(Ff7ExecutableBox.Text);
        _settings.Ff7DataPath = EmptyToNull(Ff7DataPathBox.Text);
        _settings.JapaneseText = JapaneseTextCheckBox.IsChecked == true;
        _settings.Window.FieldListVisible = FieldListCheckBox.IsChecked == true;
        _settings.Window.PreviewVisible = PreviewCheckBox.IsChecked == true;
        _settings.Window.PreviewMode = SelectedPreviewMode();
        _settings.Language = SelectedLanguage();

        _settings.Llm = llmSettings;

        DialogResult = true;
    }

    private void DoctorLlmSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildLlmSettings(out var llmSettings))
        {
            LlmDoctorStatusText.Text = "Invalid timeout.";
            MessageBox.Show(this,
                "Timeout must be a non-negative integer.",
                "LLM Settings Doctor",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var diagnostics = LlmSettingsDoctor.Diagnose(llmSettings);
        var hasErrors = diagnostics.Any(d => d.Severity == LlmSettingsDiagnosticSeverity.Error);
        var hasWarnings = diagnostics.Any(d => d.Severity == LlmSettingsDiagnosticSeverity.Warning);
        LlmDoctorStatusText.Text = hasErrors
            ? "Errors found."
            : hasWarnings
                ? "Warnings found."
                : "Looks valid.";

        MessageBox.Show(this,
            string.Join(Environment.NewLine, diagnostics.Select(FormatDiagnostic)),
            "LLM Settings Doctor",
            MessageBoxButton.OK,
            hasErrors ? MessageBoxImage.Error : hasWarnings ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void BrowseFf7Executable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select FF7 executable",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
            Ff7ExecutableBox.Text = dialog.FileName;
    }

    private void SelectLanguage(string language)
    {
        foreach (var item in LanguageComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag?.ToString()?.Equals(language, StringComparison.OrdinalIgnoreCase) == true)
            {
                LanguageComboBox.SelectedItem = item;
                return;
            }
        }

        LanguageComboBox.SelectedIndex = 0;
    }

    private string SelectedLanguage() =>
        LanguageComboBox.SelectedItem is ComboBoxItem item
            ? item.Tag?.ToString() ?? "en"
            : "en";

    private void SelectPreviewMode(string previewMode)
    {
        foreach (var item in PreviewModeComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag?.ToString()?.Equals(previewMode, StringComparison.OrdinalIgnoreCase) == true)
            {
                PreviewModeComboBox.SelectedItem = item;
                return;
            }
        }

        PreviewModeComboBox.SelectedIndex = 0;
    }

    private string SelectedPreviewMode() =>
        PreviewModeComboBox.SelectedItem is ComboBoxItem item
            ? item.Tag?.ToString() ?? "Background"
            : "Background";

    private bool TryBuildLlmSettings(out LlmAppSettings settings)
    {
        settings = new LlmAppSettings();
        if (!int.TryParse(TimeoutBox.Text.Trim(), out var timeoutMs) || timeoutMs < 0)
            return false;
        if (!int.TryParse(RepairAttemptsBox.Text.Trim(), out var repairAttempts) || repairAttempts < 0)
            return false;

        settings.Backend = (BackendComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "codex";
        settings.CodexExecutable = string.IsNullOrWhiteSpace(CodexExecutableBox.Text)
            ? "codex"
            : CodexExecutableBox.Text.Trim();
        settings.CodexModel = CodexModelBox.Text.Trim();
        settings.TimeoutMs = timeoutMs;
        settings.RepairAttempts = repairAttempts;
        settings.Endpoint = EndpointBox.Text.Trim();
        settings.Model = ModelBox.Text.Trim();
        settings.ApiKey = ApiKeyBox.Text.Trim();
        return true;
    }

    private static string FormatDiagnostic(LlmSettingsDiagnostic diagnostic) =>
        $"{diagnostic.Severity}: {diagnostic.Message}";

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

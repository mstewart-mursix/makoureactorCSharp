namespace MakouReactor.Core.Services;

public enum LlmSettingsDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record LlmSettingsDiagnostic(
    LlmSettingsDiagnosticSeverity Severity,
    string Message);

public static class LlmSettingsDoctor
{
    public static IReadOnlyList<LlmSettingsDiagnostic> Diagnose(
        LlmAppSettings settings,
        Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        fileExists ??= File.Exists;
        var diagnostics = new List<LlmSettingsDiagnostic>();
        var backend = settings.Backend.Trim();

        if (string.IsNullOrWhiteSpace(backend))
        {
            diagnostics.Add(new LlmSettingsDiagnostic(
                LlmSettingsDiagnosticSeverity.Error,
                "Backend is required."));
        }
        else if (!backend.Equals("codex", StringComparison.OrdinalIgnoreCase)
            && !backend.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new LlmSettingsDiagnostic(
                LlmSettingsDiagnosticSeverity.Error,
                $"Backend '{backend}' is not supported. Use codex or http."));
        }

        if (settings.TimeoutMs < 0)
        {
            diagnostics.Add(new LlmSettingsDiagnostic(
                LlmSettingsDiagnosticSeverity.Error,
                "Timeout must be zero or greater."));
        }
        else if (settings.TimeoutMs == 0)
        {
            diagnostics.Add(new LlmSettingsDiagnostic(
                LlmSettingsDiagnosticSeverity.Info,
                "Timeout is disabled."));
        }

        if (settings.RepairAttempts < 0)
        {
            diagnostics.Add(new LlmSettingsDiagnostic(
                LlmSettingsDiagnosticSeverity.Error,
                "Repair attempts must be zero or greater."));
        }

        if (backend.Equals("codex", StringComparison.OrdinalIgnoreCase))
            DiagnoseCodex(settings, fileExists, diagnostics);
        else if (backend.Equals("http", StringComparison.OrdinalIgnoreCase))
            DiagnoseHttp(settings, diagnostics);

        if (diagnostics.Count == 0)
        {
            diagnostics.Add(new LlmSettingsDiagnostic(
                LlmSettingsDiagnosticSeverity.Info,
                "LLM settings look valid."));
        }

        return diagnostics;
    }

    private static void DiagnoseCodex(
        LlmAppSettings settings,
        Func<string, bool> fileExists,
        List<LlmSettingsDiagnostic> diagnostics)
    {
        var executable = settings.CodexExecutable.Trim();
        if (string.IsNullOrWhiteSpace(executable))
        {
            diagnostics.Add(new LlmSettingsDiagnostic(
                LlmSettingsDiagnosticSeverity.Error,
                "Codex executable is required for the codex backend."));
            return;
        }

        if (LooksLikePath(executable) && !fileExists(executable))
        {
            diagnostics.Add(new LlmSettingsDiagnostic(
                LlmSettingsDiagnosticSeverity.Warning,
                $"Codex executable was not found: {executable}"));
        }

        if (string.IsNullOrWhiteSpace(settings.CodexModel))
        {
            diagnostics.Add(new LlmSettingsDiagnostic(
                LlmSettingsDiagnosticSeverity.Info,
                "Codex model is blank; the Codex CLI default model will be used."));
        }
    }

    private static void DiagnoseHttp(
        LlmAppSettings settings,
        List<LlmSettingsDiagnostic> diagnostics)
    {
        if (!Uri.TryCreate(settings.Endpoint.Trim(), UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            diagnostics.Add(new LlmSettingsDiagnostic(
                LlmSettingsDiagnosticSeverity.Error,
                "HTTP endpoint must be an absolute http or https URL."));
        }

        if (string.IsNullOrWhiteSpace(settings.Model))
        {
            diagnostics.Add(new LlmSettingsDiagnostic(
                LlmSettingsDiagnosticSeverity.Error,
                "HTTP model is required for the http backend."));
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            diagnostics.Add(new LlmSettingsDiagnostic(
                LlmSettingsDiagnosticSeverity.Warning,
                "HTTP API key is blank. This is only valid for local servers that do not require a key."));
        }
    }

    private static bool LooksLikePath(string value) =>
        value.Contains('\\', StringComparison.Ordinal)
        || value.Contains('/', StringComparison.Ordinal)
        || Path.IsPathFullyQualified(value);
}

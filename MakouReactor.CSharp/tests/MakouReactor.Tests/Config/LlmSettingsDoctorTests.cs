using FluentAssertions;

using MakouReactor.Core.Services;

using Xunit;

namespace MakouReactor.Tests.Config;

public sealed class LlmSettingsDoctorTests
{
    [Fact]
    public void codex_backend_accepts_path_executable_that_exists()
    {
        var settings = new LlmAppSettings
        {
            Backend = "codex",
            CodexExecutable = "C:\\tools\\codex.cmd",
            CodexModel = "gpt-5-codex",
            TimeoutMs = 30000,
        };

        var diagnostics = LlmSettingsDoctor.Diagnose(settings, path => path == settings.CodexExecutable);

        diagnostics.Should().ContainSingle()
            .Which.Message.Should().Be("LLM settings look valid.");
    }

    [Fact]
    public void codex_backend_warns_when_path_executable_is_missing()
    {
        var settings = new LlmAppSettings
        {
            Backend = "codex",
            CodexExecutable = "C:\\missing\\codex.cmd",
        };

        var diagnostics = LlmSettingsDoctor.Diagnose(settings, _ => false);

        diagnostics.Should().Contain(d =>
            d.Severity == LlmSettingsDiagnosticSeverity.Warning
            && d.Message.Contains("was not found", StringComparison.Ordinal));
    }

    [Fact]
    public void http_backend_requires_absolute_http_endpoint_and_model()
    {
        var settings = new LlmAppSettings
        {
            Backend = "http",
            Endpoint = "localhost:1234",
            Model = string.Empty,
        };

        var diagnostics = LlmSettingsDoctor.Diagnose(settings);

        diagnostics.Should().Contain(d =>
            d.Severity == LlmSettingsDiagnosticSeverity.Error
            && d.Message.Contains("endpoint", StringComparison.OrdinalIgnoreCase));
        diagnostics.Should().Contain(d =>
            d.Severity == LlmSettingsDiagnosticSeverity.Error
            && d.Message.Contains("model", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void unsupported_backend_is_an_error()
    {
        var diagnostics = LlmSettingsDoctor.Diagnose(new LlmAppSettings { Backend = "other" });

        diagnostics.Should().Contain(d =>
            d.Severity == LlmSettingsDiagnosticSeverity.Error
            && d.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void negative_repair_attempts_is_an_error()
    {
        var diagnostics = LlmSettingsDoctor.Diagnose(new LlmAppSettings { RepairAttempts = -1 });

        diagnostics.Should().Contain(d =>
            d.Severity == LlmSettingsDiagnosticSeverity.Error
            && d.Message.Contains("Repair attempts", StringComparison.OrdinalIgnoreCase));
    }
}

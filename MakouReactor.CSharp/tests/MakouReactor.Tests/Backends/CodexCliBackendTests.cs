using System;
using System.IO;
using System.Threading.Tasks;

using FluentAssertions;

using MakouReactor.AI.Backends;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Backends;

/// <summary>
/// Tests the Codex CLI backend against fake .cmd scripts that emulate
/// `codex exec` behaviors (success, fenced output, failure, hang).
/// Windows-only: the fakes are batch files.
/// </summary>
public class CodexCliBackendTests : IDisposable
{
    private const string SceneJson = "{\"meta\":{\"title\":\"Test\",\"model\":\"fake\",\"version\":\"1.0\"}}";

    private readonly string _dir;

    public CodexCliBackendTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mr_codex_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string WriteFake(string name, string body)
    {
        var path = Path.Combine(_dir, name + ".cmd");
        File.WriteAllText(path, body);
        return path;
    }

    private static LLMRequest Request(int timeoutMs = 30_000) => new()
    {
        SystemPrompt = "system",
        UserPrompt = "user",
        TimeoutMs = timeoutMs
    };

    [Fact]
    public async Task missing_executable_reports_not_found()
    {
        var backend = new CodexCliBackend("mr_definitely_not_a_real_executable");

        var result = await backend.RequestScenePlanAsync(Request());

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task success_via_output_last_message_file()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Emulates codex exec: writes the final message to the --output-last-message path.
        var fake = WriteFake("codex_ok", """
            @echo off
            :loop
            if "%~1"=="" goto end
            if "%~1"=="--output-last-message" (
              >"%~2" echo {"meta":{"title":"Test","model":"fake","version":"1.0"}}
              goto end
            )
            shift
            goto loop
            :end
            exit /b 0
            """);

        var backend = new CodexCliBackend(fake);
        var result = await backend.RequestScenePlanAsync(Request());

        result.Ok.Should().BeTrue(result.Error);
        result.Json.Should().NotBeNull();
        result.Json!.RootElement.GetProperty("meta").GetProperty("title").GetString().Should().Be("Test");
    }

    [Fact]
    public async Task success_via_stdout_fallback()
    {
        if (!OperatingSystem.IsWindows()) return;

        var fake = WriteFake("codex_stdout", $"""
            @echo off
            echo {SceneJson}
            exit /b 0
            """);

        var backend = new CodexCliBackend(fake);
        var result = await backend.RequestScenePlanAsync(Request());

        result.Ok.Should().BeTrue(result.Error);
        result.Json.Should().NotBeNull();
    }

    [Fact]
    public async Task fenced_output_is_unwrapped()
    {
        if (!OperatingSystem.IsWindows()) return;

        var fake = WriteFake("codex_fenced", $"""
            @echo off
            echo ```json
            echo {SceneJson}
            echo ```
            exit /b 0
            """);

        var backend = new CodexCliBackend(fake);
        var result = await backend.RequestScenePlanAsync(Request());

        result.Ok.Should().BeTrue(result.Error);
        result.Json.Should().NotBeNull();
    }

    [Fact]
    public async Task nonzero_exit_surfaces_stderr()
    {
        if (!OperatingSystem.IsWindows()) return;

        var fake = WriteFake("codex_fail", """
            @echo off
            echo not logged in 1>&2
            exit /b 3
            """);

        var backend = new CodexCliBackend(fake);
        var result = await backend.RequestScenePlanAsync(Request());

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("exited with code 3");
        result.Error.Should().Contain("not logged in");
    }

    [Fact]
    public async Task non_json_output_reports_error()
    {
        if (!OperatingSystem.IsWindows()) return;

        var fake = WriteFake("codex_prose", """
            @echo off
            echo Sorry, I cannot help with that.
            exit /b 0
            """);

        var backend = new CodexCliBackend(fake);
        var result = await backend.RequestScenePlanAsync(Request());

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("not valid JSON");
    }

    [Fact]
    public async Task hanging_process_is_killed_on_timeout()
    {
        if (!OperatingSystem.IsWindows()) return;

        var fake = WriteFake("codex_hang", """
            @echo off
            ping -n 30 127.0.0.1 >nul
            exit /b 0
            """);

        var backend = new CodexCliBackend(fake);
        var result = await backend.RequestScenePlanAsync(Request(timeoutMs: 300));

        result.Ok.Should().BeFalse();
        result.Error.Should().Be("Request timed out");
    }

    [Fact]
    public void resolve_executable_finds_codex_style_shims_on_path()
    {
        if (!OperatingSystem.IsWindows()) return;

        // A bare name with no match anywhere on PATH resolves to null.
        CodexCliBackend.ResolveExecutable("mr_definitely_not_a_real_executable").Should().BeNull();

        // An explicit existing path resolves to itself.
        var fake = WriteFake("codex_resolve", "@echo off");
        CodexCliBackend.ResolveExecutable(fake).Should().Be(fake);
    }

    [Theory]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    [InlineData("```json\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("Here is the plan:\n{\"a\":1}\nHope that helps!", "{\"a\":1}")]
    public void extract_json_handles_wrappers(string input, string expected)
    {
        CodexCliBackend.ExtractJson(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("no json here")]
    [InlineData("[1,2,3]")]
    [InlineData("{broken")]
    public void extract_json_rejects_non_objects(string input)
    {
        CodexCliBackend.ExtractJson(input).Should().BeNull();
    }
}

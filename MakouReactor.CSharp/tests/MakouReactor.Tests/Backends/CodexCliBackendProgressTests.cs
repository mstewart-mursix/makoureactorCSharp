using System.Runtime.InteropServices;

using FluentAssertions;

using MakouReactor.AI.Backends;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Backends;

/// <summary>
/// Runs <see cref="CodexCliBackend"/> against a tiny fake <c>codex</c> (a .cmd on Windows, a shell script
/// elsewhere) so the real process path - stdin, --output-last-message, stderr progress - is exercised.
/// </summary>
public sealed class CodexCliBackendProgressTests : IDisposable
{
    private const string SceneJson = "{\"meta\":{\"title\":\"Test\",\"model\":\"fake\",\"version\":\"1.0\"}}";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mr_codex_progress_" + Guid.NewGuid().ToString("N"));

    public CodexCliBackendProgressTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private sealed class Collect : IProgress<LLMProgressEvent>
    {
        private readonly List<LLMProgressEvent> _events = new();
        public IReadOnlyList<LLMProgressEvent> Events { get { lock (_events) return _events.ToArray(); } }
        public void Report(LLMProgressEvent value) { lock (_events) _events.Add(value); }
    }

    private static LLMRequest Request() => new() { SystemPrompt = "system", UserPrompt = "user", TimeoutMs = 30_000 };

    /// <summary>Fake codex: prints two progress lines on stderr, then writes the answer to --output-last-message.</summary>
    private string WriteFake(string answer, bool succeed = true)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var cmd = Path.Combine(_dir, "codex_fake.cmd");
            File.WriteAllText(cmd, $$"""
                @echo off
                echo thinking about the scene 1>&2
                echo drafting dialogue 1>&2
                :loop
                if "%~1"=="" goto end
                if "%~1"=="--output-last-message" (
                  >"%~2" echo {{answer}}
                  goto end
                )
                shift
                goto loop
                :end
                exit /b {{(succeed ? 0 : 1)}}
                """);
            return cmd;
        }

        var sh = Path.Combine(_dir, "codex_fake.sh");
        File.WriteAllText(sh, $$"""
            #!/bin/sh
            cat >/dev/null
            echo "thinking about the scene" >&2
            echo "drafting dialogue" >&2
            out=""
            while [ $# -gt 0 ]; do
              if [ "$1" = "--output-last-message" ]; then out="$2"; fi
              shift
            done
            printf '%s' '{{answer}}' > "$out"
            exit {{(succeed ? 0 : 1)}}
            """.Replace("\r\n", "\n"));
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    [Fact]
    public async Task stderr_lines_are_forwarded_as_progress_and_the_answer_is_returned()
    {
        var backend = new CodexCliBackend(WriteFake(SceneJson));
        var progress = new Collect();

        var result = await backend.RequestScenePlanAsync(Request(), progress);

        result.Ok.Should().BeTrue(result.Error);
        result.Json!.RootElement.GetProperty("meta").GetProperty("title").GetString().Should().Be("Test");
        progress.Events.First().Phase.Should().Be(LLMProgressPhase.Waiting);
        progress.Events.Where(e => e.Phase == LLMProgressPhase.Responding).Select(e => e.Detail)
            .Should().Equal("thinking about the scene", "drafting dialogue");
    }

    [Fact]
    public async Task plain_overload_still_works_without_a_progress_sink()
    {
        var backend = new CodexCliBackend(WriteFake(SceneJson));

        var result = await backend.RequestScenePlanAsync(Request());

        result.Ok.Should().BeTrue(result.Error);
    }

    [Fact]
    public async Task non_json_answer_is_a_failure_that_still_carries_the_model_text()
    {
        var backend = new CodexCliBackend(WriteFake("Sure thing - here is your scene"));

        var result = await backend.RequestScenePlanAsync(Request());

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("not valid JSON");
        System.Text.Encoding.UTF8.GetString(result.Raw).Should().Contain("here is your scene");
    }

    [Fact]
    public async Task non_zero_exit_reports_the_failure_with_stderr_detail()
    {
        var backend = new CodexCliBackend(WriteFake(SceneJson, succeed: false));

        var result = await backend.RequestScenePlanAsync(Request());

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("exited with code 1").And.Contain("drafting dialogue");
    }
}

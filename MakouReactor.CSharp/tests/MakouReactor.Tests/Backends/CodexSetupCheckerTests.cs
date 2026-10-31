using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using FluentAssertions;

using MakouReactor.AI.Backends;

using Xunit;

namespace MakouReactor.Tests.Backends;

/// <summary>
/// Exercises <see cref="CodexSetupChecker"/> against fake codex scripts: <c>.cmd</c> on Windows,
/// executable <c>.sh</c> elsewhere, so the check runs for real on both CI platforms.
/// Never calls the real codex CLI.
/// </summary>
public sealed class CodexSetupCheckerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mr_codexcheck_" + Guid.NewGuid().ToString("N"));

    public CodexSetupCheckerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>Write a fake codex that prints <paramref name="message"/> and exits with <paramref name="exitCode"/>.</summary>
    private string FakeCodex(string message, int exitCode, int sleepSeconds = 0)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(_dir, "codex.cmd");
            var sleep = sleepSeconds > 0 ? $"ping -n {sleepSeconds + 1} 127.0.0.1 >nul\r\n" : string.Empty;
            File.WriteAllText(path, $"@echo off\r\n{sleep}echo {message}\r\nexit /b {exitCode}\r\n");
            return path;
        }
        else
        {
            var path = Path.Combine(_dir, "codex.sh");
            var sleep = sleepSeconds > 0 ? $"sleep {sleepSeconds}\n" : string.Empty;
            File.WriteAllText(path, $"#!/bin/sh\n[ \"$1 $2\" = \"login status\" ] || exit 9\n{sleep}echo \"{message}\"\nexit {exitCode}\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return path;
        }
    }

    [Fact]
    public async Task missing_executable_is_not_installed_with_install_hint()
    {
        var status = await CodexSetupChecker.CheckAsync("mr_definitely_not_a_real_codex");

        status.State.Should().Be(CodexSetupState.NotInstalled);
        status.Detail.Should().Contain(CodexSetupChecker.InstallCommand);
        status.ResolvedPath.Should().BeNull();
    }

    [Fact]
    public async Task exit_zero_means_ready()
    {
        var fake = FakeCodex("Logged in using ChatGPT", 0);

        var status = await CodexSetupChecker.CheckAsync(fake);

        status.State.Should().Be(CodexSetupState.Ready);
        status.Detail.Should().Be("Logged in using ChatGPT");
        status.ResolvedPath.Should().Be(fake);
    }

    [Fact]
    public async Task nonzero_exit_means_not_logged_in()
    {
        var status = await CodexSetupChecker.CheckAsync(FakeCodex("Not logged in", 1));

        status.State.Should().Be(CodexSetupState.NotLoggedIn);
        status.Detail.Should().Contain("Not logged in").And.Contain("codex login");
    }

    [Fact]
    public async Task slow_check_times_out_without_blocking()
    {
        var fake = FakeCodex("Logged in", 0, sleepSeconds: 20);
        var sw = Stopwatch.StartNew();

        var status = await CodexSetupChecker.CheckAsync(fake, TimeSpan.FromMilliseconds(500));

        status.State.Should().Be(CodexSetupState.CheckFailed);
        status.Detail.Should().Contain("timed out");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void login_start_info_uses_shell_execute()
    {
        var fake = FakeCodex("x", 0);

        var psi = CodexSetupChecker.BuildLoginStartInfo(fake);

        psi.Should().NotBeNull();
        psi!.UseShellExecute.Should().BeTrue();
        psi.ArgumentList.Should().EndWith("login");
        CodexSetupChecker.BuildLoginStartInfo("mr_definitely_not_a_real_codex").Should().BeNull();
    }
}

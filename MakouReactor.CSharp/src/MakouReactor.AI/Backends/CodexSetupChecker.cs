using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MakouReactor.AI.Backends;

/// <summary>Outcome category of <see cref="CodexSetupChecker.CheckAsync"/>.</summary>
public enum CodexSetupState
{
    /// <summary>The Codex CLI could not be found.</summary>
    NotInstalled,

    /// <summary>The CLI runs but <c>codex login status</c> reports no valid login.</summary>
    NotLoggedIn,

    /// <summary>The CLI is installed and logged in.</summary>
    Ready,

    /// <summary>The check itself failed (could not start, timed out, cancelled).</summary>
    CheckFailed,
}

/// <summary>Result of a Codex CLI setup check.</summary>
/// <param name="State">What was found.</param>
/// <param name="Detail">One-line, user-facing explanation (CLI output where available).</param>
/// <param name="ResolvedPath">Resolved executable path, when found.</param>
public sealed record CodexSetupStatus(CodexSetupState State, string Detail, string? ResolvedPath);

/// <summary>
/// Checks whether the Codex CLI is installed and logged in by running <c>codex login status</c>
/// (exit code 0 = logged in). Never throws for process problems; never blocks longer than the timeout.
/// </summary>
public static class CodexSetupChecker
{
    /// <summary>Command that installs the Codex CLI.</summary>
    public const string InstallCommand = "npm i -g @openai/codex";

    /// <summary>Default time allowed for <c>codex login status</c>.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Run the check for <paramref name="executable"/> (a path or a name resolved via PATH).</summary>
    public static async Task<CodexSetupStatus> CheckAsync(string executable, TimeSpan? timeout = null,
                                                          CancellationToken cancellationToken = default)
    {
        var resolved = CodexCliBackend.ResolveExecutable(executable);
        if (resolved == null)
            return new(CodexSetupState.NotInstalled,
                $"Codex CLI '{executable}' was not found. Install it with '{InstallCommand}', then run 'codex login'.", null);

        var psi = CodexCliBackend.CreateStartInfo(resolved);
        psi.RedirectStandardInput = false;
        psi.ArgumentList.Add("login");
        psi.ArgumentList.Add("status");

        using var proc = new Process { StartInfo = psi };
        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            return new(CodexSetupState.CheckFailed, $"Could not start '{resolved}': {ex.Message}", resolved);
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? DefaultTimeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            CodexCliBackend.TryKill(proc);
            return new(CodexSetupState.CheckFailed,
                cancellationToken.IsCancellationRequested ? "Check was cancelled." : "'codex login status' timed out.", resolved);
        }

        var output = FirstLine((await stdoutTask) + "\n" + (await stderrTask));
        return proc.ExitCode == 0
            ? new(CodexSetupState.Ready, output.Length > 0 ? output : "Logged in.", resolved)
            : new(CodexSetupState.NotLoggedIn,
                output.Length > 0 ? $"{output} Run 'codex login' to sign in." : "Not logged in. Run 'codex login' to sign in.",
                resolved);
    }

    /// <summary>
    /// Start info that opens <c>codex login</c> in a visible terminal window (shell execute), for
    /// the "Log in" button. Returns null when the executable cannot be resolved.
    /// </summary>
    public static ProcessStartInfo? BuildLoginStartInfo(string executable)
    {
        var resolved = CodexCliBackend.ResolveExecutable(executable);
        if (resolved == null)
            return null;

        var psi = new ProcessStartInfo { UseShellExecute = true };
        if (OperatingSystem.IsWindows())
        {
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.ArgumentList.Add("/k");
            psi.ArgumentList.Add(resolved);
        }
        else
        {
            psi.FileName = resolved;
        }
        psi.ArgumentList.Add("login");
        return psi;
    }

    private static string FirstLine(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                return trimmed.Length > 200 ? trimmed[..200] : trimmed;
        }
        return string.Empty;
    }
}

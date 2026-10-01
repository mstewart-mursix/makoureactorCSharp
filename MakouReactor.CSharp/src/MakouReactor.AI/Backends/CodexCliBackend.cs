using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using MakouReactor.Core.Models;

namespace MakouReactor.AI.Backends;

/// <summary>
/// LLM backend that shells out to the OpenAI Codex CLI (<c>codex exec</c>).
/// Authentication is handled externally by <c>codex login</c> against a ChatGPT
/// subscription — no API key is stored or sent by this backend.
///
/// The prompt is written to stdin; the model's final message is read from a
/// temp file via <c>--output-last-message</c> (falling back to stdout).
/// The sandbox is read-only and the working directory is the temp directory,
/// so Codex cannot touch the user's project.
/// </summary>
public sealed class CodexCliBackend : ILLMBackend
{
    private readonly string _executable;
    private readonly ConcurrentDictionary<CancellationTokenSource, byte> _inFlight = new();

    public CodexCliBackend(string? executable = null)
    {
        _executable = string.IsNullOrWhiteSpace(executable) ? "codex" : executable;
    }

    public Task<LLMRawResult> RequestScenePlanAsync(LLMRequest req) =>
        RequestScenePlanAsync(req, progress: null);

    public async Task<LLMRawResult> RequestScenePlanAsync(LLMRequest req, IProgress<LLMProgressEvent>? progress)
    {
        var cts = new CancellationTokenSource();
        _inFlight.TryAdd(cts, 0);

        var lastMessageFile = Path.Combine(Path.GetTempPath(), $"mr_codex_{Guid.NewGuid():N}.txt");

        try
        {
            if (req.TimeoutMs > 0)
                cts.CancelAfter(req.TimeoutMs);

            var psi = BuildStartInfo(req, lastMessageFile);
            if (psi == null)
                return LLMRawResult.Failure(0, $"Codex CLI not found: '{_executable}'. Install it (npm i -g @openai/codex) and run 'codex login'.");

            using var proc = new Process { StartInfo = psi };

            try
            {
                proc.Start();
            }
            catch (Exception ex)
            {
                return LLMRawResult.Failure(0, $"Failed to start Codex CLI '{_executable}': {ex.Message}");
            }

            // Prompt goes over stdin so it never hits the command line (length/quoting limits).
            var prompt = string.IsNullOrEmpty(req.SystemPrompt)
                ? req.UserPrompt
                : req.SystemPrompt + "\n\n" + req.UserPrompt;

            progress?.Report(new LLMProgressEvent(LLMProgressPhase.Waiting, "Codex CLI started"));

            // Collect both streams in full (stdout may carry the answer; stderr carries the failure detail)
            // and forward each stderr line as progress text. Lines are only displayed, never parsed, so a
            // change in Codex's progress format cannot break generation.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = ReadLinesAsync(proc.StandardError, progress);

            // Feed stdin in the background: a wedged process that never reads stdin
            // must not keep us from reaching the cancellable wait below (killing the
            // process breaks the pipe and unblocks the writer).
            var stdinTask = FeedStdinAsync(proc, prompt);

            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
            {
                TryKill(proc);
                return LLMRawResult.Failure(0, req.TimeoutMs > 0 ? "Request timed out" : "Request was cancelled");
            }

            await stdinTask;
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode != 0)
            {
                var detail = FirstNonEmpty(stderr, stdout).Trim();
                if (detail.Length > 500) detail = detail[..500];
                return LLMRawResult.Failure(0, $"Codex CLI exited with code {proc.ExitCode}: {detail}");
            }

            var lastMessage = File.Exists(lastMessageFile)
                ? await File.ReadAllTextAsync(lastMessageFile)
                : string.Empty;

            var content = FirstNonEmpty(lastMessage, stdout);
            if (string.IsNullOrWhiteSpace(content))
                return LLMRawResult.Failure(0, "Codex CLI produced no output");

            return BuildResult(content);
        }
        catch (Exception ex)
        {
            return LLMRawResult.Failure(0, $"Request failed: {ex.Message}");
        }
        finally
        {
            TryDelete(lastMessageFile);
            _inFlight.TryRemove(cts, out _);
        }
    }

    public void CancelAll()
    {
        foreach (var cts in _inFlight.Keys)
        {
            if (_inFlight.TryRemove(cts, out _))
                cts.Cancel();
        }
    }

    private static async Task<string> ReadLinesAsync(StreamReader reader, IProgress<LLMProgressEvent>? progress)
    {
        var all = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            all.AppendLine(line);
            var detail = line.Trim();
            if (detail.Length == 0)
                continue;

            progress?.Report(new LLMProgressEvent(
                LLMProgressPhase.Responding,
                detail.Length > 200 ? detail[..200] : detail));
        }

        return all.ToString();
    }

    private static async Task FeedStdinAsync(Process proc, string prompt)
    {
        try
        {
            await proc.StandardInput.WriteAsync(prompt);
            proc.StandardInput.Close();
        }
        catch (IOException)
        {
            // Process exited (or was killed) before consuming stdin — the
            // exit-code check reports the real failure.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose() => CancelAll();

    // -----------------------------------------------------------------------
    // Process setup
    // -----------------------------------------------------------------------

    private ProcessStartInfo? BuildStartInfo(LLMRequest req, string lastMessageFile)
    {
        var resolved = ResolveExecutable(_executable);
        if (resolved == null)
            return null;

        var psi = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        };

        // npm installs codex as a .cmd shim on Windows; batch files must run under cmd.exe.
        var isBatch = resolved.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                   || resolved.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        if (isBatch)
        {
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(resolved);
        }
        else
        {
            psi.FileName = resolved;
        }

        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add("--skip-git-repo-check");
        psi.ArgumentList.Add("--color");
        psi.ArgumentList.Add("never");
        psi.ArgumentList.Add("--sandbox");
        psi.ArgumentList.Add("read-only");
        psi.ArgumentList.Add("--output-last-message");
        psi.ArgumentList.Add(lastMessageFile);

        if (!string.IsNullOrEmpty(req.Model))
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(req.Model);
        }

        psi.ArgumentList.Add("-"); // read prompt from stdin

        return psi;
    }

    /// <summary>
    /// Resolve the executable to a launchable path. Absolute/relative paths are
    /// used as-is; bare names are searched on PATH with Windows extensions.
    /// Returns null when nothing is found.
    /// </summary>
    public static string? ResolveExecutable(string executable)
    {
        if (executable.Contains(Path.DirectorySeparatorChar) || executable.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(executable) ? executable : null;

        var hasExtension = !string.IsNullOrEmpty(Path.GetExtension(executable));
        string[] candidates = OperatingSystem.IsWindows() && !hasExtension
            ? [executable + ".exe", executable + ".cmd", executable + ".bat"]
            : [executable];

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var candidate in candidates)
            {
                var full = Path.Combine(dir.Trim(), candidate);
                if (File.Exists(full))
                    return full;
            }
        }

        return null;
    }

    // -----------------------------------------------------------------------
    // Output handling
    // -----------------------------------------------------------------------

    private static LLMRawResult BuildResult(string content)
    {
        var json = ExtractJson(content);
        if (json == null)
        {
            var snippet = content.Trim();
            if (snippet.Length > 200) snippet = snippet[..200];
            return new LLMRawResult
            {
                Ok = false,
                Error = $"Model output was not valid JSON: {snippet}",
                Raw = Encoding.UTF8.GetBytes(content)
            };
        }

        var raw = Encoding.UTF8.GetBytes(json);
        return new LLMRawResult
        {
            Ok = true,
            Raw = raw,
            Json = JsonDocument.Parse(raw)
        };
    }

    /// <summary>
    /// Extract a JSON object from model output that may be wrapped in markdown
    /// code fences or surrounding prose. Returns null when no valid JSON is found.
    /// </summary>
    public static string? ExtractJson(string content)
    {
        var text = content.Trim();

        if (TryParse(text))
            return text;

        // Slice from the first '{' to the last '}' — covers fences and prose.
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        var sliced = text[start..(end + 1)];
        return TryParse(sliced) ? sliced : null;
    }

    private static bool TryParse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string FirstNonEmpty(string a, string b) =>
        !string.IsNullOrWhiteSpace(a) ? a : b;

    private static void TryKill(Process proc)
    {
        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // Process already gone.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Temp file cleanup is best-effort.
        }
    }
}

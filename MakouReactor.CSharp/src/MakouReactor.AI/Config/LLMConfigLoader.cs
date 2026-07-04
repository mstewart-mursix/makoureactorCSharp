using System;
using System.IO;
using System.Text.Json;

using MakouReactor.Core.Models;

namespace MakouReactor.AI.Config;

/// <summary>
/// Loads LLM configuration from <c>llm.config.json</c>.
/// Searches working directory, application directory, and a resource directory.
/// Falls back to sensible defaults when no config file is found.
/// </summary>
public static class LLMConfigLoader
{
    private const string DefaultBackend = "codex";
    private const string DefaultCodexExecutable = "codex";
    private const string DefaultEndpoint = "http://localhost:1234/v1/chat/completions";
    private const string DefaultApiKey = "lm-studio";
    private const string DefaultModel = "qwen/qwen3-coder-30b";

    /// <summary>
    /// Load configuration from the first available candidate path,
    /// or from the explicit <paramref name="overridePath"/> if provided.
    /// </summary>
    public static LLMConfig Load(string? overridePath = null)
    {
        var filePath = overridePath;

        if (string.IsNullOrEmpty(filePath))
            filePath = FindConfigFile();

        if (string.IsNullOrEmpty(filePath))
            return Defaults();

        try
        {
            var data = File.ReadAllBytes(filePath);
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            var backend = root.TryGetProperty("backend", out var be) && be.ValueKind == JsonValueKind.String
                ? be.GetString()!
                : DefaultBackend;

            var codexExecutable = root.TryGetProperty("codex_executable", out var cx) && cx.ValueKind == JsonValueKind.String
                ? cx.GetString()!
                : DefaultCodexExecutable;

            var codexModel = root.TryGetProperty("codex_model", out var cm) && cm.ValueKind == JsonValueKind.String
                ? cm.GetString()!
                : string.Empty;

            var timeoutMs = root.TryGetProperty("timeout_ms", out var tm) && tm.ValueKind == JsonValueKind.Number
                ? tm.GetInt32()
                : 0;

            var endpoint = root.TryGetProperty("endpoint", out var ep) && ep.ValueKind == JsonValueKind.String
                ? ep.GetString()!
                : DefaultEndpoint;

            var apiKey = root.TryGetProperty("api_key", out var ak) && ak.ValueKind == JsonValueKind.String
                ? ak.GetString()!
                : DefaultApiKey;

            var model = root.TryGetProperty("model", out var md) && md.ValueKind == JsonValueKind.String
                ? md.GetString()!
                : DefaultModel;

            // Normalize endpoint: append /chat/completions if it ends with /v1
            if (endpoint.EndsWith("/v1", StringComparison.Ordinal))
                endpoint += "/chat/completions";

            return new LLMConfig
            {
                Backend = backend,
                CodexExecutable = codexExecutable,
                CodexModel = codexModel,
                TimeoutMs = timeoutMs,
                Endpoint = endpoint,
                ApiKey = apiKey,
                Model = model
            };
        }
        catch
        {
            return Defaults();
        }
    }

    private static LLMConfig Defaults() => new()
    {
        Backend = DefaultBackend,
        CodexExecutable = DefaultCodexExecutable,
        Endpoint = DefaultEndpoint,
        ApiKey = DefaultApiKey,
        Model = DefaultModel
    };

    private static string? FindConfigFile()
    {
        var candidates = CandidatePaths();
        foreach (var p in candidates)
        {
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    private static string[] CandidatePaths()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var cwd = Directory.GetCurrentDirectory();

        // Resolve a "resource" directory (same as app dir for now)
        var resourceDir = Path.Combine(appDir, "Resources");

        return new[]
        {
            Path.Combine(cwd, "llm.config.json"),
            Path.Combine(appDir, "llm.config.json"),
            Path.Combine(resourceDir, "llm.config.json")
        };
    }
}

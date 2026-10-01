using System.Text.Json;

namespace MakouReactor.Core.Models;

/// <summary>
/// JSON-based LLM configuration loaded from <c>llm.config.json</c>.
/// Maps to <c>struct LLMConfig</c> in <c>ai/LLMConfig.h</c>.
/// </summary>
public sealed class LLMConfig
{
    /// <summary>Backend transport: "codex" (Codex CLI, ChatGPT subscription) or "http" (OpenAI-compatible endpoint).</summary>
    public string Backend { get; init; } = "codex";

    /// <summary>Path or command name of the Codex CLI executable (resolved via PATH when not absolute).</summary>
    public string CodexExecutable { get; init; } = "codex";

    /// <summary>Model passed to the Codex CLI via -m; empty uses the CLI's configured default.</summary>
    public string CodexModel { get; init; } = string.Empty;

    /// <summary>Request timeout in milliseconds; 0 lets the caller pick a backend-appropriate default.</summary>
    public int TimeoutMs { get; init; }

    /// <summary>
    /// How many times the pipeline re-prompts the model with the exact parse/validation errors
    /// when its output is unusable; 0 disables repair.
    /// </summary>
    public int MaxRepairAttempts { get; init; } = 1;

    // HTTP backend (OpenAI-compatible endpoint, e.g. LM Studio) — kept as an offline fallback.
    public string Endpoint { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// Load configuration from a JSON file.
    /// Searches <paramref name="overridePath"/> first, then the working directory,
    /// then the application directory.
    /// </summary>
    public static LLMConfig Load(string? overridePath = null)
    {
        string[] searchPaths = overridePath is not null
            ? [overridePath]
            : [
                Path.Combine(Directory.GetCurrentDirectory(), "llm.config.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "llm.config.json"),
            ];

        foreach (string path in searchPaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    using JsonDocument doc = JsonDocument.Parse(json);
                    JsonElement root = doc.RootElement;

                    return new LLMConfig
                    {
                        Backend = GetStringOr(root, "backend", "codex"),
                        CodexExecutable = GetStringOr(root, "codex_executable", "codex"),
                        CodexModel = GetStringOr(root, "codex_model", string.Empty),
                        TimeoutMs = root.TryGetProperty("timeout_ms", out var tm) && tm.ValueKind == JsonValueKind.Number ? tm.GetInt32() : 0,
                        MaxRepairAttempts = root.TryGetProperty("max_repair_attempts", out var mr) && mr.ValueKind == JsonValueKind.Number
                            ? Math.Max(0, mr.GetInt32()) : 1,
                        Endpoint = GetStringOr(root, "endpoint", string.Empty),
                        ApiKey = root.TryGetProperty("apiKey", out var ak) ? ak.GetString() ?? string.Empty
                               : GetStringOr(root, "api_key", string.Empty),
                        Model = GetStringOr(root, "model", string.Empty),
                    };
                }
                catch (JsonException ex)
                {
                    Console.Error.WriteLine($"[LLMConfig] Failed to parse {path}: {ex.Message}");
                }
            }
        }

        return new LLMConfig();
    }

    private static string GetStringOr(JsonElement root, string key, string fallback) =>
        root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? fallback
            : fallback;

    public override string ToString() =>
        $"LLMConfig(backend={Backend}, endpoint={Endpoint}, model={Model})";
}

/// <summary>
/// Scene prompt from the UI dialog.
/// Maps to <c>ScenePrompt</c> in <c>ui/LLMSceneDialog.h</c>.
/// </summary>
public sealed class ScenePrompt
{
    public string UserText { get; init; } = string.Empty;
    public bool GenDialog { get; init; } = true;
    public bool GenLayout { get; init; } = true;
    public bool GenScripts { get; init; } = true;
    public double Temperature { get; init; } = 0.6;
    public int MaxTokens { get; init; } = 2048;
    public string ModelName { get; init; } = string.Empty;
    public string EndpointUrl { get; init; } = string.Empty;

    public override string ToString() =>
        $"ScenePrompt(genDialog={GenDialog}, genLayout={GenLayout}, genScripts={GenScripts})";
}

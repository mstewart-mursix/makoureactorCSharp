using System;
using System.Text;
using System.Threading.Tasks;

using MakouReactor.AI.Backends;
using MakouReactor.AI.Layout;
using MakouReactor.AI.Parsing;
using MakouReactor.AI.Prompt;
using MakouReactor.AI.Validation;
using MakouReactor.Core.Models;

namespace MakouReactor.AI;

/// <summary>
/// Result of a full scene generation run: raw model output, parsed plan,
/// layout notes, and validation issues.
/// </summary>
public sealed class SceneGenerationResult
{
    public bool Ok { get; init; }
    public string Error { get; init; } = string.Empty;

    /// <summary>The extracted scene-plan JSON as returned by the model.</summary>
    public string RawJson { get; init; } = string.Empty;

    public ScenePlan? Plan { get; init; }
    public LayoutResult? Layout { get; init; }
    public ValidationResult? Validation { get; init; }

    public static SceneGenerationResult Failure(string error, string rawJson = "") =>
        new() { Ok = false, Error = error, RawJson = rawJson };
}

/// <summary>
/// End-to-end scene generation pipeline:
/// prompt composition → LLM backend → JSON extraction → parse → layout adjust → validate.
/// The backend is chosen from <see cref="LLMConfig.Backend"/> ("codex" by default).
/// </summary>
public sealed class SceneGenerationService : IDisposable
{
    // Codex runs an agent loop, so it needs far more headroom than a plain HTTP completion.
    private const int DefaultCodexTimeoutMs = 300_000;
    private const int DefaultHttpTimeoutMs = 60_000;

    private readonly LLMConfig _config;
    private readonly ILLMBackend _backend;
    private readonly bool _ownsBackend;

    public SceneGenerationService(LLMConfig config, ILLMBackend? backend = null)
    {
        _config = config;
        _ownsBackend = backend == null;
        _backend = backend ?? LLMBackendFactory.Create(config);
    }

    /// <summary>
    /// Compose the <see cref="LLMRequest"/> for a scene prompt without sending it.
    /// </summary>
    public LLMRequest BuildRequest(ScenePrompt prompt, int widthPx = 320, int heightPx = 240)
    {
        var isCodex = _config.Backend != "http";
        var model = isCodex ? _config.CodexModel : _config.Model;

        var timeout = _config.TimeoutMs > 0
            ? _config.TimeoutMs
            : isCodex ? DefaultCodexTimeoutMs : DefaultHttpTimeoutMs;

        return new LLMRequest
        {
            EndpointUrl = _config.Endpoint,
            ApiKey = _config.ApiKey,
            Model = model,
            Temperature = prompt.Temperature,
            MaxTokens = prompt.MaxTokens,
            SystemPrompt = PromptBuilder.SystemPrompt(),
            UserPrompt = PromptBuilder.UserPrompt(prompt, widthPx, heightPx),
            TimeoutMs = timeout
        };
    }

    /// <summary>
    /// Run the full pipeline. Nothing is written to any field; the caller decides
    /// whether to apply the returned plan via <see cref="Mapping.ScenePlanMapper"/>.
    /// </summary>
    public async Task<SceneGenerationResult> GenerateAsync(ScenePrompt prompt, Field? field = null,
                                                           int widthPx = 320, int heightPx = 240)
    {
        var request = BuildRequest(prompt, widthPx, heightPx);

        var raw = await _backend.RequestScenePlanAsync(request);
        if (!raw.Ok)
            return SceneGenerationResult.Failure(raw.Error);

        var jsonBytes = LLMResponseExtractor.ExtractScenePlanJson(raw, out var extractError);
        if (jsonBytes == null)
            return SceneGenerationResult.Failure(extractError);

        var rawJson = Encoding.UTF8.GetString(jsonBytes);

        var parsed = ScenePlanParser.Parse(jsonBytes);
        if (!parsed.Ok)
            return SceneGenerationResult.Failure(parsed.Error, rawJson);

        var layout = LayoutGenerator.Adjust(parsed.Plan, field);
        var validation = ScenePlanValidator.Validate(parsed.Plan, field);

        return new SceneGenerationResult
        {
            Ok = true,
            RawJson = rawJson,
            Plan = parsed.Plan,
            Layout = layout,
            Validation = validation
        };
    }

    /// <summary>Cancel all in-flight requests.</summary>
    public void CancelAll() => _backend.CancelAll();

    public void Dispose()
    {
        if (_ownsBackend)
            _backend.Dispose();
    }
}

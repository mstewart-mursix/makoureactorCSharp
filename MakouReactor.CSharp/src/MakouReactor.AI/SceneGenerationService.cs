using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// One entry per model call: an empty string when that attempt produced a usable plan,
    /// otherwise the errors that triggered a repair (or the final failure).
    /// </summary>
    public IReadOnlyList<string> Attempts { get; init; } = Array.Empty<string>();

    public static SceneGenerationResult Failure(string error, string rawJson = "",
                                                IReadOnlyList<string>? attempts = null) =>
        new() { Ok = false, Error = error, RawJson = rawJson, Attempts = attempts ?? new[] { error } };
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
    public LLMRequest BuildRequest(ScenePrompt prompt, int widthPx = 320, int heightPx = 240, Field? field = null)
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
            UserPrompt = PromptBuilder.UserPrompt(prompt, widthPx, heightPx,
                field is null ? null : FieldContextBuilder.Describe(field)),
            TimeoutMs = timeout
        };
    }

    /// <summary>
    /// Run the full pipeline. Nothing is written to any field; the caller decides
    /// whether to apply the returned plan via <see cref="Mapping.ScenePlanMapper"/>.
    /// When the model's output cannot be extracted/parsed, or the plan has validation
    /// errors, the model is re-prompted with the exact errors up to
    /// <see cref="LLMConfig.MaxRepairAttempts"/> times.
    /// </summary>
    public async Task<SceneGenerationResult> GenerateAsync(ScenePrompt prompt, Field? field = null,
                                                           int widthPx = 320, int heightPx = 240)
    {
        var request = BuildRequest(prompt, widthPx, heightPx, field);
        var maxRepairs = Math.Max(0, _config.MaxRepairAttempts);
        var attempts = new List<string>();

        SceneGenerationResult? last = null;
        for (var attempt = 0; attempt <= maxRepairs; attempt++)
        {
            var outcome = await RunOnceAsync(request, field);
            var problem = outcome.Problem;
            attempts.Add(problem);

            if (outcome.Transport)
                return SceneGenerationResult.Failure(problem, attempts: attempts);

            last = outcome.Result;
            if (problem.Length == 0)
                break;

            if (attempt < maxRepairs)
                request = BuildRepairRequest(request, outcome.PreviousOutput, problem);
        }

        if (last is { Plan: not null })
        {
            // Parsed but still invalid after all repairs: hand back the plan with its issues.
            return new SceneGenerationResult
            {
                Ok = true,
                RawJson = last.RawJson,
                Plan = last.Plan,
                Layout = last.Layout,
                Validation = last.Validation,
                Attempts = attempts,
            };
        }

        return SceneGenerationResult.Failure(attempts[^1], last?.RawJson ?? string.Empty, attempts);
    }

    private sealed record Outcome(SceneGenerationResult? Result, string Problem, string PreviousOutput, bool Transport);

    private async Task<Outcome> RunOnceAsync(LLMRequest request, Field? field)
    {
        var raw = await _backend.RequestScenePlanAsync(request);
        if (!raw.Ok)
            return new Outcome(null, raw.Error, string.Empty, Transport: true);

        var jsonBytes = LLMResponseExtractor.ExtractScenePlanJson(raw, out var extractError);
        if (jsonBytes == null)
        {
            var text = raw.Raw is { Length: > 0 } ? Encoding.UTF8.GetString(raw.Raw) : string.Empty;
            return new Outcome(SceneGenerationResult.Failure(extractError), extractError, text, Transport: false);
        }

        var rawJson = Encoding.UTF8.GetString(jsonBytes);

        var parsed = ScenePlanParser.Parse(jsonBytes);
        if (!parsed.Ok)
            return new Outcome(SceneGenerationResult.Failure(parsed.Error, rawJson), parsed.Error, rawJson, Transport: false);

        var layout = LayoutGenerator.Adjust(parsed.Plan, field);
        var validation = ScenePlanValidator.Validate(parsed.Plan, field);

        var result = new SceneGenerationResult
        {
            Ok = true,
            RawJson = rawJson,
            Plan = parsed.Plan,
            Layout = layout,
            Validation = validation
        };

        var problem = validation.HasErrors
            ? string.Join("\n", validation.Issues.Where(i => i.Level == Severity.Error).Select(i => i.ToString()))
            : string.Empty;
        return new Outcome(result, problem, rawJson, Transport: false);
    }

    /// <summary>
    /// Compose a repair request: the original prompts, the model's previous output and the
    /// exact errors to fix.
    /// </summary>
    internal static LLMRequest BuildRepairRequest(LLMRequest original, string previousOutput, string errors)
    {
        var repairPrompt =
            original.UserPrompt + "\n\n" +
            "Your previous output:\n" + previousOutput + "\n\n" +
            "Fix instructions:\n" + errors + "\n\n" +
            "Return the corrected complete JSON object only.";

        return new LLMRequest
        {
            EndpointUrl = original.EndpointUrl,
            Model = original.Model,
            ApiKey = original.ApiKey,
            Temperature = original.Temperature,
            MaxTokens = original.MaxTokens,
            SystemPrompt = original.SystemPrompt,
            UserPrompt = repairPrompt,
            Stream = original.Stream,
            TimeoutMs = original.TimeoutMs,
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

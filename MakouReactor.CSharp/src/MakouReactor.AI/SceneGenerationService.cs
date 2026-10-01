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
    /// <param name="progress">Optional progress sink (phase changes, repair attempts, backend progress lines).</param>
    public Task<SceneGenerationResult> GenerateAsync(ScenePrompt prompt, Field? field = null,
                                                     int widthPx = 320, int heightPx = 240,
                                                     IProgress<LLMProgressEvent>? progress = null)
    {
        var request = BuildRequest(prompt, widthPx, heightPx, field);
        return RunWithRepairsAsync(request, field, progress);
    }

    /// <summary>
    /// Ask the model to change an existing plan ("make the blacksmith angrier", "add a battle at the end").
    /// The previous plan JSON and the instruction are sent together with the original constraints, and the
    /// result goes through the same parse/validate/repair loop as <see cref="GenerateAsync"/>.
    /// </summary>
    /// <param name="previous">A successful earlier result; its <see cref="SceneGenerationResult.RawJson"/> is refined.</param>
    /// <param name="instruction">What to change, in plain language.</param>
    /// <param name="originalPrompt">The prompt that produced <paramref name="previous"/> (keeps its flags and text).</param>
    public Task<SceneGenerationResult> RefineAsync(SceneGenerationResult previous, string instruction,
                                                   ScenePrompt originalPrompt, Field? field = null,
                                                   int widthPx = 320, int heightPx = 240,
                                                   IProgress<LLMProgressEvent>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(originalPrompt);

        if (string.IsNullOrWhiteSpace(instruction))
            return Task.FromResult(SceneGenerationResult.Failure("Enter what should change in the scene."));
        if (string.IsNullOrWhiteSpace(previous.RawJson))
            return Task.FromResult(SceneGenerationResult.Failure("There is no previous plan to refine."));

        var request = BuildRefineRequest(BuildRequest(originalPrompt, widthPx, heightPx, field), previous.RawJson, instruction);
        return RunWithRepairsAsync(request, field, progress);
    }

    private async Task<SceneGenerationResult> RunWithRepairsAsync(LLMRequest request, Field? field,
                                                                  IProgress<LLMProgressEvent>? progress)
    {
        var maxRepairs = Math.Max(0, _config.MaxRepairAttempts);
        var attempts = new List<string>();
        progress?.Report(new LLMProgressEvent(LLMProgressPhase.Starting));

        SceneGenerationResult? last = null;
        for (var attempt = 0; attempt <= maxRepairs; attempt++)
        {
            var number = attempt + 1;
            progress?.Report(new LLMProgressEvent(LLMProgressPhase.Waiting, string.Empty, number));
            var outcome = await RunOnceAsync(request, field, progress, number);
            var problem = outcome.Problem;
            attempts.Add(problem);

            if (outcome.Transport)
            {
                progress?.Report(new LLMProgressEvent(LLMProgressPhase.Done, problem, number));
                return SceneGenerationResult.Failure(problem, attempts: attempts);
            }

            last = outcome.Result;
            if (problem.Length == 0)
                break;

            if (attempt < maxRepairs)
            {
                progress?.Report(new LLMProgressEvent(LLMProgressPhase.Repairing, FirstLine(problem), number + 1));
                request = BuildRepairRequest(request, outcome.PreviousOutput, problem);
            }
        }

        SceneGenerationResult result;
        if (last is { Plan: not null })
        {
            // Parsed but still invalid after all repairs: hand back the plan with its issues.
            result = new SceneGenerationResult
            {
                Ok = true,
                RawJson = last.RawJson,
                Plan = last.Plan,
                Layout = last.Layout,
                Validation = last.Validation,
                Attempts = attempts,
            };
        }
        else
        {
            result = SceneGenerationResult.Failure(attempts[^1], last?.RawJson ?? string.Empty, attempts);
        }

        progress?.Report(new LLMProgressEvent(LLMProgressPhase.Done, result.Ok ? string.Empty : result.Error, attempts.Count));
        return result;
    }

    private sealed record Outcome(SceneGenerationResult? Result, string Problem, string PreviousOutput, bool Transport);

    private async Task<Outcome> RunOnceAsync(LLMRequest request, Field? field,
                                             IProgress<LLMProgressEvent>? progress, int attempt)
    {
        var raw = await _backend.RequestScenePlanAsync(request, progress);
        if (!raw.Ok)
        {
            // A reply that arrived but was not usable JSON carries the model's text and is worth repairing.
            // Only a failure with nothing from the model (not logged in, timeout, no network) is final.
            if (raw.Raw.Length == 0)
                return new Outcome(null, raw.Error, string.Empty, Transport: true);

            return new Outcome(SceneGenerationResult.Failure(raw.Error), raw.Error,
                Encoding.UTF8.GetString(raw.Raw), Transport: false);
        }

        progress?.Report(new LLMProgressEvent(LLMProgressPhase.Parsing, string.Empty, attempt));
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

        progress?.Report(new LLMProgressEvent(LLMProgressPhase.Validating, string.Empty, attempt));
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

    private static string FirstLine(string text)
    {
        var index = text.IndexOf('\n');
        return index < 0 ? text : text[..index];
    }

    /// <summary>Compose a refine request: original prompts, the current plan JSON and the change to make.</summary>
    internal static LLMRequest BuildRefineRequest(LLMRequest original, string previousJson, string instruction)
    {
        var refinePrompt =
            original.UserPrompt + "\n\n" +
            "Current scene plan JSON:\n" + previousJson + "\n\n" +
            "Refinement request:\n" + instruction.Trim() + "\n\n" +
            "Apply the refinement to the current scene plan, keeping everything else unchanged. " +
            "Return the complete updated JSON object only.";

        return CopyWithUserPrompt(original, refinePrompt);
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

        return CopyWithUserPrompt(original, repairPrompt);
    }

    private static LLMRequest CopyWithUserPrompt(LLMRequest original, string userPrompt) => new()
    {
        EndpointUrl = original.EndpointUrl,
        Model = original.Model,
        ApiKey = original.ApiKey,
        Temperature = original.Temperature,
        MaxTokens = original.MaxTokens,
        SystemPrompt = original.SystemPrompt,
        UserPrompt = userPrompt,
        Stream = original.Stream,
        TimeoutMs = original.TimeoutMs,
    };

    /// <summary>Cancel all in-flight requests.</summary>
    public void CancelAll() => _backend.CancelAll();

    public void Dispose()
    {
        if (_ownsBackend)
            _backend.Dispose();
    }
}

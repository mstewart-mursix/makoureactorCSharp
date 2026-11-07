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
    /// One entry per model call, in order: the first is the original request, any further
    /// entries are automatic repair attempts. Empty for results built outside the pipeline.
    /// </summary>
    public IReadOnlyList<GenerationAttempt> Attempts { get; init; } = Array.Empty<GenerationAttempt>();

    /// <summary>Number of repair re-prompts that were needed (0 when the first answer was accepted).</summary>
    public int RepairCount => Math.Max(0, Attempts.Count - 1);

    public static SceneGenerationResult Failure(string error, string rawJson = "") =>
        new() { Ok = false, Error = error, RawJson = rawJson };

    /// <summary>Copy of this result carrying the given attempt history.</summary>
    internal SceneGenerationResult WithAttempts(IReadOnlyList<GenerationAttempt> attempts) => new()
    {
        Ok = Ok,
        Error = Error,
        RawJson = RawJson,
        Plan = Plan,
        Layout = Layout,
        Validation = Validation,
        Attempts = attempts
    };
}

/// <summary>
/// Outcome of a single model call within <see cref="SceneGenerationService.GenerateAsync"/>.
/// </summary>
public sealed class GenerationAttempt
{
    /// <summary>1-based attempt number (1 = original request, 2+ = repairs).</summary>
    public int Number { get; init; }

    /// <summary>
    /// Blocking problems found in this attempt's response (transport, extraction, parse, or
    /// validation errors). Empty when the response was accepted. Warnings are not listed.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>True when this attempt produced an error-free plan.</summary>
    public bool Accepted => Errors.Count == 0;

    /// <summary>One-line human-readable summary for UI/CLI display.</summary>
    public string Summary => Accepted
        ? $"Attempt {Number}: ok"
        : $"Attempt {Number}: {string.Join("; ", Errors)}";

    public override string ToString() => Summary;
}

/// <summary>
/// End-to-end scene generation pipeline:
/// prompt composition → LLM backend → JSON extraction → parse → layout adjust → validate.
/// The backend is chosen from <see cref="LLMConfig.Backend"/> ("codex" by default).
/// </summary>
public sealed class SceneGenerationService : IDisposable
{
    /// <summary>Upper bound on previous-output characters echoed back in a repair prompt.</summary>
    public const int MaxEchoedOutputChars = 16_000;

    /// <summary>Upper bound on error lines listed in a repair prompt.</summary>
    public const int MaxListedErrors = 30;

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
    /// When <paramref name="field"/> is given, a <see cref="FieldContextBuilder"/> description is
    /// included and, if the field has a walkmesh, the size hint becomes its bounding box.
    /// </summary>
    public LLMRequest BuildRequest(ScenePrompt prompt, int widthPx = 320, int heightPx = 240, Field? field = null)
    {
        string? fieldContext = null;
        if (field != null)
        {
            fieldContext = FieldContextBuilder.Describe(field);
            if (LayoutGenerator.WalkmeshOf(field) != null)
            {
                var bounds = LayoutGenerator.FieldBounds(field);
                widthPx = bounds.Width;
                heightPx = bounds.Height;
            }
        }

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
            UserPrompt = PromptBuilder.UserPrompt(prompt, widthPx, heightPx, fieldContext),
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
    /// <remarks>
    /// When the response cannot be extracted or parsed, or validation reports errors, the model
    /// is re-prompted with its previous output and the exact error messages, up to
    /// <see cref="LLMConfig.MaxRepairAttempts"/> times. Transport failures (including
    /// cancellation) are never retried. If every attempt still has validation errors, the last
    /// parsed plan is returned with <c>Ok = true</c> and its errors in <see cref="SceneGenerationResult.Validation"/>,
    /// matching the behaviour without repair. <see cref="SceneGenerationResult.Attempts"/> records each call.
    /// </remarks>
    /// <param name="prompt">Scene prompt.</param>
    /// <param name="field">Field to describe and validate against.</param>
    /// <param name="widthPx">Fallback width hint when the field has no walkmesh.</param>
    /// <param name="heightPx">Fallback height hint when the field has no walkmesh.</param>
    /// <param name="progress">Optional progress sink (phase changes, repair attempts, backend progress lines).</param>
    public Task<SceneGenerationResult> GenerateAsync(ScenePrompt prompt, Field? field = null,
                                                     int widthPx = 320, int heightPx = 240,
                                                     IProgress<LLMProgressEvent>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return RunWithRepairsAsync(BuildRequest(prompt, widthPx, heightPx, field), field, progress);
    }

    /// <summary>
    /// Ask the model to change an existing plan ("make the blacksmith angrier", "add a battle at the end").
    /// The previous plan JSON and the instruction are sent together with the original constraints, and the
    /// result goes through the same parse/validate/repair loop as <see cref="GenerateAsync"/>.
    /// </summary>
    /// <param name="previous">A successful earlier result; its <see cref="SceneGenerationResult.RawJson"/> is refined.</param>
    /// <param name="instruction">What to change, in plain language.</param>
    /// <param name="originalPrompt">The prompt that produced <paramref name="previous"/> (keeps its flags and text).</param>
    /// <param name="field">Field to describe and validate against.</param>
    /// <param name="widthPx">Fallback width hint when the field has no walkmesh.</param>
    /// <param name="heightPx">Fallback height hint when the field has no walkmesh.</param>
    /// <param name="progress">Optional progress sink.</param>
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

    /// <summary>
    /// Re-analyse stored plan JSON (for example a history entry) against the current field: parse, lay out and
    /// validate, without calling the model.
    /// </summary>
    public static SceneGenerationResult FromJson(string json, Field? field = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return SceneGenerationResult.Failure("No plan JSON to load.");

        var parsed = ScenePlanParser.Parse(Encoding.UTF8.GetBytes(json));
        if (!parsed.Ok)
            return SceneGenerationResult.Failure(parsed.Error, json);

        return new SceneGenerationResult
        {
            Ok = true,
            RawJson = json,
            Plan = parsed.Plan,
            Layout = LayoutGenerator.Adjust(parsed.Plan, field),
            Validation = ScenePlanValidator.Validate(parsed.Plan, field),
        };
    }

    private async Task<SceneGenerationResult> RunWithRepairsAsync(LLMRequest originalRequest, Field? field,
                                                                  IProgress<LLMProgressEvent>? progress)
    {
        progress?.Report(new LLMProgressEvent(LLMProgressPhase.Starting));
        var maxAttempts = 1 + Math.Max(0, _config.MaxRepairAttempts);
        var attempts = new List<GenerationAttempt>();

        var request = originalRequest;
        SceneGenerationResult? last = null;

        for (var number = 1; number <= maxAttempts; number++)
        {
            progress?.Report(new LLMProgressEvent(LLMProgressPhase.Waiting, string.Empty, number));
            var raw = await _backend.RequestScenePlanAsync(request, progress);
            if (!raw.Ok && raw.Raw.Length == 0)
            {
                // Transport failure or cancellation (nothing came back from the model): retrying
                // would not help, and would ignore Cancel.
                attempts.Add(new GenerationAttempt { Number = number, Errors = new[] { raw.Error } });
                var failed = last?.Plan != null
                    ? last.WithAttempts(attempts)
                    : SceneGenerationResult.Failure(raw.Error, last?.RawJson ?? string.Empty).WithAttempts(attempts);
                progress?.Report(new LLMProgressEvent(LLMProgressPhase.Done, raw.Error, number));
                return failed;
            }

            // A reply that arrived but was not usable still carries the model's text: repair it.
            var (result, errors, modelOutput) = raw.Ok
                ? Evaluate(raw, field, progress, number)
                : (SceneGenerationResult.Failure(raw.Error), (IReadOnlyList<string>)new[] { raw.Error },
                   Encoding.UTF8.GetString(raw.Raw));
            attempts.Add(new GenerationAttempt { Number = number, Errors = errors });

            if (errors.Count == 0)
            {
                progress?.Report(new LLMProgressEvent(LLMProgressPhase.Done, string.Empty, number));
                return result.WithAttempts(attempts);
            }

            // Prefer keeping a parsed-but-invalid plan over a later unparseable one.
            if (result.Plan != null || last?.Plan == null)
                last = result;

            if (number < maxAttempts)
            {
                progress?.Report(new LLMProgressEvent(LLMProgressPhase.Repairing, errors[0], number + 1));
                request = BuildRepairRequest(originalRequest, modelOutput, errors);
            }
        }

        var final = last!.WithAttempts(attempts);
        progress?.Report(new LLMProgressEvent(LLMProgressPhase.Done, final.Ok ? string.Empty : final.Error, attempts.Count));
        return final;
    }

    /// <summary>
    /// Compose a repair request: the original prompts, the model's previous output, and a
    /// <c>Fix instructions:</c> block listing the exact errors to correct.
    /// </summary>
    public static LLMRequest BuildRepairRequest(LLMRequest original, string previousOutput,
                                                IReadOnlyList<string> errors)
    {
        var echoed = previousOutput.Length > MaxEchoedOutputChars
            ? previousOutput[..MaxEchoedOutputChars] + "\n...[truncated]"
            : previousOutput;

        var sb = new StringBuilder();
        sb.AppendLine(original.UserPrompt);
        sb.AppendLine();
        sb.AppendLine("Your previous response was rejected. Previous response:");
        sb.AppendLine("```");
        sb.AppendLine(string.IsNullOrWhiteSpace(echoed) ? "(empty response)" : echoed);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Fix instructions:");
        foreach (var error in errors.Take(MaxListedErrors))
            sb.Append("- ").AppendLine(error);
        if (errors.Count > MaxListedErrors)
            sb.Append("- ...and ").Append(errors.Count - MaxListedErrors).AppendLine(" more error(s) of the same kinds.");
        sb.AppendLine();
        sb.Append("Return the corrected complete JSON object only.");

        return new LLMRequest
        {
            EndpointUrl = original.EndpointUrl,
            ApiKey = original.ApiKey,
            Model = original.Model,
            Temperature = original.Temperature,
            MaxTokens = original.MaxTokens,
            SystemPrompt = original.SystemPrompt,
            UserPrompt = sb.ToString(),
            Stream = original.Stream,
            TimeoutMs = original.TimeoutMs
        };
    }

    /// <summary>
    /// Extract, parse, lay out and validate one backend response. Returns the result, the list of
    /// blocking errors (empty when accepted), and the text to echo back in a repair prompt.
    /// </summary>
    private static (SceneGenerationResult Result, IReadOnlyList<string> Errors, string ModelOutput)
        Evaluate(LLMRawResult raw, Field? field, IProgress<LLMProgressEvent>? progress, int attempt)
    {
        progress?.Report(new LLMProgressEvent(LLMProgressPhase.Parsing, string.Empty, attempt));
        var jsonBytes = LLMResponseExtractor.ExtractScenePlanJson(raw, out var extractError);
        if (jsonBytes == null)
        {
            var output = Encoding.UTF8.GetString(raw.Raw);
            return (SceneGenerationResult.Failure(extractError), new[] { extractError }, output);
        }

        var rawJson = Encoding.UTF8.GetString(jsonBytes);

        var parsed = ScenePlanParser.Parse(jsonBytes);
        if (!parsed.Ok)
            return (SceneGenerationResult.Failure(parsed.Error, rawJson), new[] { parsed.Error }, rawJson);

        progress?.Report(new LLMProgressEvent(LLMProgressPhase.Validating, string.Empty, attempt));
        var layout = LayoutGenerator.Adjust(parsed.Plan, field);
        var validation = ScenePlanValidator.Validate(parsed.Plan, field);

        var errors = validation.Issues
            .Where(i => i.Level == Severity.Error)
            .Select(i => string.IsNullOrEmpty(i.Path) ? i.Message : $"{i.Path}: {i.Message}")
            .ToList();

        var result = new SceneGenerationResult
        {
            Ok = true,
            RawJson = rawJson,
            Plan = parsed.Plan,
            Layout = layout,
            Validation = validation
        };
        return (result, errors, rawJson);
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

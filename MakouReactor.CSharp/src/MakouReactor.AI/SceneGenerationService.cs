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
    /// </summary>
    /// <remarks>
    /// When the response cannot be extracted or parsed, or validation reports errors, the model
    /// is re-prompted with its previous output and the exact error messages, up to
    /// <see cref="LLMConfig.MaxRepairAttempts"/> times. Transport failures (including
    /// cancellation) are never retried. If every attempt still has validation errors, the last
    /// parsed plan is returned with <c>Ok = true</c> and its errors in <see cref="SceneGenerationResult.Validation"/>,
    /// matching the behaviour without repair. <see cref="SceneGenerationResult.Attempts"/> records each call.
    /// </remarks>
    public async Task<SceneGenerationResult> GenerateAsync(ScenePrompt prompt, Field? field = null,
                                                           int widthPx = 320, int heightPx = 240)
    {
        var originalRequest = BuildRequest(prompt, widthPx, heightPx, field);
        var maxAttempts = 1 + Math.Max(0, _config.MaxRepairAttempts);
        var attempts = new List<GenerationAttempt>();

        var request = originalRequest;
        SceneGenerationResult? last = null;

        for (var number = 1; number <= maxAttempts; number++)
        {
            var raw = await _backend.RequestScenePlanAsync(request);
            if (!raw.Ok)
            {
                // Transport failure or cancellation: retrying would not help (and would ignore Cancel).
                attempts.Add(new GenerationAttempt { Number = number, Errors = new[] { raw.Error } });
                return last?.Plan != null
                    ? last.WithAttempts(attempts)
                    : SceneGenerationResult.Failure(raw.Error, last?.RawJson ?? string.Empty).WithAttempts(attempts);
            }

            var (result, errors, modelOutput) = Evaluate(raw, field);
            attempts.Add(new GenerationAttempt { Number = number, Errors = errors });

            if (errors.Count == 0)
                return result.WithAttempts(attempts);

            // Prefer keeping a parsed-but-invalid plan over a later unparseable one.
            if (result.Plan != null || last?.Plan == null)
                last = result;

            if (number < maxAttempts)
                request = BuildRepairRequest(originalRequest, modelOutput, errors);
        }

        return last!.WithAttempts(attempts);
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
        Evaluate(LLMRawResult raw, Field? field)
    {
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

    /// <summary>Cancel all in-flight requests.</summary>
    public void CancelAll() => _backend.CancelAll();

    public void Dispose()
    {
        if (_ownsBackend)
            _backend.Dispose();
    }
}

using System;

namespace MakouReactor.AI.Backends;

/// <summary>Coarse phases of a scene generation run, for progress UIs.</summary>
public enum LLMProgressPhase
{
    /// <summary>The run has begun (prompt composed).</summary>
    Starting,

    /// <summary>A request is in flight to the backend.</summary>
    Waiting,

    /// <summary>The backend is producing output (detail carries a line of its progress text).</summary>
    Responding,

    /// <summary>The model output is being extracted and parsed.</summary>
    Parsing,

    /// <summary>The parsed plan is being laid out and validated.</summary>
    Validating,

    /// <summary>The output was unusable and the model is being re-prompted with the errors.</summary>
    Repairing,

    /// <summary>The run finished (successfully or not).</summary>
    Done,
}

/// <summary>A progress notification from the generation pipeline or a backend.</summary>
/// <param name="Phase">Where the run is.</param>
/// <param name="Detail">Optional human-readable detail (a repair reason, a backend progress line).</param>
/// <param name="Attempt">1-based model call number (2 = first repair).</param>
public sealed record LLMProgressEvent(LLMProgressPhase Phase, string Detail = "", int Attempt = 1)
{
    public override string ToString() =>
        string.IsNullOrEmpty(Detail) ? $"{Phase} (attempt {Attempt})" : $"{Phase} (attempt {Attempt}): {Detail}";
}

using System;
using System.Threading.Tasks;

using MakouReactor.Core.Models;

namespace MakouReactor.AI.Backends;

/// <summary>
/// Transport abstraction for scene plan generation.
/// Implementations: <see cref="CodexCliBackend"/> (Codex CLI, ChatGPT subscription)
/// and <see cref="MakouReactor.AI.Http.LLMClient"/> (OpenAI-compatible HTTP endpoint).
/// </summary>
public interface ILLMBackend : IDisposable
{
    /// <summary>
    /// Send a scene plan generation request and await the raw response.
    /// Never throws for transport failures; errors are reported via <see cref="LLMRawResult.Error"/>.
    /// </summary>
    Task<LLMRawResult> RequestScenePlanAsync(LLMRequest req);

    /// <summary>
    /// Same as <see cref="RequestScenePlanAsync(LLMRequest)"/> but lets the backend report progress while
    /// it works. Backends without progress support need not override this.
    /// </summary>
    Task<LLMRawResult> RequestScenePlanAsync(LLMRequest req, IProgress<LLMProgressEvent>? progress) =>
        RequestScenePlanAsync(req);

    /// <summary>Cancel all in-flight requests immediately.</summary>
    void CancelAll();
}

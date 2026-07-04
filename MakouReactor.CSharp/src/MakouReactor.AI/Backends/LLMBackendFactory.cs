using MakouReactor.AI.Http;
using MakouReactor.Core.Models;

namespace MakouReactor.AI.Backends;

/// <summary>
/// Creates the configured <see cref="ILLMBackend"/> from an <see cref="LLMConfig"/>.
/// Defaults to the Codex CLI (subscription) backend; "http" selects the
/// OpenAI-compatible endpoint fallback (e.g. LM Studio).
/// </summary>
public static class LLMBackendFactory
{
    public static ILLMBackend Create(LLMConfig config) => config.Backend switch
    {
        "http" => new LLMClient(),
        _ => new CodexCliBackend(config.CodexExecutable),
    };
}

using System.Text;
using System.Text.Json;

using MakouReactor.Core.Models;

namespace MakouReactor.AI.Backends;

/// <summary>
/// Normalizes backend responses to the bare scene-plan JSON expected by
/// <see cref="MakouReactor.AI.Parsing.ScenePlanParser"/>. HTTP backends return
/// an OpenAI chat-completion envelope; the Codex CLI backend returns the
/// message content directly.
/// </summary>
public static class LLMResponseExtractor
{
    /// <summary>
    /// Extract scene-plan JSON bytes from a raw backend result.
    /// Returns null and sets <paramref name="error"/> on failure.
    /// </summary>
    public static byte[]? ExtractScenePlanJson(LLMRawResult raw, out string error)
    {
        error = string.Empty;

        if (raw.Json is { } doc && doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("choices", out var choices))
        {
            // OpenAI chat-completion envelope: choices[0].message.content
            if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                error = "LLM response has no choices";
                return null;
            }

            var first = choices[0];
            if (!first.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var contentEl) ||
                contentEl.ValueKind != JsonValueKind.String)
            {
                error = "LLM response has no message content";
                return null;
            }

            var content = contentEl.GetString()!;
            var json = CodexCliBackend.ExtractJson(content);
            if (json == null)
            {
                error = "Message content is not valid JSON";
                return null;
            }

            return Encoding.UTF8.GetBytes(json);
        }

        // Codex CLI (or any backend that already returns bare scene-plan JSON).
        if (raw.Raw.Length == 0)
        {
            error = "Empty LLM response";
            return null;
        }

        return raw.Raw;
    }
}

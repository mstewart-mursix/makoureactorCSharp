using System.Text.Json;

namespace MakouReactor.Core.Models;

/// <summary>
/// Request DTO for an OpenAI-style chat completion (e.g., LM Studio).
/// Maps to <c>struct LLMRequest</c> in <c>ai/LLMSchemas.h</c>.
/// </summary>
public sealed class LLMRequest
{
    public string EndpointUrl { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public double Temperature { get; init; } = 0.6;
    public int MaxTokens { get; init; } = 2048;
    public string SystemPrompt { get; init; } = string.Empty;
    public string UserPrompt { get; init; } = string.Empty;
    public bool Stream { get; init; }
    public int TimeoutMs { get; init; } = 30_000;

    public override string ToString() =>
        $"LLMRequest(model={Model}, temp={Temperature}, maxTokens={MaxTokens})";
}

/// <summary>
/// Raw result from an LLM HTTP call: status, error, raw body, and parsed JSON.
/// Maps to <c>struct LLMRawResult</c> in <c>ai/LLMSchemas.h</c>.
/// </summary>
public sealed class LLMRawResult
{
    public bool Ok { get; set; }
    public int HttpStatus { get; set; }
    public string Error { get; set; } = string.Empty;
    public byte[] Raw { get; set; } = Array.Empty<byte>();
    public JsonDocument? Json { get; set; }

    public static LLMRawResult Success(int httpStatus, byte[] raw, JsonDocument? json = null) =>
        new() { Ok = true, HttpStatus = httpStatus, Raw = raw, Json = json };

    public static LLMRawResult Failure(int httpStatus, string error, byte[]? raw = null) =>
        new() { Ok = false, HttpStatus = httpStatus, Error = error, Raw = raw ?? Array.Empty<byte>() };

    public override string ToString() =>
        $"LLMRawResult(ok={Ok}, status={HttpStatus}{(!string.IsNullOrEmpty(Error) ? $", error={Error}" : string.Empty)})";
}

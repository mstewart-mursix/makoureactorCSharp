using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using MakouReactor.AI.Backends;
using MakouReactor.Core.Models;

namespace MakouReactor.AI.Http;

/// <summary>
/// Async HTTP client for OpenAI-compatible chat completions endpoints.
/// Tracks in-flight requests and supports cancellation.
/// </summary>
public class LLMClient : ILLMBackend
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ConcurrentDictionary<CancellationTokenSource, byte> _inFlight = new();

    public LLMClient(HttpClient? client = null)
    {
        _ownsHttpClient = client == null;
        _httpClient = client ?? new HttpClient();
    }

    /// <summary>
    /// Send a scene plan generation request and await the raw response.
    /// </summary>
    public async Task<LLMRawResult> RequestScenePlanAsync(LLMRequest req)
    {
        var cts = new CancellationTokenSource();
        _inFlight.TryAdd(cts, 0);

        try
        {
            // Apply timeout
            if (req.TimeoutMs > 0)
                cts.CancelAfter(req.TimeoutMs);

            // Build payload for OpenAI-style chat completions
            var messages = new List<Dictionary<string, string>>();

            if (!string.IsNullOrEmpty(req.SystemPrompt))
                messages.Add(new() { ["role"] = "system", ["content"] = req.SystemPrompt });

            messages.Add(new() { ["role"] = "user", ["content"] = req.UserPrompt });

            var payload = new Dictionary<string, object>
            {
                ["model"] = req.Model,
                ["temperature"] = req.Temperature,
                ["max_tokens"] = req.MaxTokens,
                ["stream"] = req.Stream,
                ["messages"] = messages
            };

            var content = JsonContent.Create(payload);

            using var request = new HttpRequestMessage(HttpMethod.Post, req.EndpointUrl)
            {
                Content = content
            };

            if (!string.IsNullOrEmpty(req.ApiKey))
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", req.ApiKey);

            var response = await _httpClient.SendAsync(request, cts.Token);
            var raw = await response.Content.ReadAsByteArrayAsync();

            var result = new LLMRawResult
            {
                HttpStatus = (int)response.StatusCode,
                Raw = raw,
                Ok = response.IsSuccessStatusCode
            };

            // Parse JSON if available
            try
            {
                result.Json = JsonDocument.Parse(raw);
            }
            catch (JsonException ex)
            {
                result.Ok = false;
                result.Error = $"Invalid JSON: {ex.Message}";
            }

            return result;
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            // Timeout fires cts.CancelAfter(); CancelAll() fires cts.Cancel() directly.
            // If TimeoutMs > 0 and the token is cancelled, treat as timeout.
            if (req.TimeoutMs > 0)
            {
                return new LLMRawResult { Ok = false, Error = "Request timed out" };
            }
            return new LLMRawResult { Ok = false, Error = "Request was cancelled" };
        }
        catch (HttpRequestException ex)
        {
            return new LLMRawResult
            {
                Ok = false,
                Error = $"HTTP error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new LLMRawResult
            {
                Ok = false,
                Error = $"Request failed: {ex.Message}"
            };
        }
        finally
        {
            _inFlight.TryRemove(cts, out _);
        }
    }

    /// <summary>
    /// Cancel all in-flight requests immediately.
    /// </summary>
    public void CancelAll()
    {
        foreach (var cts in _inFlight.Keys)
        {
            if (_inFlight.TryRemove(cts, out _))
                cts.Cancel();
        }
    }

    /// <summary>
    /// Dispose the underlying HttpClient (unless it was externally provided).
    /// </summary>
    public void Dispose()
    {
        CancelAll();
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}

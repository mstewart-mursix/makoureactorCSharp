using System.Text.Json;

namespace MakouReactor.AI.Batch;

/// <summary>One entry of a batch script: generate a scene for <see cref="Field"/> from <see cref="Prompt"/>.</summary>
public sealed class BatchEntry
{
    /// <summary>Field name inside the archive (e.g. <c>md1stin</c>).</summary>
    public required string Field { get; init; }

    /// <summary>High-level scene description sent to the model.</summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// Optional seed. Informational only: no backend supports seeding, so it is recorded in the
    /// batch report but does not make generation reproducible.
    /// </summary>
    public int? Seed { get; init; }
}

/// <summary>Result of <see cref="BatchScript.Parse"/>: the entries, or an error describing the bad input.</summary>
public sealed class BatchScriptParseResult
{
    /// <summary>True when the script parsed and every entry is well formed.</summary>
    public bool Ok => Error.Length == 0;

    /// <summary>Description of the first problem found; empty on success.</summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>Parsed entries in script order (empty on failure).</summary>
    public IReadOnlyList<BatchEntry> Entries { get; init; } = Array.Empty<BatchEntry>();
}

/// <summary>
/// Reader for batch scripts: a JSON array of
/// <c>{ "field": name, "prompt": text, "seed"?: int }</c> objects. Comments and trailing commas
/// are tolerated; unknown properties are ignored.
/// </summary>
public static class BatchScript
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Read and parse the batch script at <paramref name="path"/>.</summary>
    public static BatchScriptParseResult Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new BatchScriptParseResult { Error = $"Cannot read batch script '{path}': {ex.Message}" };
        }
        return Parse(json);
    }

    /// <summary>Parse batch-script JSON text.</summary>
    public static BatchScriptParseResult Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            return Fail($"Batch script is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
                return Fail("Batch script must be a JSON array of { \"field\", \"prompt\", \"seed\"? } objects.");
            if (root.GetArrayLength() == 0)
                return Fail("Batch script contains no entries.");

            var entries = new List<BatchEntry>();
            var index = 0;
            foreach (var element in root.EnumerateArray())
            {
                index++;
                if (element.ValueKind != JsonValueKind.Object)
                    return Fail($"Entry {index}: expected an object.");

                var field = RequiredString(element, "field", index, out var error);
                if (field == null)
                    return Fail(error);
                var prompt = RequiredString(element, "prompt", index, out error);
                if (prompt == null)
                    return Fail(error);

                int? seed = null;
                if (element.TryGetProperty("seed", out var seedElement) && seedElement.ValueKind != JsonValueKind.Null)
                {
                    if (seedElement.ValueKind != JsonValueKind.Number || !seedElement.TryGetInt32(out var seedValue))
                        return Fail($"Entry {index}: \"seed\" must be a 32-bit integer.");
                    seed = seedValue;
                }

                entries.Add(new BatchEntry { Field = field, Prompt = prompt, Seed = seed });
            }

            return new BatchScriptParseResult { Entries = entries };
        }
    }

    private static string? RequiredString(JsonElement element, string name, int index, out string error)
    {
        error = string.Empty;
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            error = $"Entry {index}: \"{name}\" must be a string.";
            return null;
        }

        var text = value.GetString()!.Trim();
        if (text.Length == 0)
        {
            error = $"Entry {index}: \"{name}\" must not be empty.";
            return null;
        }
        return text;
    }

    private static BatchScriptParseResult Fail(string error) => new() { Error = error };
}

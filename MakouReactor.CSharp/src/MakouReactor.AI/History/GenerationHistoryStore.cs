using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using MakouReactor.Core.Models;

namespace MakouReactor.AI.History;

/// <summary>One saved generation: what was asked, what came back, and how it validated.</summary>
public sealed class GenerationHistoryEntry
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }

    /// <summary>The scene description the user typed.</summary>
    public string Prompt { get; set; } = string.Empty;

    public bool GenDialog { get; set; } = true;
    public bool GenLayout { get; set; } = true;
    public bool GenScripts { get; set; } = true;

    /// <summary>Field the run targeted, if any.</summary>
    public string? FieldName { get; set; }

    /// <summary>The refinement instruction when this entry came from a refine, otherwise null.</summary>
    public string? Refinement { get; set; }

    /// <summary>The generated plan JSON (empty when the run failed).</summary>
    public string RawJson { get; set; } = string.Empty;

    /// <summary>Failure message when the run failed.</summary>
    public string? Error { get; set; }

    /// <summary>Model calls made (1 = no repair needed).</summary>
    public int Attempts { get; set; } = 1;

    public int ValidationErrors { get; set; }
    public int ValidationWarnings { get; set; }

    public bool Succeeded => string.IsNullOrEmpty(Error) && RawJson.Length > 0;

    /// <summary>A short label for list UIs.</summary>
    public string Title
    {
        get
        {
            var text = (Refinement ?? Prompt).Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (text.Length > 60)
                text = text[..60] + "...";
            var prefix = Refinement != null ? "Refine: " : string.Empty;
            return $"{CreatedUtc.ToLocalTime():HH:mm:ss}  {prefix}{text}";
        }
    }

    /// <summary>Capture a finished run.</summary>
    public static GenerationHistoryEntry From(ScenePrompt prompt, SceneGenerationResult result,
                                              string? fieldName = null, string? refinement = null) => new()
    {
        Prompt = prompt.UserText,
        GenDialog = prompt.GenDialog,
        GenLayout = prompt.GenLayout,
        GenScripts = prompt.GenScripts,
        FieldName = fieldName,
        Refinement = refinement,
        RawJson = result.RawJson,
        Error = result.Ok ? null : result.Error,
        Attempts = Math.Max(1, result.Attempts.Count),
        ValidationErrors = result.Validation?.Issues.Count(static i => i.Level == Severity.Error) ?? 0,
        ValidationWarnings = result.Validation?.WarnCount ?? 0,
    };
}

/// <summary>
/// Persists generation history as one JSON file per entry (newest first on load), capped at
/// <see cref="MaxEntries"/>. Unreadable files are skipped, never fatal.
/// </summary>
public sealed class GenerationHistoryStore
{
    /// <summary>Default number of entries kept.</summary>
    public const int DefaultMaxEntries = 50;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _directory;

    public GenerationHistoryStore(string? directory = null, int maxEntries = DefaultMaxEntries)
    {
        _directory = directory ?? DefaultDirectory();
        MaxEntries = Math.Max(1, maxEntries);
    }

    public int MaxEntries { get; }

    public string Directory => _directory;

    /// <summary>%APPDATA%\MakouReactor\history (platform equivalent elsewhere).</summary>
    public static string DefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MakouReactor", "history");

    /// <summary>Write the entry (assigning an id and timestamp when missing) and prune old entries.</summary>
    public GenerationHistoryEntry Add(GenerationHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.CreatedUtc == default)
            entry.CreatedUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(entry.Id))
            entry.Id = $"{entry.CreatedUtc:yyyyMMdd-HHmmssfff}-{Guid.NewGuid().ToString("N")[..4]}";

        System.IO.Directory.CreateDirectory(_directory);
        var path = PathFor(entry.Id);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(entry, JsonOptions));
        File.Move(tempPath, path, overwrite: true);

        Prune();
        return entry;
    }

    /// <summary>All readable entries, newest first.</summary>
    public IReadOnlyList<GenerationHistoryEntry> List()
    {
        if (!System.IO.Directory.Exists(_directory))
            return [];

        var entries = new List<GenerationHistoryEntry>();
        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<GenerationHistoryEntry>(File.ReadAllText(file));
                if (entry is { Id.Length: > 0 })
                    entries.Add(entry);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // A corrupt or locked history file must never break the generator.
            }
        }

        return entries
            .OrderByDescending(static e => e.CreatedUtc)
            .ThenByDescending(static e => e.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Delete one entry; returns false when it did not exist.</summary>
    public bool Delete(string id)
    {
        var path = PathFor(id);
        if (!File.Exists(path))
            return false;

        File.Delete(path);
        return true;
    }

    /// <summary>Delete every entry.</summary>
    public void Clear()
    {
        if (!System.IO.Directory.Exists(_directory))
            return;

        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
            File.Delete(file);
    }

    private void Prune()
    {
        foreach (var old in List().Skip(MaxEntries))
            Delete(old.Id);
    }

    private string PathFor(string id)
    {
        // Ids are generated here, but a hand-edited file could carry anything; never let it escape the folder.
        var safe = string.Concat(id.Where(static c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        return Path.Combine(_directory, safe + ".json");
    }
}

using System;

namespace MakouReactor.Core.Models;

// ============================================================================
// Validation types (maps to ai/ScenePlanValidator.h)
// ============================================================================

/// <summary>
/// Severity level for a validation issue.
/// Maps to <c>enum class Severity</c> in <c>ai/ScenePlanValidator.h</c>.
/// </summary>
public enum Severity
{
    Info = 0,
    Warn,
    Error
}

/// <summary>
/// A single validation issue with severity, path, and message.
/// Maps to <c>struct Issue</c> in <c>ai/ScenePlanValidator.h</c>.
/// </summary>
public sealed class Issue
{
    public Severity Level { get; init; }
    public string Path { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public Issue() { }

    public Issue(Severity level, string path, string message)
    {
        Level = level;
        Path = path;
        Message = message;
    }

    public override string ToString() => $"[{Level}] {Path}: {Message}";
}

/// <summary>
/// Options for scene plan validation.
/// Maps to <c>struct Options</c> in <c>ai/ScenePlanValidator.h</c>.
/// </summary>
public sealed class ValidationOptions
{
    public int MaxLineLength { get; init; } = 100;
    public bool ProfanityFilter { get; init; }
    public List<string> BannedWords { get; init; } = new();
}

/// <summary>
/// Result of scene plan validation.
/// Maps to <c>struct Result</c> in <c>ai/ScenePlanValidator.h</c>.
/// </summary>
public sealed class ValidationResult
{
    public List<Issue> Issues { get; init; } = new();

    public bool HasErrors => Issues.Any(i => i.Level == Severity.Error);

    public int WarnCount => Issues.Count(i => i.Level == Severity.Warn);

    public override string ToString() =>
        $"ValidationResult(issues={Issues.Count}, errors={HasErrors}, warns={WarnCount})";
}

// ============================================================================
// Mapper types (maps to ai/ScenePlanMapper.h)
// ============================================================================

/// <summary>How a proposed walkmesh is combined with the field's existing one.</summary>
public enum WalkmeshMode
{
    /// <summary>Do not touch the field's walkmesh (default, the safe choice).</summary>
    Ignore,

    /// <summary>Append the proposed triangles to the existing walkmesh.</summary>
    Merge,

    /// <summary>Replace the existing walkmesh entirely.</summary>
    Replace,
}

/// <summary>
/// Options for applying a scene plan to a field.
/// Maps to <c>struct ApplyOptions</c> in <c>ai/ScenePlanMapper.h</c>.
/// </summary>
public sealed class ApplyOptions
{
    public bool PreviewOnly { get; init; } = true;
    public string? GroupNameOverride { get; init; }

    /// <summary>How a proposed walkmesh (schema 1.1) is combined with the field's walkmesh.</summary>
    public WalkmeshMode WalkmeshMode { get; init; } = WalkmeshMode.Ignore;
}

/// <summary>
/// Result of applying a scene plan to a field.
/// Maps to <c>struct ApplyResult</c> in <c>ai/ScenePlanMapper.h</c>.
/// </summary>
public sealed class ApplyResult
{
    public bool Ok { get; init; }
    public string GroupName { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;

    public static ApplyResult Success(string groupName, string summary) =>
        new() { Ok = true, GroupName = groupName, Summary = summary };

    public static ApplyResult Failure(string error) =>
        new() { Ok = false, Error = error };

    public override string ToString() =>
        $"ApplyResult(ok={Ok}, group={GroupName}{(!string.IsNullOrEmpty(Error) ? $", error={Error}" : string.Empty)})";
}

// ============================================================================
// Layout types (maps to ai/LayoutGenerator.h)
// ============================================================================

/// <summary>
/// Options for layout adjustment.
/// Maps to <c>struct LayoutOptions</c> in <c>ai/LayoutGenerator.h</c>.
/// </summary>
public sealed class LayoutOptions
{
    public int MinDistancePx { get; init; } = 24;
    public bool EnableWalkmeshSnap { get; init; }

    /// <summary>
    /// When the field has a walkmesh, move spawn point, props and actors that fall outside it onto the
    /// nearest walkable point, and keep overlap nudging on walkable ground. Ignored without a walkmesh.
    /// </summary>
    public bool ConstrainToWalkmesh { get; init; } = true;
    public int NudgeStepPx { get; init; } = 8;
    public int MaxNudgeTries { get; init; } = 200;
}

/// <summary>
/// Result of layout adjustment.
/// Maps to <c>struct LayoutResult</c> in <c>ai/LayoutGenerator.h</c>.
/// </summary>
public sealed class LayoutResult
{
    public bool Ok { get; init; } = true;
    public List<string> Notes { get; init; } = new();

    public override string ToString() =>
        $"LayoutResult(ok={Ok}, notes={Notes.Count})";
}

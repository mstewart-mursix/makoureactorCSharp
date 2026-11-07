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
    /// <summary>
    /// When true (the default) the mapper computes the full result (groups, scripts, opcode
    /// counts, text additions, warnings) without modifying the field.
    /// </summary>
    public bool PreviewOnly { get; init; } = true;

    /// <summary>
    /// Name for the generated director group. It is sanitized, cut to the 8-character group name
    /// limit and made unique within the field.
    /// </summary>
    public string? GroupNameOverride { get; init; }

    /// <summary>
    /// Variable bank (1-15) for the bits that back <c>set_flag</c>/<c>if_flag</c> keys. Defaults to
    /// bank 8, one of the banks the C++ reference documents as temporary, not stored in the save
    /// (<c>src/widgets/VarManager.cpp</c>: "Var banks 08, 09 and 10 are temporary").
    /// </summary>
    public byte FlagBank { get; init; } = 8;
    /// <summary>How a proposed walkmesh (schema 1.1) is combined with the field's walkmesh.</summary>
    public WalkmeshMode WalkmeshMode { get; init; } = WalkmeshMode.Ignore;
}

/// <summary>A dialogue text appended to the field's text table by the mapper.</summary>
/// <param name="TextId">Text index in Section 1.</param>
/// <param name="Text">Decoded text, including any automatic line breaks.</param>
public sealed record AppliedText(int TextId, string Text);

/// <summary>One script slot emitted by the mapper.</summary>
/// <param name="Slot">Script slot (0 = "S0 - Init", 1 = "S0 - Main", N + 1 = file script N).</param>
/// <param name="Label">Display label of the slot.</param>
/// <param name="Bytes">Compiled script bytes.</param>
/// <param name="Opcodes">Opcode names in emission order.</param>
public sealed record AppliedScript(int Slot, string Label, byte[] Bytes, IReadOnlyList<string> Opcodes);

/// <summary>A script group created by the mapper.</summary>
/// <param name="GroupIndex">Index of the group in the field once applied.</param>
/// <param name="Name">Group name (at most 8 characters, unique within the field).</param>
/// <param name="Role">"director" or the plan actor id the group represents.</param>
/// <param name="Scripts">Non-empty script slots.</param>
public sealed record AppliedGroup(int GroupIndex, string Name, string Role, IReadOnlyList<AppliedScript> Scripts);

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

    /// <summary>True when the result was computed without modifying the field.</summary>
    public bool PreviewOnly { get; init; }

    /// <summary>Script groups created (or, for a preview, that would be created).</summary>
    public IReadOnlyList<AppliedGroup> Groups { get; init; } = Array.Empty<AppliedGroup>();

    /// <summary>Texts appended to the field's text table.</summary>
    public IReadOnlyList<AppliedText> TextsAdded { get; init; } = Array.Empty<AppliedText>();

    /// <summary>Emitted opcode counts by opcode name, over all groups.</summary>
    public IReadOnlyDictionary<string, int> OpcodeCounts { get; init; } = new Dictionary<string, int>();

    /// <summary>Things the mapper could not emit faithfully (skipped steps, approximations).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary><c>custom_note</c> texts, recorded instead of emitted.</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

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

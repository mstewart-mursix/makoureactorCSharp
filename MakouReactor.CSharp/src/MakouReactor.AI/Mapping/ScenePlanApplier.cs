using System;
using System.IO;
using System.Linq;

using MakouReactor.AI.Layout;
using MakouReactor.AI.Validation;
using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

namespace MakouReactor.AI.Mapping;

/// <summary>Result of <see cref="ScenePlanApplier.ApplyToArchive"/>.</summary>
public sealed class ScenePlanApplyOutcome
{
    public bool Ok { get; init; }
    public string Error { get; init; } = string.Empty;
    public bool DryRun { get; init; }

    /// <summary>Preview/apply summary including what is and is not written.</summary>
    public string Summary { get; init; } = string.Empty;

    public ValidationResult? Validation { get; init; }

    /// <summary>Layout notes (placements that were moved), if any.</summary>
    public LayoutResult? Layout { get; init; }

    /// <summary>The backup and prune details when the archive was actually written.</summary>
    public SafeWriteResult? Write { get; init; }

    public static ScenePlanApplyOutcome Failure(string error, ValidationResult? validation = null) =>
        new() { Ok = false, Error = error, Validation = validation };
}

/// <summary>
/// Applies a scene plan to one field of an LGP archive end to end: adjust layout, validate (errors
/// block), map into the field, then write through <see cref="SafeArchiveWriter"/>.
/// </summary>
public static class ScenePlanApplier
{
    /// <param name="archivePath">Path to the flevel.lgp to modify.</param>
    /// <param name="fieldName">Field inside the archive.</param>
    /// <param name="plan">The plan; layout adjustment may move its placements.</param>
    /// <param name="dryRun">When true nothing is written; the summary describes the change.</param>
    /// <param name="walkmeshMode">How a proposed walkmesh is combined with the field's.</param>
    /// <param name="scriptMode">Whether on_enter/auto events are compiled into a new script group.</param>
    public static ScenePlanApplyOutcome ApplyToArchive(
        string archivePath,
        string fieldName,
        ScenePlan plan,
        bool dryRun = false,
        WalkmeshMode walkmeshMode = WalkmeshMode.Ignore,
        ScriptMode scriptMode = ScriptMode.None)
    {
        ArgumentNullException.ThrowIfNull(plan);

        FieldPC field;
        try
        {
            field = FieldArchive.Open(archivePath).OpenField(fieldName);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            return ScenePlanApplyOutcome.Failure($"Could not open field '{fieldName}': {ex.Message}");
        }

        var layout = LayoutGenerator.Adjust(plan, field);
        var validation = ScenePlanValidator.Validate(plan, field);
        if (validation.HasErrors)
        {
            var errors = string.Join("; ", validation.Issues.Where(static i => i.Level == Severity.Error).Select(static i => i.ToString()));
            return new ScenePlanApplyOutcome
            {
                Ok = false,
                Error = $"Plan has validation errors: {errors}",
                Validation = validation,
                Layout = layout,
            };
        }

        var preview = ScenePlanMapper.ApplyToField(plan, field,
            new ApplyOptions { PreviewOnly = true, WalkmeshMode = walkmeshMode, ScriptMode = scriptMode });
        if (!preview.Ok)
            return ScenePlanApplyOutcome.Failure(preview.Error, validation);

        if (dryRun)
        {
            return new ScenePlanApplyOutcome
            {
                Ok = true,
                DryRun = true,
                Summary = preview.Summary,
                Validation = validation,
                Layout = layout,
            };
        }

        var applied = ScenePlanMapper.ApplyToField(plan, field,
            new ApplyOptions { PreviewOnly = false, WalkmeshMode = walkmeshMode, ScriptMode = scriptMode });
        if (!applied.Ok)
            return ScenePlanApplyOutcome.Failure(applied.Error, validation);

        try
        {
            var write = SafeArchiveWriter.WriteField(archivePath, fieldName, field.SaveCompressed());
            return new ScenePlanApplyOutcome
            {
                Ok = true,
                Summary = applied.Summary,
                Validation = validation,
                Layout = layout,
                Write = write,
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return ScenePlanApplyOutcome.Failure($"Writing the archive failed (original untouched): {ex.Message}", validation);
        }
    }
}

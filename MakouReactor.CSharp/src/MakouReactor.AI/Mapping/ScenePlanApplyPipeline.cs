using MakouReactor.AI.Layout;
using MakouReactor.AI.Validation;
using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

namespace MakouReactor.AI.Mapping;

/// <summary>
/// Outcome of <see cref="ScenePlanApplyPipeline.Check"/>: layout notes, validation issues and,
/// when validation passed, the mapper's preview of the change.
/// </summary>
public sealed class PlanApplyCheck
{
    /// <summary>Notes from <see cref="LayoutGenerator.Adjust"/> (positions nudged, clamped, snapped).</summary>
    public required LayoutResult Layout { get; init; }

    /// <summary>Validation of the adjusted plan against the target field.</summary>
    public required ValidationResult Validation { get; init; }

    /// <summary>
    /// Mapper preview (<see cref="ApplyOptions.PreviewOnly"/> = true); null when validation
    /// reported errors, because such a plan is never applied.
    /// </summary>
    public ApplyResult? Preview { get; init; }

    /// <summary>True when the plan validated without errors and the preview succeeded.</summary>
    public bool CanApply => !Validation.HasErrors && Preview is { Ok: true };
}

/// <summary>Outcome of <see cref="ScenePlanApplyPipeline.Commit"/>.</summary>
public sealed class PlanCommitResult
{
    /// <summary>True when the archive now contains the modified field.</summary>
    public bool Ok { get; init; }

    /// <summary>
    /// Failure description, already prefixed with the failing stage ("Apply failed: ..." for the
    /// mapper, "Write failed, archive untouched: ..." for the archive writer). Empty on success.
    /// </summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>The mapper result (null if the mapper was not reached).</summary>
    public ApplyResult? Applied { get; init; }

    /// <summary>The archive writer result (null if the writer was not reached).</summary>
    public SafeApplyResult? Write { get; init; }
}

/// <summary>
/// The shared "apply a scene plan to a PC field inside an LGP archive" pipeline used by the CLI
/// <c>apply</c> and <c>batch --apply</c> commands:
/// <see cref="LayoutGenerator.Adjust"/> → <see cref="ScenePlanValidator"/> → mapper preview
/// (<see cref="Check"/>), then <see cref="ScenePlanMapper.ApplyToField"/> with
/// <c>PreviewOnly = false</c> → <see cref="SafeArchiveWriter.ApplyField"/> (<see cref="Commit"/>).
/// </summary>
public static class ScenePlanApplyPipeline
{
    /// <summary>
    /// Adjust the plan's layout for <paramref name="field"/> (mutating <paramref name="plan"/>),
    /// validate it and, when there are no validation errors, compute the mapper preview.
    /// The field is not modified.
    /// </summary>
    public static PlanApplyCheck Check(ScenePlan plan, FieldPC field)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(field);

        var layout = LayoutGenerator.Adjust(plan, field);
        var validation = ScenePlanValidator.Validate(plan, field);
        var preview = validation.HasErrors
            ? null
            : ScenePlanMapper.ApplyToField(plan, field, new ApplyOptions { PreviewOnly = true });

        return new PlanApplyCheck { Layout = layout, Validation = validation, Preview = preview };
    }

    /// <summary>
    /// Apply <paramref name="plan"/> to <paramref name="field"/> and write the field into the archive
    /// through <paramref name="writer"/> (backup → stage → verify → atomic replace). Call only after a
    /// <see cref="Check"/> whose <see cref="PlanApplyCheck.CanApply"/> is true. On a write failure the
    /// archive is untouched (the in-memory field is modified either way).
    /// </summary>
    public static PlanCommitResult Commit(ScenePlan plan, FieldPC field, SafeArchiveWriter writer)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(writer);

        var applied = ScenePlanMapper.ApplyToField(plan, field, new ApplyOptions { PreviewOnly = false });
        if (!applied.Ok)
            return new PlanCommitResult { Error = $"Apply failed: {applied.Error}", Applied = applied };

        var write = writer.ApplyField(field);
        if (!write.Ok)
            return new PlanCommitResult { Error = $"Write failed, archive untouched: {write.Error}", Applied = applied, Write = write };

        return new PlanCommitResult { Ok = true, Applied = applied, Write = write };
    }
}

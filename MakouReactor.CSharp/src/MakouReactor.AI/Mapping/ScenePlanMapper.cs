using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using MakouReactor.Core.Models;

namespace MakouReactor.AI.Mapping;

/// <summary>
/// Applies a validated <see cref="ScenePlan"/> to a <see cref="Field"/>: appends one director
/// group plus one model group per plan actor to Section 1, emits their scripts as real field
/// opcodes (see <see cref="SceneScriptEmitter"/>) and appends the dialogue texts.
/// </summary>
public static class ScenePlanMapper
{
    /// <summary>
    /// Apply a scene plan to the given field. With <see cref="ApplyOptions.PreviewOnly"/> the full
    /// result (groups, scripts, opcode counts, text additions, warnings) is computed but the field
    /// is left untouched.
    /// </summary>
    public static ApplyResult ApplyToField(ScenePlan plan, Field? field, ApplyOptions? opts = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        opts ??= new ApplyOptions();

        if (field is null)
            return new ApplyResult { Ok = false, Error = "No active field" };

        var section = field.ScriptsAndTexts;
        if (section == null)
        {
            if (field is FieldPC)
                return ApplyResult.Failure("Active field does not have a parsed Section 1 text table.");

            // Fields without a Section 1 model (test doubles, formats still being ported) only get
            // the plan summary and the modified flag.
            var fallbackName = string.IsNullOrEmpty(opts.GroupNameOverride)
                ? MakeGroupName("LLM_Generated")
                : opts.GroupNameOverride;
            const string warning = "The field has no parsed Section 1; no scripts or texts were emitted.";
            if (!opts.PreviewOnly)
                field.SetModified();

            return new ApplyResult
            {
                Ok = true,
                GroupName = fallbackName,
                PreviewOnly = opts.PreviewOnly,
                Warnings = [warning],
                Summary = $"{Summarize(plan)}{Environment.NewLine}Warnings:{Environment.NewLine} - {warning}{Environment.NewLine}",
            };
        }

        var emission = SceneScriptEmitter.Emit(plan, field, section, opts);
        if (emission.Error != null)
            return ApplyResult.Failure(emission.Error);

        if (!opts.PreviewOnly)
            Commit(field, section, emission);

        return new ApplyResult
        {
            Ok = true,
            GroupName = emission.DirectorName,
            PreviewOnly = opts.PreviewOnly,
            Groups = emission.Groups,
            TextsAdded = emission.Texts,
            OpcodeCounts = emission.OpcodeCounts,
            Warnings = emission.Warnings,
            Notes = emission.Notes,
            Summary = BuildSummary(plan, emission, opts.PreviewOnly),
        };
    }

    /// <summary>
    /// Generate a timestamped group name (longer than the 8-character field limit; kept for
    /// callers that need a unique label, not a field group name).
    /// </summary>
    public static string MakeGroupName(string base_ = "")
    {
        var ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        return string.IsNullOrEmpty(base_)
            ? $"LLM_Generated_{ts}"
            : $"{base_}_{ts}";
    }

    /// <summary>
    /// Produce a human-readable summary of the scene plan for preview.
    /// </summary>
    public static string Summarize(ScenePlan plan)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Title: {plan.Meta.Title}");
        sb.AppendLine($"Model: {plan.Meta.Model} (schema {plan.Meta.Version})");
        sb.AppendLine($"Actors: {plan.Actors.Count}");
        sb.AppendLine($"Dialog lines: {plan.Dialog.Count}");
        sb.AppendLine($"Events: {plan.Events.Count}");

        if (plan.Actors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Actors:");
            foreach (var a in plan.Actors)
            {
                var name = string.IsNullOrEmpty(a.DisplayName) ? a.Id : a.DisplayName;
                var face = a.Facing.HasValue ? a.Facing.Value.ToString() : "?";
                sb.AppendLine($" - {a.Id} ({name}) at {a.Position.X},{a.Position.Y} facing {face}");
            }
        }

        if (plan.Dialog.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Dialog (first 5):");
            var n = Math.Min(5, plan.Dialog.Count);
            for (int i = 0; i < n; i++)
            {
                var d = plan.Dialog[i];
                var preview = d.Text.Length > 80 ? d.Text[..80] : d.Text;
                sb.AppendLine($" - {d.SpeakerId}: {preview}");
            }
            if (plan.Dialog.Count > n)
                sb.AppendLine($" (… {plan.Dialog.Count - n} more)");
        }

        if (plan.Events.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Events:");
            foreach (var e in plan.Events)
                sb.AppendLine($" - {e.Id} [{e.Trigger}], steps={e.Steps.Count}");
        }

        return sb.ToString();
    }

    private static void Commit(Field field, Section1File section, SceneEmission emission)
    {
        // Appended texts take new ids, so no existing MESSAGE/ASK/MPNAM reference needs shifting
        // (InsertText would also shift out-of-range ids already present in the field).
        section.Texts.AddRange(emission.TextObjects);

        foreach (var group in emission.GrpScripts)
            section.InsertGrpScript(section.GrpScriptCount, group);

        section.MarkModified();
        field.SetModified();
    }

    private static string BuildSummary(ScenePlan plan, SceneEmission emission, bool previewOnly)
    {
        var nl = Environment.NewLine;
        var sb = new StringBuilder(Summarize(plan));
        sb.Append(nl);
        sb.Append(previewOnly
            ? $"Script emission (preview only, the field was not modified):{nl}"
            : $"Script emission (applied to the field):{nl}");

        foreach (var group in emission.Groups)
        {
            sb.Append($" - Group {group.GroupIndex} \"{group.Name}\" ({group.Role}){nl}");
            foreach (var script in group.Scripts)
            {
                var counts = string.Join(", ", script.Opcodes
                    .GroupBy(static name => name)
                    .Select(static g => g.Count() == 1 ? g.Key : $"{g.Key} x{g.Count()}"));
                sb.Append($"     {script.Label}: {script.Bytes.Length} byte(s), {script.Opcodes.Count} opcode(s) [{counts}]{nl}");
            }
        }

        if (emission.OpcodeCounts.Count > 0)
        {
            sb.Append("Opcode counts: ");
            sb.Append(string.Join(", ", emission.OpcodeCounts
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => $"{pair.Key}={pair.Value}")));
            sb.Append(nl);
        }

        if (emission.Texts.Count == 0)
        {
            sb.Append($"No dialogue text was added.{nl}");
        }
        else
        {
            var count = emission.Texts.Count;
            sb.Append($"Added {count} dialogue text entr{(count == 1 ? "y" : "ies")} starting at text {emission.Texts[0].TextId}.{nl}");
            foreach (var text in emission.Texts)
            {
                var oneLine = text.Text.Replace("\n", " / ", StringComparison.Ordinal);
                sb.Append($" - text {text.TextId}: {oneLine}{nl}");
            }
        }

        if (emission.Notes.Count > 0)
        {
            sb.Append($"Notes (custom_note, no opcode emitted):{nl}");
            foreach (var note in emission.Notes)
                sb.Append($" - {note}{nl}");
        }

        if (emission.Warnings.Count > 0)
        {
            sb.Append($"Warnings:{nl}");
            foreach (var warning in emission.Warnings)
                sb.Append($" - {warning}{nl}");
        }

        return sb.ToString();
    }
}

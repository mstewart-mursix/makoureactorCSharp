using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using MakouReactor.AI.Layout;
using MakouReactor.Core.Models;

namespace MakouReactor.AI.Mapping;

/// <summary>
/// Applies a validated <see cref="ScenePlan"/> to a <see cref="Field"/>.
/// Initial implementation focuses on scaffolding and idempotent grouping.
/// </summary>
public static class ScenePlanMapper
{
    /// <summary>
    /// Apply a scene plan to the given field.
    /// </summary>
    public static ApplyResult ApplyToField(ScenePlan plan, Field? field, ApplyOptions? opts = null)
    {
        opts ??= new ApplyOptions();

        if (field is null)
            return new ApplyResult { Ok = false, Error = "No active field" };

        var groupName = string.IsNullOrEmpty(opts.GroupNameOverride)
            ? MakeGroupName("LLM_Generated")
            : opts.GroupNameOverride;

        var summary = Summarize(plan);

        if (opts.PreviewOnly)
        {
            return new ApplyResult
            {
                Ok = true,
                GroupName = groupName,
                Summary = $"{summary}{Environment.NewLine}{DescribeChanges(plan, field, opts.WalkmeshMode, opts.ScriptMode)}",
            };
        }

        if (field is FieldPC pcField)
        {
            var walkmesh = opts.WalkmeshMode != WalkmeshMode.Ignore && plan.Layout.Walkmesh is { Regions.Count: > 0 }
                ? plan.Layout.Walkmesh
                : null;

            // Validate the walkmesh before touching anything so a bad region cannot leave a half-applied plan.
            if (walkmesh != null)
            {
                foreach (var region in walkmesh.Regions)
                {
                    var problem = WalkmeshBuilder.ValidatePolygon(region.Polygon);
                    if (problem != null)
                        return ApplyResult.Failure($"Walkmesh region '{region.Id}': {problem}.");
                }
            }

            var applyResult = ApplyToPcField(plan, pcField, groupName, summary, opts);
            if (!applyResult.Ok || walkmesh == null)
                return applyResult;

            if (pcField.Walkmesh == null)
                pcField.ReplaceWalkmesh(new IdFile());

            var added = WalkmeshBuilder.Apply(pcField.Walkmesh!, walkmesh, opts.WalkmeshMode);
            pcField.ApplyWalkmeshChanges();
            var verb = opts.WalkmeshMode == WalkmeshMode.Replace ? "Replaced the walkmesh with" : "Added";
            return ApplyResult.Success(
                applyResult.GroupName,
                $"{applyResult.Summary}{Environment.NewLine}{verb} {added} walkmesh triangle(s) from {walkmesh.Regions.Count} region(s).");
        }

        // Non-PC field support is still being ported; retain the previous
        // behavior for test doubles and future field implementations.
        field.SetModified();

        return new ApplyResult { Ok = true, GroupName = groupName, Summary = summary };
    }

    /// <summary>
    /// Generate a timestamped group name.
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
                sb.AppendLine($" (\u2026 {plan.Dialog.Count - n} more)");
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

    /// <summary>
    /// Describe precisely what <see cref="ApplyToField"/> will write to <paramref name="field"/> and what
    /// stays only in the plan JSON, so the preview never overstates the change.
    /// </summary>
    public static string DescribeChanges(ScenePlan plan, Field? field,
                                         WalkmeshMode walkmeshMode = WalkmeshMode.Ignore,
                                         ScriptMode scriptMode = ScriptMode.None)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Changes to the field:");

        var entries = CollectDialogue(plan);
        var texts = entries.Count;
        var compiled = new CompiledScene();
        if (field is FieldPC { ScriptsAndTexts: { } sectionForScript } && scriptMode == ScriptMode.AppendGroup)
            compiled = CompileEvents(plan, entries, sectionForScript.TextCount);

        if (field is FieldPC { ScriptsAndTexts: { } section })
        {
            if (texts == 0)
            {
                sb.AppendLine(" - No dialogue text will be added.");
            }
            else
            {
                var start = section.TextCount;
                var end = start + texts - 1;
                sb.AppendLine($" - Add {texts} dialogue text entr{(texts == 1 ? "y" : "ies")} (texts {start}..{end}).");
                if (start + texts > Section1File.MaxTextCount)
                    sb.AppendLine($" ! This exceeds the Section 1 text limit of {Section1File.MaxTextCount}; apply will be refused.");
            }
        }
        else
        {
            sb.AppendLine(" - The active field has no parsed Section 1; nothing can be applied.");
        }

        var steps = plan.Events.SelectMany(static e => FlattenSteps(e.Steps)).Count();
        var notWritten = new List<string>();
        if (plan.Actors.Count > 0)
            notWritten.Add($"{plan.Actors.Count} actor(s)");
        if (scriptMode == ScriptMode.AppendGroup)
        {
            if (!compiled.IsEmpty)
            {
                sb.AppendLine($" - Add a script group with {compiled.StepsWritten} step(s) that run when the field loads (experimental).");
            }
            else if (plan.Events.Count > 0)
            {
                sb.AppendLine(" - No event steps could be compiled into a script group.");
            }

            foreach (var skip in compiled.Skipped)
                sb.AppendLine($"   skipped: {skip}");
        }
        else if (plan.Events.Count > 0)
        {
            notWritten.Add($"{plan.Events.Count} event(s) with {steps} step(s) (script mode is Off)");
        }
        if (plan.Layout.Props.Count > 0)
            notWritten.Add($"{plan.Layout.Props.Count} prop(s)");
        if (plan.Layout.Walkmesh is { Regions.Count: > 0 } proposed)
        {
            if (walkmeshMode == WalkmeshMode.Ignore)
            {
                notWritten.Add($"{proposed.Regions.Count} walkmesh region(s) (walkmesh mode is Ignore)");
            }
            else
            {
                var triangles = proposed.Regions
                    .Where(static r => WalkmeshBuilder.ValidatePolygon(r.Polygon) == null)
                    .Sum(static r => WalkmeshBuilder.Triangulate(r.Polygon).Count);
                var existing = (field as FieldPC)?.Walkmesh?.TriangleCount ?? 0;
                sb.AppendLine(walkmeshMode == WalkmeshMode.Replace
                    ? $" - Replace the walkmesh ({existing} triangle(s)) with {triangles} triangle(s) from {proposed.Regions.Count} region(s)."
                    : $" - Add {triangles} triangle(s) from {proposed.Regions.Count} region(s) to the walkmesh ({existing} existing).");
            }
        }

        if (notWritten.Count > 0)
            sb.AppendLine($" - Not written to the field (plan JSON only): {string.Join(", ", notWritten)}.");

        return sb.ToString().TrimEnd();
    }

    private sealed record TextEntry(string Text, EventStep? Step);

    private static ApplyResult ApplyToPcField(
        ScenePlan plan,
        FieldPC field,
        string groupName,
        string summary,
        ApplyOptions opts)
    {
        var section = field.ScriptsAndTexts;
        if (section == null)
            return ApplyResult.Failure("Active field does not have a parsed Section 1 text table.");

        var entries = CollectDialogue(plan);
        if (section.TextCount + entries.Count > Section1File.MaxTextCount)
        {
            return ApplyResult.Failure(
                $"Applying this plan would exceed the Section 1 text limit of {Section1File.MaxTextCount}.");
        }

        var startIndex = section.TextCount;

        // Compile before changing anything, so a failure cannot leave texts without their script.
        var compiled = new CompiledScene();
        string? scriptGroupName = null;
        if (opts.ScriptMode == ScriptMode.AppendGroup)
        {
            compiled = CompileEvents(plan, entries, startIndex);
            if (!compiled.IsEmpty)
            {
                if (section.GrpScripts.Count >= Section1File.MaxGrpScriptCount)
                    return ApplyResult.Failure($"The field already has the maximum of {Section1File.MaxGrpScriptCount} script groups.");

                scriptGroupName = UniqueGroupName(section, opts.GroupNameOverride);
            }
        }

        if (entries.Count == 0 && compiled.IsEmpty)
        {
            field.SetModified();
            var nothing = new StringBuilder($"{summary}{Environment.NewLine}No dialogue text was added.");
            foreach (var skip in compiled.Skipped)
                nothing.Append(Environment.NewLine).Append($"  skipped: {skip}");
            return ApplyResult.Success(groupName, nothing.ToString());
        }

        // Texts first (ids are stable because they are appended), then the group that references them.
        for (var index = 0; index < entries.Count; index++)
            field.InsertText(startIndex + index, new FF7String(entries[index].Text));

        var applySummary = new StringBuilder(summary);
        if (entries.Count > 0)
        {
            applySummary.Append(Environment.NewLine).Append(
                $"Added {entries.Count} dialogue text entr{(entries.Count == 1 ? "y" : "ies")} starting at text {startIndex}.");
        }

        if (scriptGroupName != null)
        {
            var group = new GrpScript(scriptGroupName);
            group.SetScript(0, new Script(compiled.Script));
            section.AppendGrpScript(group);
            field.SetModified();
            applySummary.Append(Environment.NewLine).Append(
                $"Added script group '{scriptGroupName}' with {compiled.StepsWritten} step(s) that run when the field loads.");
            groupName = scriptGroupName;
        }

        foreach (var skip in compiled.Skipped)
            applySummary.Append(Environment.NewLine).Append($"  skipped: {skip}");

        return ApplyResult.Success(groupName, applySummary.ToString());
    }

    private static CompiledScene CompileEvents(ScenePlan plan, IReadOnlyList<TextEntry> entries, int firstTextId)
    {
        var ids = new Dictionary<EventStep, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].Step is { } step)
                ids[step] = firstTextId + index;
        }

        return SceneScriptCompiler.Compile(plan, step => ids.TryGetValue(step, out var id) ? id : null);
    }

    /// <summary>A group name of at most 8 characters that no existing group uses.</summary>
    private static string UniqueGroupName(Section1File section, string? preferred)
    {
        var taken = new HashSet<string>(section.GrpScripts.Select(static g => g.Name), StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var trimmed = preferred.Trim();
            trimmed = trimmed.Length > 8 ? trimmed[..8] : trimmed;
            if (!taken.Contains(trimmed))
                return trimmed;
        }

        for (var n = 1; n < 1000; n++)
        {
            var candidate = $"ai_{n:00}";
            if (!taken.Contains(candidate))
                return candidate;
        }

        return "ai_new";
    }

    private static List<TextEntry> CollectDialogue(ScenePlan plan)
    {
        var entries = new List<TextEntry>();
        foreach (var line in plan.Dialog)
        {
            if (!string.IsNullOrWhiteSpace(line.Text))
                entries.Add(new TextEntry(line.Text, null));
        }

        foreach (var step in plan.Events.SelectMany(static item => FlattenSteps(item.Steps)))
        {
            if (step.Type == EventStepType.Say && !string.IsNullOrWhiteSpace(step.Text))
                entries.Add(new TextEntry(step.Text, step));
        }

        return entries;
    }

    private static IEnumerable<EventStep> FlattenSteps(IEnumerable<EventStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;
            foreach (var thenStep in FlattenSteps(step.ThenSteps))
                yield return thenStep;
            foreach (var elseStep in FlattenSteps(step.ElseSteps))
                yield return elseStep;
        }
    }
}

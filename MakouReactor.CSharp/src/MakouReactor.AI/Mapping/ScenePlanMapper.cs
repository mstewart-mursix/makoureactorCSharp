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
                Summary = $"{summary}{Environment.NewLine}{DescribeChanges(plan, field, opts.WalkmeshMode)}",
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

            var applyResult = ApplyDialogueToPcField(plan, pcField, groupName, summary);
            if (!applyResult.Ok || walkmesh == null)
                return applyResult;

            if (pcField.Walkmesh == null)
                pcField.ReplaceWalkmesh(new IdFile());

            var added = WalkmeshBuilder.Apply(pcField.Walkmesh!, walkmesh, opts.WalkmeshMode);
            pcField.ApplyWalkmeshChanges();
            var verb = opts.WalkmeshMode == WalkmeshMode.Replace ? "Replaced the walkmesh with" : "Added";
            return ApplyResult.Success(
                groupName,
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
    public static string DescribeChanges(ScenePlan plan, Field? field, WalkmeshMode walkmeshMode = WalkmeshMode.Ignore)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Changes to the field:");

        var texts = CollectDialogueTexts(plan).Count();
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
        if (plan.Events.Count > 0)
            notWritten.Add($"{plan.Events.Count} event(s) with {steps} step(s)");
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

    private static ApplyResult ApplyDialogueToPcField(
        ScenePlan plan,
        FieldPC field,
        string groupName,
        string summary)
    {
        if (field.ScriptsAndTexts == null)
            return ApplyResult.Failure("Active field does not have a parsed Section 1 text table.");

        var texts = CollectDialogueTexts(plan).ToArray();
        if (texts.Length == 0)
        {
            field.SetModified();
            return ApplyResult.Success(groupName, $"{summary}{Environment.NewLine}No dialogue text was added.");
        }

        if (field.ScriptsAndTexts.TextCount + texts.Length > Section1File.MaxTextCount)
        {
            return ApplyResult.Failure(
                $"Applying this plan would exceed the Section 1 text limit of {Section1File.MaxTextCount}.");
        }

        var startIndex = field.ScriptsAndTexts.TextCount;
        for (var index = 0; index < texts.Length; index++)
            field.InsertText(startIndex + index, new FF7String(texts[index]));

        var applySummary =
            $"{summary}{Environment.NewLine}" +
            $"Added {texts.Length} dialogue text entr{(texts.Length == 1 ? "y" : "ies")} starting at text {startIndex}.";
        return ApplyResult.Success(groupName, applySummary);
    }

    private static IEnumerable<string> CollectDialogueTexts(ScenePlan plan)
    {
        foreach (var line in plan.Dialog)
        {
            if (!string.IsNullOrWhiteSpace(line.Text))
                yield return line.Text;
        }

        foreach (var step in plan.Events.SelectMany(static item => FlattenSteps(item.Steps)))
        {
            if (step.Type == EventStepType.Say && !string.IsNullOrWhiteSpace(step.Text))
                yield return step.Text;
        }
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

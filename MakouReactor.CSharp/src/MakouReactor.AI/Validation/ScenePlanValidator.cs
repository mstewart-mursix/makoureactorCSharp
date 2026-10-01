using System;
using System.Collections.Generic;
using System.Linq;

using MakouReactor.AI.Layout;
using MakouReactor.Core.Models;

namespace MakouReactor.AI.Validation;

/// <summary>
/// Multi-pass safety validator for parsed scene plans.
/// Checks actor references, dialog text, event step correctness, and layout bounds.
/// </summary>
public static class ScenePlanValidator
{
    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Validate a <see cref="ScenePlan"/> against safety rules and current field bounds.
    /// </summary>
    public static ValidationResult Validate(ScenePlan plan, Field? field, ValidationOptions? opts = null)
    {
        var options = opts ?? new ValidationOptions();
        var issues = new List<Issue>();
        ValidateWalkmeshPlan(plan, issues);
        var mesh = LayoutGenerator.PlacementMesh(plan, field);

        ValidateActors(plan, field, mesh, issues);

        var actorIds = CollectActorIds(plan);
        ValidateDialog(plan, options, actorIds, issues);

        ValidateEvents(plan, field, mesh, options, issues);

        ValidateLayout(plan, field, mesh, issues);

        return new ValidationResult { Issues = issues };
    }

    // -----------------------------------------------------------------------
    // Proposed walkmesh validation (schema 1.1)
    // -----------------------------------------------------------------------

    private static void ValidateWalkmeshPlan(ScenePlan plan, List<Issue> out_)
    {
        var walkmesh = plan.Layout?.Walkmesh;
        if (walkmesh is null)
            return;

        var seen = new HashSet<string>();
        for (var i = 0; i < walkmesh.Regions.Count; i++)
        {
            var region = walkmesh.Regions[i];
            var path = $"layout.walkmesh.regions[{i}]";

            if (string.IsNullOrWhiteSpace(region.Id))
                Add(out_, Severity.Error, $"{path}.id", "Empty region id");
            else if (!seen.Add(region.Id))
                Add(out_, Severity.Error, $"{path}.id", $"Duplicate region id '{region.Id}'");

            var problem = WalkmeshBuilder.ValidatePolygon(region.Polygon);
            if (problem != null)
                Add(out_, Severity.Error, $"{path}.polygon", problem);
        }
    }

    // -----------------------------------------------------------------------
    // Actor validation
    // -----------------------------------------------------------------------

    private static void ValidateActors(ScenePlan plan, Field? field, WalkmeshGeometry? mesh, List<Issue> out_)
    {
        var bounds = mesh?.Bounds ?? LayoutGenerator.FieldBounds(field);
        var seen = new HashSet<string>();

        for (int i = 0; i < plan.Actors.Count; i++)
        {
            var a = plan.Actors[i];
            var path = $"actors[{i}]";

            if (string.IsNullOrWhiteSpace(a.Id))
                Add(out_, Severity.Error, $"{path}.id", "Empty actor id");
            else if (!seen.Add(a.Id))
                Add(out_, Severity.Error, $"{path}.id", $"Duplicate actor id '{a.Id}'");

            if (!bounds.Contains(a.Position))
                Add(out_, Severity.Warn, $"{path}.position",
                    $"Position out of bounds ({a.Position.X},{a.Position.Y})");
            else if (mesh is not null && !mesh.Contains(a.Position))
                Add(out_, Severity.Warn, $"{path}.position",
                    $"Position is not on the walkmesh ({a.Position.X},{a.Position.Y})");

            if (a.Facing.HasValue && !IsFaceDir(a.Facing.Value))
                Add(out_, Severity.Warn, $"{path}.facing",
                    $"Invalid facing '{a.Facing.Value}' (expected N/S/E/W)");
        }
    }

    // -----------------------------------------------------------------------
    // Dialog validation
    // -----------------------------------------------------------------------

    private static void ValidateDialog(ScenePlan plan, ValidationOptions opts, HashSet<string> actorIds, List<Issue> out_)
    {
        for (int i = 0; i < plan.Dialog.Count; i++)
        {
            var d = plan.Dialog[i];
            var path = $"dialog[{i}]";

            if (d.SpeakerId != "narrator" && !actorIds.Contains(d.SpeakerId))
                Add(out_, Severity.Error, $"{path}.speakerId",
                    $"Unknown speaker '{d.SpeakerId}'");

            CheckText(out_, $"{path}.text", d.Text, opts.MaxLineLength,
                      opts.ProfanityFilter, opts.BannedWords);
        }
    }

    // -----------------------------------------------------------------------
    // Event validation (with recursive IfFlag support)
    // -----------------------------------------------------------------------

    private static void ValidateEvents(ScenePlan plan, Field? field, WalkmeshGeometry? mesh,
                                       ValidationOptions opts, List<Issue> out_)
    {
        var bounds = mesh?.Bounds ?? LayoutGenerator.FieldBounds(field);
        var actorIds = CollectActorIds(plan);

        for (int i = 0; i < plan.Events.Count; i++)
        {
            var e = plan.Events[i];
            var epath = $"events[{i}]";

            if (e.Trigger == "zone" && !bounds.Contains(e.TriggerZone))
                Add(out_, Severity.Error, $"{epath}.triggerZone",
                    $"Trigger zone out of bounds ({e.TriggerZone.X},{e.TriggerZone.Y} {e.TriggerZone.Width}x{e.TriggerZone.Height})");

            for (int j = 0; j < e.Steps.Count; j++)
            {
                var s = e.Steps[j];
                var spath = $"{epath}.steps[{j}]";
                ValidateStep(s, spath, actorIds, bounds, mesh, opts, out_);
            }
        }
    }

    private static void ValidateStep(EventStep s, string spath, HashSet<string> actorIds,
                                     Rect bounds, WalkmeshGeometry? mesh, ValidationOptions opts, List<Issue> out_)
    {
        switch (s.Type)
        {
            case EventStepType.Say:
                if (!actorIds.Contains(s.ActorId) && s.ActorId != "narrator")
                    Add(out_, Severity.Error, $"{spath}.actorId", $"Unknown actor '{s.ActorId}'");
                CheckText(out_, $"{spath}.text", s.Text, opts.MaxLineLength,
                          opts.ProfanityFilter, opts.BannedWords);
                break;

            case EventStepType.Move:
                if (!actorIds.Contains(s.ActorId))
                    Add(out_, Severity.Error, $"{spath}.actorId", $"Unknown actor '{s.ActorId}'");
                if (!bounds.Contains(s.To))
                    Add(out_, Severity.Warn, $"{spath}.to",
                        $"Move target out of bounds ({s.To.X},{s.To.Y})");
                else if (mesh is not null && !mesh.Contains(s.To))
                    Add(out_, Severity.Warn, $"{spath}.to",
                        $"Move target is not on the walkmesh ({s.To.X},{s.To.Y})");
                break;

            case EventStepType.Face:
                if (!actorIds.Contains(s.ActorId))
                    Add(out_, Severity.Error, $"{spath}.actorId", $"Unknown actor '{s.ActorId}'");
                break;

            case EventStepType.Wait:
                if (s.Ms < 0)
                    Add(out_, Severity.Error, $"{spath}.ms", "Negative wait time");
                break;

            case EventStepType.PlayMusic:
                if (string.IsNullOrWhiteSpace(s.Track))
                    Add(out_, Severity.Warn, $"{spath}.track", "Empty music track id");
                break;

            case EventStepType.SetFlag:
                if (string.IsNullOrWhiteSpace(s.Key))
                    Add(out_, Severity.Error, $"{spath}.key", "Empty flag key");
                break;

            case EventStepType.IfFlag:
                if (string.IsNullOrWhiteSpace(s.Key))
                    Add(out_, Severity.Error, $"{spath}.key", "Empty flag key");

                // Recursively validate nested then/else branches with inherited bounds
                ValidateNestedSteps(s.ThenSteps, $"{spath}.then", out_, opts, actorIds, bounds, mesh);
                ValidateNestedSteps(s.ElseSteps, $"{spath}.else", out_, opts, actorIds, bounds, mesh);
                break;

            case EventStepType.GiveItem:
                if (s.Qty <= 0)
                    Add(out_, Severity.Error, $"{spath}.qty", "Quantity must be > 0");
                if (string.IsNullOrWhiteSpace(s.ItemId))
                    Add(out_, Severity.Error, $"{spath}.itemId", "Empty item id");
                break;

            case EventStepType.Battle:
                if (string.IsNullOrWhiteSpace(s.EncounterId))
                    Add(out_, Severity.Error, $"{spath}.encounterId", "Empty encounter id");
                break;

            case EventStepType.CustomNote:
                CheckText(out_, $"{spath}.text", s.Text, opts.MaxLineLength,
                          opts.ProfanityFilter, opts.BannedWords);
                break;
        }
    }

    /// <summary>
    /// Recursively validate nested IfFlag branches, rewriting the path prefix.
    /// </summary>
    private static void ValidateNestedSteps(List<EventStep> steps, string prefix,
                                            List<Issue> out_, ValidationOptions opts, HashSet<string> actorIds,
                                            Rect bounds, WalkmeshGeometry? mesh)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            var nestedPath = $"{prefix}[{i}]";
            ValidateStep(s, nestedPath, actorIds, bounds, mesh, opts, out_);
        }
    }

    // -----------------------------------------------------------------------
    // Layout validation
    // -----------------------------------------------------------------------

    private static void ValidateLayout(ScenePlan plan, Field? field, WalkmeshGeometry? mesh, List<Issue> out_)
    {
        var bounds = mesh?.Bounds ?? LayoutGenerator.FieldBounds(field);

        if (!bounds.Contains(plan.Layout.SpawnPoint))
            Add(out_, Severity.Warn, "layout.spawnPoint",
                $"Spawn point out of bounds ({plan.Layout.SpawnPoint.X},{plan.Layout.SpawnPoint.Y})");
        else if (mesh is not null && !mesh.Contains(plan.Layout.SpawnPoint))
            Add(out_, Severity.Warn, "layout.spawnPoint",
                $"Spawn point is not on the walkmesh ({plan.Layout.SpawnPoint.X},{plan.Layout.SpawnPoint.Y})");

        for (int i = 0; i < plan.Layout.Props.Count; i++)
        {
            var p = plan.Layout.Props[i];
            if (!bounds.Contains(p.Position))
                Add(out_, Severity.Warn, $"layout.props[{i}].position",
                    $"Prop '{p.Id}' out of bounds ({p.Position.X},{p.Position.Y})");
            else if (mesh is not null && !mesh.Contains(p.Position))
                Add(out_, Severity.Warn, $"layout.props[{i}].position",
                    $"Prop '{p.Id}' is not on the walkmesh ({p.Position.X},{p.Position.Y})");
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static HashSet<string> CollectActorIds(ScenePlan plan)
    {
        var ids = new HashSet<string>();
        foreach (var a in plan.Actors)
            ids.Add(a.Id);
        return ids;
    }

    private static void CheckText(List<Issue> out_, string path, string text, int maxLen,
                                  bool profanityFilter, List<string> banned)
    {
        // Control chars except TAB/CR/LF
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c < '\u0020' && c != '\n' && c != '\r' && c != '\t')
            {
                Add(out_, Severity.Error, path, $"Text contains control character at index {i}");
                break;
            }
        }

        // Line length
        var lines = text.Split('\n');
        for (int li = 0; li < lines.Length; li++)
        {
            if (lines[li].Length > maxLen)
                Add(out_, Severity.Error, path,
                    $"Line {li + 1} exceeds {maxLen} characters ({lines[li].Length})");
        }

        // Profanity
        if (profanityFilter && banned is { Count: > 0 })
        {
            var lower = text.ToLowerInvariant();
            foreach (var w in banned)
            {
                if (!string.IsNullOrEmpty(w) && lower.Contains(w.ToLowerInvariant()))
                    Add(out_, Severity.Warn, path, $"Contains flagged term: '{w}'");
            }
        }
    }

    private static bool IsFaceDir(char c) => c is 'N' or 'S' or 'E' or 'W';

    private static void Add(List<Issue> v, Severity lvl, string path, string msg)
    {
        v.Add(new Issue { Level = lvl, Path = path, Message = msg });
    }
}

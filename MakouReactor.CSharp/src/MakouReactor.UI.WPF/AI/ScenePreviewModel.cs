using System;
using System.Collections.Generic;
using System.Linq;

using MakouReactor.AI.Layout;
using MakouReactor.Core.Models;
using MakouReactor.Core.Services;

using Point = MakouReactor.Core.Models.Point;
using Rect = MakouReactor.Core.Models.Rect;

namespace MakouReactor.UI.WPF.AI;

/// <summary>A planned actor marker: id label, position, and whether it sits on the walkmesh.</summary>
public sealed record PreviewActor(string Id, Point Position, bool OnWalkmesh);

/// <summary>A labelled point (prop or move target).</summary>
public sealed record PreviewPoint(string Label, Point Position);

/// <summary>A line segment in field coordinates (gateway/exit lines).</summary>
public sealed record PreviewLine(Point A, Point B, string Label);

/// <summary>
/// Everything the scene preview draws, in field coordinates (walkmesh X/Y when the field has a
/// walkmesh, otherwise 320x240 screen pixels). Built from <see cref="Field"/> + <see cref="ScenePlan"/>
/// only; pure and unit-testable.
/// </summary>
public sealed class ScenePreviewModel
{
    public IReadOnlyList<Point[]> Triangles { get; init; } = Array.Empty<Point[]>();
    public IReadOnlyList<PreviewLine> Exits { get; init; } = Array.Empty<PreviewLine>();

    /// <summary>
    /// The field's existing entities at their initial constant placements (drawn gray). Only filled
    /// when the field has a walkmesh, because placements are field coordinates.
    /// </summary>
    public IReadOnlyList<PreviewPoint> ExistingEntities { get; init; } = Array.Empty<PreviewPoint>();
    public IReadOnlyList<PreviewActor> Actors { get; init; } = Array.Empty<PreviewActor>();
    public IReadOnlyList<PreviewPoint> Props { get; init; } = Array.Empty<PreviewPoint>();
    public IReadOnlyList<PreviewPoint> MoveTargets { get; init; } = Array.Empty<PreviewPoint>();
    public IReadOnlyList<Rect> TriggerZones { get; init; } = Array.Empty<Rect>();
    public Point? Spawn { get; init; }

    /// <summary>Area to fit on screen: placement bounds grown to include every drawn point.</summary>
    public Rect Extent { get; init; } = LayoutGenerator.DefaultBounds;

    /// <summary>Build the preview for <paramref name="plan"/> on <paramref name="field"/> (either may be null).</summary>
    public static ScenePreviewModel Build(Field? field, ScenePlan? plan)
    {
        var walkmesh = LayoutGenerator.WalkmeshOf(field);
        bool OnMesh(Point p) => walkmesh == null || WalkmeshGeometry.Contains(walkmesh, p);

        var triangles = walkmesh?.Triangles
            .Select(t => t.Vertices.Select(v => new Point(v.X, v.Y)).ToArray())
            .ToList() ?? new List<Point[]>();

        var exits = field?.Inf?.ExitLines
            .Where(e => e.FieldId != 0x7FFF && !(e.LineA.X == 0 && e.LineA.Y == 0 && e.LineB.X == 0 && e.LineB.Y == 0))
            .Select(e => new PreviewLine(new Point(e.LineA.X, e.LineA.Y), new Point(e.LineB.X, e.LineB.Y), $"#{e.FieldId}"))
            .ToList() ?? new List<PreviewLine>();

        var existing = walkmesh == null
            ? new List<PreviewPoint>()
            : FieldEntityLocator.Locate(field?.ScriptsAndTexts)
                .Select(e => new PreviewPoint(e.GroupName, new Point(e.X, e.Y)))
                .ToList();

        var actors = plan?.Actors.Select(a => new PreviewActor(a.Id, a.Position, OnMesh(a.Position))).ToList()
                     ?? new List<PreviewActor>();
        var props = plan?.Layout?.Props.Select(p => new PreviewPoint(p.Id, p.Position)).ToList()
                    ?? new List<PreviewPoint>();
        var zones = plan?.Events.Where(e => e.Trigger == "zone").Select(e => (Rect)e.TriggerZone).ToList()
                    ?? new List<Rect>();
        var moves = new List<PreviewPoint>();
        if (plan != null)
            foreach (var e in plan.Events)
                CollectMoves(e.Steps, moves);

        var points = triangles.SelectMany(t => t)
            .Concat(exits.SelectMany(e => new[] { e.A, e.B }))
            .Concat(existing.Select(e => e.Position))
            .Concat(actors.Select(a => a.Position))
            .Concat(props.Select(p => p.Position))
            .Concat(moves.Select(m => m.Position))
            .Concat(zones.SelectMany(z => new[] { new Point(z.X, z.Y), new Point(z.X + z.W, z.Y + z.H) }))
            .ToList();
        if (plan?.Layout != null)
            points.Add(plan.Layout.SpawnPoint);

        return new ScenePreviewModel
        {
            Triangles = triangles,
            Exits = exits,
            ExistingEntities = existing,
            Actors = actors,
            Props = props,
            MoveTargets = moves,
            TriggerZones = zones,
            Spawn = plan?.Layout?.SpawnPoint,
            Extent = Grow(LayoutGenerator.FieldBounds(field), points),
        };
    }

    /// <summary>
    /// Uniform scale + offset that fits <see cref="Extent"/> into a <paramref name="width"/> x
    /// <paramref name="height"/> canvas with <paramref name="margin"/>, centred, aspect preserved.
    /// </summary>
    public (double Scale, double OffsetX, double OffsetY) Fit(double width, double height, double margin = 12)
    {
        var w = Math.Max(1, Extent.W);
        var h = Math.Max(1, Extent.H);
        var scale = Math.Max(0.0001, Math.Min((width - 2 * margin) / w, (height - 2 * margin) / h));
        var offsetX = (width - w * scale) / 2 - Extent.X * scale;
        var offsetY = (height - h * scale) / 2 - Extent.Y * scale;
        return (scale, offsetX, offsetY);
    }

    private static void CollectMoves(IEnumerable<EventStep> steps, List<PreviewPoint> into)
    {
        foreach (var s in steps)
        {
            if (s.Type == EventStepType.Move)
                into.Add(new PreviewPoint($"{s.ActorId}→", s.To));
            CollectMoves(s.ThenSteps, into);
            CollectMoves(s.ElseSteps, into);
        }
    }

    private static Rect Grow(Rect r, IEnumerable<Point> points)
    {
        int minX = r.X, minY = r.Y, maxX = r.X + r.W, maxY = r.Y + r.H;
        foreach (var p in points)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}

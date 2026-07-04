using System;
using System.Collections.Generic;

using MakouReactor.Core.Models;

namespace MakouReactor.AI.Layout;

/// <summary>
/// Adjusts actor and prop positions to be inside bounds, avoid overlaps,
/// and optionally snap to walkmesh triangle centroids.
/// </summary>
public static class LayoutGenerator
{
    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Compute field pixel bounds; falls back to 320x240 if unavailable.
    /// NOTE: Abstract Field has no Bounds property, so we always use the default.
    /// </summary>
    public static Rect FieldBounds(Field? field)
    {
        // The abstract Field class has no Bounds property; use default FF7 resolution.
        return new Rect(0, 0, 320, 240);
    }

    /// <summary>
    /// Adjust placements in-place in the <see cref="ScenePlan"/>.
    /// Returns a summary of changes performed.
    /// </summary>
    public static LayoutResult Adjust(ScenePlan plan, Field? field, LayoutOptions? opts = null)
    {
        var notes = new List<string>();
        var bounds = FieldBounds(field);
        var minDistancePx = opts?.MinDistancePx ?? 24;
        var enableWalkmeshSnap = opts?.EnableWalkmeshSnap ?? false;
        var nudgeStepPx = opts?.NudgeStepPx ?? 8;
        var maxNudgeTries = opts?.MaxNudgeTries ?? 200;

        // Ensure Layout exists
        if (plan.Layout == null)
        {
            plan.Layout = new LayoutDef { SpawnPoint = new Point(160, 120) };
            notes.Add("Created default layout with center spawn point");
        }

        // --- Adjust spawn point ---
        if (!bounds.Contains(plan.Layout.SpawnPoint))
        {
            var before = plan.Layout.SpawnPoint;
            plan.Layout.SpawnPoint = ClampPoint(plan.Layout.SpawnPoint, bounds);
            notes.Add($"Clamped spawnPoint from {before.X},{before.Y} to {plan.Layout.SpawnPoint.X},{plan.Layout.SpawnPoint.Y}");
        }

        // --- Clamp props and record as obstacles ---
        var obstacles = new List<Point>();
        var props = plan.Layout.Props;
        if (props != null)
        {
            obstacles.Capacity = props.Count;
            foreach (var prop in props)
            {
                if (!bounds.Contains(prop.Position))
                {
                    var before = prop.Position;
                    prop.Position = ClampPoint(prop.Position, bounds);
                    notes.Add($"Clamped prop '{prop.Id}' from {before.X},{before.Y} to {prop.Position.X},{prop.Position.Y}");
                }
                obstacles.Add(prop.Position);
            }
        }

        // --- Ensure spawn does not intersect props ---
        foreach (var ob in obstacles)
        {
            if (DistSq(plan.Layout.SpawnPoint, ob) < (double)minDistancePx * minDistancePx)
            {
                var before = plan.Layout.SpawnPoint;
                var obstacleList = new List<Point>(obstacles);
                plan.Layout.SpawnPoint = NudgeToFree(plan.Layout.SpawnPoint, bounds, obstacleList,
                    minDistancePx, nudgeStepPx, maxNudgeTries);
                notes.Add($"Nudged spawnPoint from {before.X},{before.Y} to {plan.Layout.SpawnPoint.X},{plan.Layout.SpawnPoint.Y} to avoid props");
                break;
            }
        }

        // --- Adjust actors: clamp -> optional walkmesh snap -> avoid overlaps ---
        var placed = new List<Point>(plan.Actors?.Count ?? 0);
        var actors = plan.Actors ?? new List<Actor>();

        foreach (var actor in actors)
        {
            var pos = actor.Position;

            // Clamp to bounds
            if (!bounds.Contains(pos))
            {
                var before = pos;
                pos = ClampPoint(pos, bounds);
                notes.Add($"Clamped actor '{actor.Id}' from {before.X},{before.Y} to {pos.X},{pos.Y}");
            }

            // Snap to walkmesh centroid if enabled and available
            if (enableWalkmeshSnap && HasWalkmesh(field))
            {
                var before = pos;
                pos = SnapToWalkmesh(pos, field);
                if (pos != before)
                {
                    pos = ClampPoint(pos, bounds); // clamp again after snap
                    notes.Add($"Snapped actor '{actor.Id}' to walkmesh at {pos.X},{pos.Y}");
                }
            }

            // Avoid overlaps with already placed actors, props, and spawn
            var occupied = new List<Point>(placed.Count + obstacles.Count + 1);
            occupied.AddRange(placed);
            occupied.AddRange(obstacles);
            occupied.Add(plan.Layout.SpawnPoint);

            var freePos = NudgeToFree(pos, bounds, occupied,
                minDistancePx, nudgeStepPx, maxNudgeTries);

            if (freePos != pos)
                notes.Add($"Nudged actor '{actor.Id}' from {pos.X},{pos.Y} to {freePos.X},{freePos.Y} to avoid overlaps");

            actor.Position = freePos;
            placed.Add(freePos);
        }

        return new LayoutResult { Ok = true, Notes = notes };
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static double DistSq(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (double)dx * dx + (double)dy * dy;
    }

    private static Point ClampPoint(Point p, Rect bounds)
    {
        var x = Math.Clamp(p.X, bounds.X, bounds.X + bounds.Width);
        var y = Math.Clamp(p.Y, bounds.Y, bounds.Y + bounds.Height);
        return new Point(x, y);
    }

    private static bool HasWalkmesh(Field? field)
    {
        return field?.Walkmesh?.HasTriangle == true;
    }

    /// <summary>
    /// Spiral search to find a free position near <paramref name="start"/>
    /// that is at least <paramref name="minDist"/> away from all <paramref name="placed"/> points
    /// and within <paramref name="bounds"/>.
    /// </summary>
    private static Point NudgeToFree(Point start, Rect bounds, List<Point> placed,
                                     int minDist, int step, int maxTries)
    {
        var minDist2 = (double)minDist * minDist;

        bool IsFree(Point pt)
        {
            foreach (var q in placed)
                if (DistSq(pt, q) < minDist2)
                    return false;
            return bounds.Contains(pt);
        }

        if (IsFree(start))
            return start;

        var p = start;
        var dirIndex = 0;
        var dirs = new[]
        {
            new Point(1, 0), new Point(1, 1), new Point(0, 1), new Point(-1, 1),
            new Point(-1, 0), new Point(-1, -1), new Point(0, -1), new Point(1, -1)
        };

        var tries = 0;
        var legLen = 1;
        var legProgress = 0;
        var legRepeats = 0;

        while (tries++ < maxTries)
        {
            var d = dirs[dirIndex];
            p = new Point(p.X + d.X * step, p.Y + d.Y * step);
            p = ClampPoint(p, bounds);

            if (IsFree(p))
                return p;

            if (++legProgress >= legLen)
            {
                legProgress = 0;
                dirIndex = (dirIndex + 1) % 8;
                if (++legRepeats == 2)
                {
                    legRepeats = 0;
                    legLen++;
                }
            }
        }

        // Give up: return start clamped
        return ClampPoint(start, bounds);
    }

    /// <summary>
    /// Brute-force search for the nearest walkmesh triangle centroid to <paramref name="p"/>.
    /// Centroid is computed from the average of the three VertexSR positions.
    /// </summary>
    private static Point SnapToWalkmesh(Point p, Field? field)
    {
        var idFile = field?.Walkmesh;
        if (idFile is null || idFile.Triangles.Count == 0)
            return p;

        double bestD2 = double.MaxValue;
        var best = p;

        foreach (var triangle in idFile.Triangles)
        {
            // Compute centroid from the three vertices (X, Y coordinates only)
            var verts = triangle.Vertices;
            var cx = (verts[0].X + verts[1].X + verts[2].X) / 3;
            var cy = (verts[0].Y + verts[1].Y + verts[2].Y) / 3;
            var centroid = new Point(cx, cy);

            var d2 = DistSq(centroid, p);
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = centroid;
            }
        }

        return best;
    }
}

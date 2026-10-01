using System;
using System.Collections.Generic;
using System.Linq;

using MakouReactor.Core.Models;

namespace MakouReactor.AI.Layout;

/// <summary>
/// Turns LLM-proposed walkable polygons into walkmesh triangles: validation of simple polygons,
/// ear-clipping triangulation, and edge adjacency (<see cref="IdFile.Access"/>).
/// </summary>
public static class WalkmeshBuilder
{
    /// <summary>Largest vertex coordinate magnitude representable in the walkmesh (signed 16-bit).</summary>
    public const int MaxCoordinate = short.MaxValue;

    /// <summary>
    /// Returns a human-readable problem with the polygon, or null when it can be triangulated
    /// (at least 3 distinct points, non-zero area, no self-intersection, coordinates fit 16 bits).
    /// </summary>
    public static string? ValidatePolygon(IReadOnlyList<Point> polygon)
    {
        var points = Normalize(polygon);
        if (points.Count < 3)
            return "Polygon needs at least 3 distinct points";

        if (points.Any(static p => Math.Abs((long)p.X) > MaxCoordinate || Math.Abs((long)p.Y) > MaxCoordinate))
            return $"Polygon coordinates must be within +/-{MaxCoordinate}";

        // Check simplicity first: a bow tie has zero net area but is really a self-intersection.
        if (!IsSimple(points))
            return SignedArea2(points) == 0 && IsCollinear(points)
                ? "Polygon has zero area"
                : "Polygon is self-intersecting";

        if (SignedArea2(points) == 0)
            return "Polygon has zero area";

        return null;
    }

    /// <summary>
    /// Ear-clipping triangulation. Result triangles are counter-clockwise in (x, y).
    /// </summary>
    /// <exception cref="ArgumentException">The polygon is invalid (see <see cref="ValidatePolygon"/>).</exception>
    public static IReadOnlyList<(Point A, Point B, Point C)> Triangulate(IReadOnlyList<Point> polygon)
    {
        var problem = ValidatePolygon(polygon);
        if (problem != null)
            throw new ArgumentException(problem, nameof(polygon));

        var points = Normalize(polygon);
        if (SignedArea2(points) < 0)
            points.Reverse();

        var remaining = Enumerable.Range(0, points.Count).ToList();
        var triangles = new List<(Point, Point, Point)>(points.Count - 2);

        var guard = points.Count * points.Count + 8;
        while (remaining.Count > 3 && guard-- > 0)
        {
            var clipped = false;
            for (var i = 0; i < remaining.Count; i++)
            {
                var prev = remaining[(i + remaining.Count - 1) % remaining.Count];
                var cur = remaining[i];
                var next = remaining[(i + 1) % remaining.Count];

                if (!IsEar(points, remaining, prev, cur, next))
                    continue;

                triangles.Add((points[prev], points[cur], points[next]));
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }

            if (clipped)
                continue;

            // No ear: drop a collinear vertex (zero-area sliver) so the loop can make progress.
            var collinear = FindCollinear(points, remaining);
            if (collinear < 0)
                throw new ArgumentException("Polygon could not be triangulated", nameof(polygon));

            remaining.RemoveAt(collinear);
        }

        if (remaining.Count == 3)
        {
            var a = points[remaining[0]];
            var b = points[remaining[1]];
            var c = points[remaining[2]];
            if (Cross(a, b, c) != 0)
                triangles.Add((a, b, c));
        }

        if (triangles.Count == 0)
            throw new ArgumentException("Polygon could not be triangulated", nameof(polygon));

        return triangles;
    }

    /// <summary>
    /// Triangulate every region and return the triangles as 2D geometry (used for validation and
    /// layout constraints before anything is applied). Returns null when no region triangulates.
    /// </summary>
    public static WalkmeshGeometry? ToGeometry(WalkmeshPlan? plan)
    {
        if (plan is null || plan.Regions.Count == 0)
            return null;

        var triangles = new List<((double, double), (double, double), (double, double))>();
        foreach (var region in plan.Regions)
        {
            if (ValidatePolygon(region.Polygon) != null)
                continue;

            foreach (var (a, b, c) in Triangulate(region.Polygon))
                triangles.Add(((a.X, a.Y), (b.X, b.Y), (c.X, c.Y)));
        }

        return WalkmeshGeometry.FromTriangles(triangles);
    }

    /// <summary>Build a fresh <see cref="IdFile"/> (triangles plus adjacency) from the plan's regions.</summary>
    /// <param name="plan">Proposed regions; every region must be valid.</param>
    /// <param name="groundZ">Z assigned to every generated vertex.</param>
    public static IdFile Build(WalkmeshPlan plan, short groundZ = 0)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var id = new IdFile();
        AppendTriangles(id, plan, groundZ);
        return id;
    }

    /// <summary>
    /// Combine the plan with <paramref name="target"/> per <paramref name="mode"/>. Returns the number
    /// of triangles added. <see cref="WalkmeshMode.Ignore"/> leaves the target unchanged.
    /// </summary>
    public static int Apply(IdFile target, WalkmeshPlan plan, WalkmeshMode mode, short? groundZ = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(plan);

        if (mode == WalkmeshMode.Ignore || plan.Regions.Count == 0)
            return 0;

        // Validate everything up front so a bad region never leaves a half-modified mesh.
        foreach (var region in plan.Regions)
        {
            var problem = ValidatePolygon(region.Polygon);
            if (problem != null)
                throw new ArgumentException($"Region '{region.Id}': {problem}", nameof(plan));
        }

        var z = groundZ ?? AverageGroundZ(target);
        if (mode == WalkmeshMode.Replace)
        {
            target.Triangles.Clear();
            target.Access.Clear();
        }

        var before = target.Triangles.Count;
        AppendTriangles(target, plan, z);
        return target.Triangles.Count - before;
    }

    /// <summary>
    /// Recompute neighbour links for triangles from <paramref name="firstTriangle"/> on, linking them to
    /// each other and to any existing triangle whose matching slot is still unset (-1).
    /// </summary>
    public static void ComputeAdjacency(IdFile walkmesh, int firstTriangle = 0)
    {
        ArgumentNullException.ThrowIfNull(walkmesh);

        while (walkmesh.Access.Count < walkmesh.Triangles.Count)
            walkmesh.Access.Add(new Access(-1, -1, -1));

        var edges = new Dictionary<((int, int), (int, int)), List<(int Triangle, int Slot)>>();
        for (var t = 0; t < walkmesh.Triangles.Count; t++)
        {
            var v = walkmesh.Triangles[t].Vertices;
            for (var slot = 0; slot < 3; slot++)
            {
                var a = (v[slot].X, (int)v[slot].Y);
                var b = (v[(slot + 1) % 3].X, (int)v[(slot + 1) % 3].Y);
                var key = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
                if (!edges.TryGetValue(key, out var list))
                    edges[key] = list = new List<(int, int)>();
                list.Add((t, slot));
            }
        }

        var access = walkmesh.Access.Select(static a => (short[])a.A.Clone()).ToArray();
        foreach (var shared in edges.Values.Where(static l => l.Count == 2))
        {
            var (t0, s0) = shared[0];
            var (t1, s1) = shared[1];
            if (t0 < firstTriangle && t1 < firstTriangle)
                continue;

            if (t0 >= firstTriangle || access[t0][s0] == -1)
                access[t0][s0] = (short)t1;
            if (t1 >= firstTriangle || access[t1][s1] == -1)
                access[t1][s1] = (short)t0;
        }

        for (var t = 0; t < access.Length; t++)
            walkmesh.Access[t] = new Access(access[t][0], access[t][1], access[t][2]);
    }

    // -----------------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------------

    private static void AppendTriangles(IdFile id, WalkmeshPlan plan, short z)
    {
        var first = id.Triangles.Count;
        foreach (var region in plan.Regions)
        {
            foreach (var (a, b, c) in Triangulate(region.Polygon))
            {
                id.Triangles.Add(new Triangle(Vertex(a, z), Vertex(b, z), Vertex(c, z)));
                id.Access.Add(new Access(-1, -1, -1));
            }
        }

        ComputeAdjacency(id, first);
    }

    private static VertexSR Vertex(Point p, short z) => new((short)p.X, (short)p.Y, z, z);

    private static short AverageGroundZ(IdFile target)
    {
        if (target.Triangles.Count == 0)
            return 0;

        var sum = 0L;
        foreach (var t in target.Triangles)
            sum += t.Vertices[0].Z + t.Vertices[1].Z + t.Vertices[2].Z;

        return (short)Math.Clamp(sum / (target.Triangles.Count * 3L), short.MinValue, short.MaxValue);
    }

    /// <summary>Drop a repeated closing point and consecutive duplicates.</summary>
    private static List<Point> Normalize(IReadOnlyList<Point> polygon)
    {
        var points = new List<Point>(polygon.Count);
        foreach (var p in polygon)
        {
            if (points.Count == 0 || points[^1] != p)
                points.Add(p);
        }

        while (points.Count > 1 && points[0] == points[^1])
            points.RemoveAt(points.Count - 1);

        return points;
    }

    private static bool IsCollinear(List<Point> points) =>
        points.All(p => Cross(points[0], points[1], p) == 0);

    private static long Cross(Point a, Point b, Point c) =>
        ((long)(b.X - a.X) * (c.Y - a.Y)) - ((long)(b.Y - a.Y) * (c.X - a.X));

    private static long SignedArea2(List<Point> points)
    {
        long sum = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            sum += ((long)a.X * b.Y) - ((long)b.X * a.Y);
        }

        return sum;
    }

    private static bool IsEar(List<Point> points, List<int> remaining, int prev, int cur, int next)
    {
        var a = points[prev];
        var b = points[cur];
        var c = points[next];
        if (Cross(a, b, c) <= 0)
            return false; // reflex or collinear (polygon is counter-clockwise here)

        foreach (var other in remaining)
        {
            if (other == prev || other == cur || other == next)
                continue;

            var p = points[other];
            if (p == a || p == b || p == c)
                continue;

            if (Cross(a, b, p) >= 0 && Cross(b, c, p) >= 0 && Cross(c, a, p) >= 0)
                return false;
        }

        return true;
    }

    private static int FindCollinear(List<Point> points, List<int> remaining)
    {
        for (var i = 0; i < remaining.Count; i++)
        {
            var prev = remaining[(i + remaining.Count - 1) % remaining.Count];
            var next = remaining[(i + 1) % remaining.Count];
            if (Cross(points[prev], points[remaining[i]], points[next]) == 0)
                return i;
        }

        return -1;
    }

    private static bool IsSimple(List<Point> points)
    {
        var n = points.Count;
        for (var i = 0; i < n; i++)
        {
            var a1 = points[i];
            var a2 = points[(i + 1) % n];
            for (var j = i + 1; j < n; j++)
            {
                var adjacent = j == i + 1 || (i == 0 && j == n - 1);
                var b1 = points[j];
                var b2 = points[(j + 1) % n];

                if (adjacent)
                {
                    // Neighbouring edges may only touch at their shared vertex (no folding back on themselves).
                    var shared = j == i + 1 ? a2 : a1;
                    var otherA = j == i + 1 ? a1 : a2;
                    var otherB = j == i + 1 ? b2 : b1;
                    if (Cross(otherA, shared, otherB) == 0 && Dot(shared, otherA, otherB) > 0)
                        return false;
                    continue;
                }

                if (SegmentsIntersect(a1, a2, b1, b2))
                    return false;
            }
        }

        return true;
    }

    /// <summary>Dot product of (a - origin) and (b - origin): positive means the two arms point the same way.</summary>
    private static long Dot(Point origin, Point a, Point b) =>
        ((long)(a.X - origin.X) * (b.X - origin.X)) + ((long)(a.Y - origin.Y) * (b.Y - origin.Y));

    private static bool SegmentsIntersect(Point p1, Point p2, Point p3, Point p4)
    {
        var d1 = Cross(p3, p4, p1);
        var d2 = Cross(p3, p4, p2);
        var d3 = Cross(p1, p2, p3);
        var d4 = Cross(p1, p2, p4);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;

        return (d1 == 0 && OnSegment(p3, p4, p1))
               || (d2 == 0 && OnSegment(p3, p4, p2))
               || (d3 == 0 && OnSegment(p1, p2, p3))
               || (d4 == 0 && OnSegment(p1, p2, p4));
    }

    private static bool OnSegment(Point a, Point b, Point p) =>
        p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) &&
        p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using MakouReactor.Core.Models;

namespace MakouReactor.AI.Layout;

/// <summary>
/// 2D (top-down X/Y) view of a field walkmesh: point-in-triangle tests, nearest walkable point,
/// bounds and a coarse ASCII occupancy grid. Plan coordinates are interpreted in the same units
/// as the walkmesh vertices.
/// </summary>
public sealed class WalkmeshGeometry
{
    private readonly (double X, double Y)[][] _triangles;

    private WalkmeshGeometry(IEnumerable<(double X, double Y)[]> triangles)
    {
        _triangles = triangles.ToArray();

        var xs = _triangles.SelectMany(static t => t).Select(static v => v.X).ToArray();
        var ys = _triangles.SelectMany(static t => t).Select(static v => v.Y).ToArray();
        MinX = xs.Min();
        MaxX = xs.Max();
        MinY = ys.Min();
        MaxY = ys.Max();

        var count = _triangles.Length * 3;
        Centroid = new Point(
            (int)Math.Round(xs.Sum() / count),
            (int)Math.Round(ys.Sum() / count));
    }

    /// <summary>Number of non-degenerate triangles.</summary>
    public int TriangleCount => _triangles.Length;

    public double MinX { get; }
    public double MaxX { get; }
    public double MinY { get; }
    public double MaxY { get; }

    /// <summary>Average of all triangle vertices.</summary>
    public Point Centroid { get; }

    /// <summary>Inclusive integer bounding box of the mesh as a <see cref="Rect"/>.</summary>
    public Rect Bounds => new(
        (int)Math.Floor(MinX),
        (int)Math.Floor(MinY),
        (int)Math.Ceiling(MaxX) - (int)Math.Floor(MinX) + 1,
        (int)Math.Ceiling(MaxY) - (int)Math.Floor(MinY) + 1);

    /// <summary>Builds geometry from a field's walkmesh, or null when it has none.</summary>
    public static WalkmeshGeometry? From(Field? field) => From(field?.Walkmesh);

    /// <summary>Builds geometry from an <see cref="IdFile"/>, or null when it has no usable triangle.</summary>
    public static WalkmeshGeometry? From(IdFile? walkmesh)
    {
        if (walkmesh is null || walkmesh.Triangles.Count == 0)
            return null;

        var triangles = walkmesh.Triangles
            .Select(static t => new[]
            {
                ((double)t.Vertices[0].X, (double)t.Vertices[0].Y),
                ((double)t.Vertices[1].X, (double)t.Vertices[1].Y),
                ((double)t.Vertices[2].X, (double)t.Vertices[2].Y),
            })
            .Where(static t => Math.Abs(Cross(t[0], t[1], t[2])) > double.Epsilon)
            .ToArray();

        return triangles.Length == 0 ? null : new WalkmeshGeometry(triangles);
    }

    /// <summary>Builds geometry from explicit 2D triangles (used for proposed walkmeshes).</summary>
    public static WalkmeshGeometry? FromTriangles(IEnumerable<((double X, double Y) A, (double X, double Y) B, (double X, double Y) C)> triangles)
    {
        var list = triangles
            .Select(static t => new[] { t.A, t.B, t.C })
            .Where(static t => Math.Abs(Cross(t[0], t[1], t[2])) > double.Epsilon)
            .ToArray();
        return list.Length == 0 ? null : new WalkmeshGeometry(list);
    }

    /// <summary>True when the point lies inside (or on the edge of) any triangle.</summary>
    public bool Contains(Point p) => Contains(p.X, p.Y);

    public bool Contains(double x, double y)
    {
        if (x < MinX || x > MaxX || y < MinY || y > MaxY)
            return false;

        foreach (var t in _triangles)
        {
            if (PointInTriangle((x, y), t[0], t[1], t[2]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The walkable point closest to <paramref name="p"/>: the point itself when already walkable,
    /// otherwise the closest point on any triangle edge.
    /// </summary>
    public Point Nearest(Point p)
    {
        if (Contains(p))
            return p;

        var best = (X: (double)p.X, Y: (double)p.Y);
        var bestD2 = double.MaxValue;
        foreach (var t in _triangles)
        {
            var candidate = ClosestPointOnTriangle((p.X, p.Y), t[0], t[1], t[2]);
            var dx = candidate.X - p.X;
            var dy = candidate.Y - p.Y;
            var d2 = (dx * dx) + (dy * dy);
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = candidate;
            }
        }

        var rounded = new Point((int)Math.Round(best.X), (int)Math.Round(best.Y));
        // Rounding can step just outside a thin triangle; fall back to the unrounded centroid-ish point.
        return Contains(rounded) ? rounded : NearestTriangleCentroid(p);
    }

    /// <summary>
    /// Coarse ASCII picture of the mesh ('#' walkable, '.' not). Row 0 is the minimum Y; Y grows downward.
    /// </summary>
    public string RenderAscii(int columns = 16, int rows = 12)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);

        var width = Math.Max(MaxX - MinX, 1);
        var height = Math.Max(MaxY - MinY, 1);
        var sb = new StringBuilder(rows * (columns + 1));
        for (var r = 0; r < rows; r++)
        {
            var y = MinY + ((r + 0.5) / rows * height);
            for (var c = 0; c < columns; c++)
            {
                var x = MinX + ((c + 0.5) / columns * width);
                sb.Append(Contains(x, y) ? '#' : '.');
            }

            if (r < rows - 1)
                sb.Append('\n');
        }

        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // Geometry primitives
    // -----------------------------------------------------------------------

    private Point NearestTriangleCentroid(Point p)
    {
        var best = Centroid;
        var bestD2 = double.MaxValue;
        foreach (var t in _triangles)
        {
            var cx = (t[0].X + t[1].X + t[2].X) / 3;
            var cy = (t[0].Y + t[1].Y + t[2].Y) / 3;
            var d2 = ((cx - p.X) * (cx - p.X)) + ((cy - p.Y) * (cy - p.Y));
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = new Point((int)Math.Round(cx), (int)Math.Round(cy));
            }
        }

        return best;
    }

    internal static double Cross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) =>
        ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));

    internal static bool PointInTriangle(
        (double X, double Y) p, (double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        var d1 = Cross(a, b, p);
        var d2 = Cross(b, c, p);
        var d3 = Cross(c, a, p);
        var hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    private static (double X, double Y) ClosestPointOnTriangle(
        (double X, double Y) p, (double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        if (PointInTriangle(p, a, b, c))
            return p;

        var best = ClosestPointOnSegment(p, a, b);
        var bestD2 = DistSq(p, best);
        foreach (var (s, e) in new[] { (b, c), (c, a) })
        {
            var candidate = ClosestPointOnSegment(p, s, e);
            var d2 = DistSq(p, candidate);
            if (d2 < bestD2)
            {
                best = candidate;
                bestD2 = d2;
            }
        }

        return best;
    }

    private static (double X, double Y) ClosestPointOnSegment(
        (double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var len2 = (abx * abx) + (aby * aby);
        if (len2 <= double.Epsilon)
            return a;

        var t = (((p.X - a.X) * abx) + ((p.Y - a.Y) * aby)) / len2;
        t = Math.Clamp(t, 0, 1);
        return (a.X + (t * abx), a.Y + (t * aby));
    }

    private static double DistSq((double X, double Y) a, (double X, double Y) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }
}

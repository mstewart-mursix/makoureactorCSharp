using System;

using MakouReactor.Core.Models;

namespace MakouReactor.AI.Layout;

/// <summary>
/// 2D geometry over walkmesh (<see cref="IdFile"/>) triangles, projected onto the X/Y floor plane
/// (Z is height). When a field with a walkmesh is supplied, scene-plan positions are interpreted
/// in these field coordinates.
/// </summary>
public static class WalkmeshGeometry
{
    /// <summary>True when <paramref name="walkmesh"/> is non-null and has at least one triangle.</summary>
    public static bool HasTriangles(IdFile? walkmesh) => walkmesh?.HasTriangle == true;

    /// <summary>
    /// Inclusive X/Y bounding box of all triangles as a half-open <see cref="Rect"/>
    /// (so every vertex satisfies <see cref="Rect.Contains(Point)"/>), or null without triangles.
    /// </summary>
    public static Rect? Bounds(IdFile? walkmesh)
    {
        if (!HasTriangles(walkmesh))
            return null;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var t in walkmesh!.Triangles)
        {
            foreach (var v in t.Vertices)
            {
                minX = Math.Min(minX, v.X);
                minY = Math.Min(minY, v.Y);
                maxX = Math.Max(maxX, v.X);
                maxY = Math.Max(maxY, v.Y);
            }
        }

        return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>True when <paramref name="p"/> lies inside or on the edge of any walkmesh triangle.</summary>
    public static bool Contains(IdFile walkmesh, Point p)
    {
        foreach (var t in walkmesh.Triangles)
            if (TriangleContains(t, p.X, p.Y))
                return true;
        return false;
    }

    /// <summary>
    /// The walkable point closest to <paramref name="p"/>: <paramref name="p"/> itself when already
    /// walkable, otherwise the nearest point on the nearest triangle, rounded to integer
    /// coordinates that are verified to be walkable.
    /// </summary>
    public static Point NearestWalkable(IdFile walkmesh, Point p)
    {
        if (!HasTriangles(walkmesh) || Contains(walkmesh, p))
            return p;

        double bestD2 = double.MaxValue;
        (double X, double Y) best = (p.X, p.Y);
        Triangle bestTriangle = walkmesh.Triangles[0];

        foreach (var t in walkmesh.Triangles)
        {
            var c = ClosestPointOnTriangle(t, p.X, p.Y);
            var d2 = Sq(c.X - p.X) + Sq(c.Y - p.Y);
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = c;
                bestTriangle = t;
            }
        }

        // Rounding can land just outside a thin triangle; walk toward its centroid until inside.
        var (cx, cy) = Centroid(bestTriangle);
        for (var i = 0; i <= 8; i++)
        {
            var f = i / 8.0;
            var candidate = new Point(
                (int)Math.Round(best.X + (cx - best.X) * f),
                (int)Math.Round(best.Y + (cy - best.Y) * f));
            if (Contains(walkmesh, candidate))
                return candidate;
        }

        return new Point((int)Math.Round(cx), (int)Math.Round(cy));
    }

    /// <summary>Area-weighted centroid of all triangles (falls back to vertex average for degenerate meshes).</summary>
    public static Point? Centroid(IdFile? walkmesh)
    {
        if (!HasTriangles(walkmesh))
            return null;

        double area = 0, sx = 0, sy = 0, vx = 0, vy = 0;
        foreach (var t in walkmesh!.Triangles)
        {
            var a = Math.Abs(SignedArea2(t)) / 2.0;
            var (cx, cy) = Centroid(t);
            area += a;
            sx += cx * a;
            sy += cy * a;
            vx += cx;
            vy += cy;
        }

        return area > 0
            ? new Point((int)Math.Round(sx / area), (int)Math.Round(sy / area))
            : new Point((int)Math.Round(vx / walkmesh.Triangles.Count), (int)Math.Round(vy / walkmesh.Triangles.Count));
    }

    /// <summary>Total walkable floor area (X/Y plane, square field units).</summary>
    public static double Area(IdFile? walkmesh)
    {
        if (!HasTriangles(walkmesh))
            return 0;
        double area = 0;
        foreach (var t in walkmesh!.Triangles)
            area += Math.Abs(SignedArea2(t)) / 2.0;
        return area;
    }

    // -----------------------------------------------------------------------
    // Triangle helpers
    // -----------------------------------------------------------------------

    internal static bool TriangleContains(Triangle t, double x, double y)
    {
        var v = t.Vertices;
        var d1 = Cross(v[0].X, v[0].Y, v[1].X, v[1].Y, x, y);
        var d2 = Cross(v[1].X, v[1].Y, v[2].X, v[2].Y, x, y);
        var d3 = Cross(v[2].X, v[2].Y, v[0].X, v[0].Y, x, y);

        var hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        if (hasNeg && hasPos)
            return false;

        // Degenerate (zero-area) triangles contain only points on their segment.
        if (!hasNeg && !hasPos)
            return OnSegment(v[0], v[1], x, y) || OnSegment(v[1], v[2], x, y) || OnSegment(v[2], v[0], x, y);

        return true;
    }

    private static (double X, double Y) ClosestPointOnTriangle(Triangle t, double x, double y)
    {
        if (TriangleContains(t, x, y))
            return (x, y);

        var v = t.Vertices;
        var best = ClosestPointOnSegment(v[0], v[1], x, y);
        var bestD2 = Sq(best.X - x) + Sq(best.Y - y);
        foreach (var (a, b) in new[] { (v[1], v[2]), (v[2], v[0]) })
        {
            var c = ClosestPointOnSegment(a, b, x, y);
            var d2 = Sq(c.X - x) + Sq(c.Y - y);
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = c;
            }
        }
        return best;
    }

    private static (double X, double Y) ClosestPointOnSegment(VertexSR a, VertexSR b, double x, double y)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        var len2 = dx * dx + dy * dy;
        if (len2 == 0)
            return (a.X, a.Y);
        var u = Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / len2, 0, 1);
        return (a.X + u * dx, a.Y + u * dy);
    }

    private static bool OnSegment(VertexSR a, VertexSR b, double x, double y)
    {
        var c = ClosestPointOnSegment(a, b, x, y);
        return Sq(c.X - x) + Sq(c.Y - y) < 1e-9;
    }

    private static (double X, double Y) Centroid(Triangle t)
    {
        var v = t.Vertices;
        return ((v[0].X + v[1].X + v[2].X) / 3.0, (v[0].Y + v[1].Y + v[2].Y) / 3.0);
    }

    private static double SignedArea2(Triangle t)
    {
        var v = t.Vertices;
        return Cross(v[0].X, v[0].Y, v[1].X, v[1].Y, v[2].X, v[2].Y);
    }

    private static double Cross(double ax, double ay, double bx, double by, double px, double py) =>
        (bx - ax) * (py - ay) - (by - ay) * (px - ax);

    private static double Sq(double v) => v * v;
}

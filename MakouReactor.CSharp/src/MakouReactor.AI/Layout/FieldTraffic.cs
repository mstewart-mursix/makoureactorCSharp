using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

using MakouReactor.Core.Models;

namespace MakouReactor.AI.Layout;

/// <summary>
/// Where things move in a field, so that new (solid) actors are not placed in their way: the paths
/// existing entities walk in their scripts (an <c>XYZI</c>/<c>XYI</c> start, then each
/// <c>MOVE</c>/<c>FMOVE</c>/<c>CMOVE</c> target), the field's <c>LINE</c> triggers and its exit
/// gateways. A new actor must stay <see cref="Clearance"/> away from all of them and must not cut
/// the walkmesh so that points which could reach each other no longer can (a narrow walkway with
/// a guard standing in it blocks both scripted characters and the player).
/// </summary>
public sealed class FieldTraffic
{
    /// <summary>
    /// Default distance kept between a new actor and any path, trigger or exit, in field units:
    /// room for the actor's own collision circle plus that of a character walking past, with margin.
    /// </summary>
    public const int DefaultClearance = 64;

    private const ushort UnusedFieldId = 0x7FFF;
    private const int GridCell = 8;

    private readonly IdFile? _walkmesh;
    private readonly List<(Point A, Point B)> _segments;
    private readonly List<Point> _keyPoints;

    private FieldTraffic(IdFile? walkmesh, List<(Point A, Point B)> segments, List<Point> keyPoints, int clearance)
    {
        _walkmesh = walkmesh;
        _segments = segments;
        _keyPoints = keyPoints;
        Clearance = clearance;
    }

    /// <summary>Distance kept from paths, triggers and exits.</summary>
    public int Clearance { get; }

    /// <summary>Path, trigger and exit segments (a standing entity is a zero-length segment).</summary>
    public IReadOnlyList<(Point A, Point B)> Segments => _segments;

    /// <summary>Points that must stay mutually reachable: path points, trigger and exit midpoints on the walkmesh.</summary>
    public IReadOnlyList<Point> KeyPoints => _keyPoints;

    /// <summary>True when the field has nothing to keep clear.</summary>
    public bool IsEmpty => _segments.Count == 0;

    /// <summary>Collect the traffic of <paramref name="field"/> (empty without Section 1 or exits).</summary>
    public static FieldTraffic Of(Field? field, IdFile? walkmesh, int clearance = DefaultClearance)
    {
        var segments = new List<(Point A, Point B)>();
        var keyPoints = new List<Point>();

        if (field?.ScriptsAndTexts is { } section)
        {
            foreach (var group in section.GrpScripts)
                AddGroupPaths(group, segments, keyPoints);
        }

        if (field?.Inf is { } inf)
        {
            foreach (var exit in inf.ExitLines)
            {
                if (exit.FieldId == UnusedFieldId || (exit.LineA.X == 0 && exit.LineA.Y == 0 && exit.LineB.X == 0 && exit.LineB.Y == 0))
                    continue;

                var a = new Point(exit.LineA.X, exit.LineA.Y);
                var b = new Point(exit.LineB.X, exit.LineB.Y);
                segments.Add((a, b));
                keyPoints.Add(new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2));
            }
        }

        if (walkmesh != null)
        {
            // Only points on the floor can anchor connectivity; off-mesh ones (gateways sit at the
            // edge) are pulled onto the nearest walkable point.
            for (var i = 0; i < keyPoints.Count; i++)
            {
                if (!WalkmeshGeometry.Contains(walkmesh, keyPoints[i]))
                    keyPoints[i] = WalkmeshGeometry.NearestWalkable(walkmesh, keyPoints[i]);
            }
        }

        return new FieldTraffic(walkmesh, segments, keyPoints.Distinct().ToList(), clearance);
    }

    /// <summary>Distance from <paramref name="p"/> to the nearest path, trigger or exit (infinity when none).</summary>
    public double DistanceToTraffic(Point p)
    {
        var best = double.PositiveInfinity;
        foreach (var (a, b) in _segments)
            best = Math.Min(best, DistanceToSegment(p, a, b));
        return best;
    }

    /// <summary>
    /// True when an actor at <paramref name="p"/> keeps <see cref="Clearance"/> from all traffic and,
    /// together with the actors already at <paramref name="others"/>, leaves every pair of key points
    /// that is connected on the empty walkmesh still connected.
    /// </summary>
    public bool IsClear(Point p, IReadOnlyList<Point> others) =>
        DistanceToTraffic(p) >= Clearance && KeepsConnected(others.Append(p).ToList());

    /// <summary>
    /// Nearest point to <paramref name="start"/> on the walkmesh that <see cref="IsClear"/> accepts and
    /// that is at least <paramref name="minDistance"/> from <paramref name="others"/>, searched on a grid
    /// in rings of growing distance. Null when the field has nowhere clear.
    /// </summary>
    public Point? FindClear(Point start, IReadOnlyList<Point> others, int minDistance)
    {
        if (_walkmesh == null)
            return null;

        var bounds = WalkmeshGeometry.Bounds(_walkmesh);
        if (bounds == null)
            return null;

        var b = bounds.Value;
        var maxRadius = Math.Max(b.Width, b.Height);
        var minSq = (double)minDistance * minDistance;
        for (var radius = 0; radius <= maxRadius; radius += GridCell)
        {
            // Cheap tests first; the flood fill runs only for survivors, nearest first, and the
            // first one that keeps the field connected wins.
            var candidates = Ring(start, radius)
                .Where(c => WalkmeshGeometry.Contains(_walkmesh, c)
                            && DistanceToTraffic(c) >= Clearance
                            && !others.Any(o => DistSq(o, c) < minSq))
                .OrderBy(c => DistSq(c, start));
            foreach (var candidate in candidates)
            {
                if (KeepsConnected(others.Append(candidate).ToList()))
                    return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<Point> Ring(Point c, int r)
    {
        if (r == 0)
        {
            yield return c;
            yield break;
        }

        for (var dx = -r; dx <= r; dx += GridCell)
        {
            yield return new Point(c.X + dx, c.Y - r);
            yield return new Point(c.X + dx, c.Y + r);
        }

        for (var dy = -r + GridCell; dy <= r - GridCell; dy += GridCell)
        {
            yield return new Point(c.X - r, c.Y + dy);
            yield return new Point(c.X + r, c.Y + dy);
        }
    }

    /// <summary>
    /// Flood-fill the walkmesh on a grid with a <see cref="Clearance"/> disc blocked around each actor,
    /// and require that key points connected without the actors stay connected with them.
    /// </summary>
    private bool KeepsConnected(IReadOnlyList<Point> actors)
    {
        if (_walkmesh == null || _keyPoints.Count < 2 || actors.Count == 0)
            return true;

        _baseline ??= Components(Array.Empty<Point>());
        var blocked = Components(actors);
        if (_baseline == null || blocked == null)
            return true;

        for (var i = 0; i < _keyPoints.Count; i++)
        {
            for (var j = i + 1; j < _keyPoints.Count; j++)
            {
                if (_baseline[i] < 0 || _baseline[i] != _baseline[j])
                    continue;

                // A key point covered by an actor's disc counts as cut off.
                if (blocked[i] < 0 || blocked[i] != blocked[j])
                    return false;
            }
        }

        return true;
    }

    private int[]? _baseline;
    private bool[]? _walkable;
    private int _cols, _rows, _cell;
    private Rect _gridBounds;

    private bool EnsureGrid()
    {
        if (_walkable != null)
            return true;
        if (WalkmeshGeometry.Bounds(_walkmesh) is not { } b)
            return false;

        // About 8 field units per cell, coarser for large fields so a check stays cheap (at most
        // ~128 cells along the longer side; still well under the 64-unit clearance on shipped fields).
        _cell = Math.Max(GridCell, Math.Max(b.Width, b.Height) / 128);
        _gridBounds = b;
        _cols = (b.Width / _cell) + 2;
        _rows = (b.Height / _cell) + 2;
        _walkable = new bool[_cols * _rows];
        for (var cy = 0; cy < _rows; cy++)
        {
            for (var cx = 0; cx < _cols; cx++)
                _walkable[(cy * _cols) + cx] = WalkmeshGeometry.Contains(_walkmesh!, CellCenter(cx, cy));
        }

        return true;
    }

    private Point CellCenter(int cx, int cy) => new(_gridBounds.X + (cx * _cell), _gridBounds.Y + (cy * _cell));

    /// <summary>Component id of each key point (-1 when blocked or off the walkmesh).</summary>
    private int[]? Components(IReadOnlyList<Point> actors)
    {
        if (!EnsureGrid())
            return null;

        var cells = new int[_walkable!.Length];
        for (var i = 0; i < cells.Length; i++)
            cells[i] = _walkable[i] ? 0 : -1;

        // Block each actor's disc, visiting only the cells under its bounding box.
        var radiusSq = (double)Clearance * Clearance;
        var reach = (Clearance / _cell) + 1;
        foreach (var a in actors)
        {
            var acx = (int)Math.Round((a.X - _gridBounds.X) / (double)_cell);
            var acy = (int)Math.Round((a.Y - _gridBounds.Y) / (double)_cell);
            for (var cy = Math.Max(0, acy - reach); cy <= Math.Min(_rows - 1, acy + reach); cy++)
            {
                for (var cx = Math.Max(0, acx - reach); cx <= Math.Min(_cols - 1, acx + reach); cx++)
                {
                    if (DistSq(a, CellCenter(cx, cy)) < radiusSq)
                        cells[(cy * _cols) + cx] = -1;
                }
            }
        }

        var label = 0;
        var queue = new Queue<int>();
        for (var start = 0; start < cells.Length; start++)
        {
            if (cells[start] != 0)
                continue;

            label++;
            cells[start] = label;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                int cx = cell % _cols, cy = cell / _cols;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= _cols || ny >= _rows)
                            continue;
                        var n = (ny * _cols) + nx;
                        if (cells[n] != 0)
                            continue;
                        cells[n] = label;
                        queue.Enqueue(n);
                    }
                }
            }
        }

        var result = new int[_keyPoints.Count];
        for (var i = 0; i < _keyPoints.Count; i++)
        {
            var kp = _keyPoints[i];
            var cx = (int)Math.Round((kp.X - _gridBounds.X) / (double)_cell);
            var cy = (int)Math.Round((kp.Y - _gridBounds.Y) / (double)_cell);
            result[i] = NearestLabel(cells, _cols, _rows, cx, cy);
        }

        return result;
    }

    /// <summary>The label at (cx, cy), or of a free neighbour (key points sit on cell corners), or -1.</summary>
    private static int NearestLabel(int[] cells, int cols, int rows, int cx, int cy)
    {
        for (var r = 0; r <= 1; r++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x >= 0 && y >= 0 && x < cols && y < rows && cells[(y * cols) + x] > 0)
                        return cells[(y * cols) + x];
                }
            }
        }

        return -1;
    }

    private static void AddGroupPaths(GrpScript group, List<(Point A, Point B)> segments, List<Point> keyPoints)
    {
        // The entity keeps its position between scripts (placed in init, walked from in another).
        Point? current = null;
        foreach (var script in group.Scripts)
        {
            foreach (var op in script.RawOpcodes)
            {
                var bytes = op.Bytes;
                switch (op.Name)
                {
                    // XYZI: id, banks(2), x, y, z, triangle. XYI: id, banks(2), x, y, triangle.
                    case "XYZI" or "XYI" when bytes.Length >= 7:
                        if (bytes[1] != 0 || bytes[2] != 0)
                        {
                            current = null; // position from variables: unknown
                            break;
                        }

                        current = ReadPoint(bytes, 3);
                        segments.Add((current.Value, current.Value));
                        keyPoints.Add(current.Value);
                        break;

                    // MOVE/FMOVE/CMOVE: id, banks(1), x, y.
                    case "MOVE" or "FMOVE" or "CMOVE" when bytes.Length >= 6:
                        if (bytes[1] != 0)
                        {
                            current = null;
                            break;
                        }

                        var target = ReadPoint(bytes, 2);
                        segments.Add((current ?? target, target));
                        keyPoints.Add(target);
                        current = target;
                        break;

                    // LINE: id, x1, y1, z1, x2, y2, z2 (a trigger the player crosses).
                    case "LINE" when bytes.Length >= 13:
                        var a = ReadPoint(bytes, 1);
                        var b = new Point(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(7, 2)),
                                          BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(9, 2)));
                        segments.Add((a, b));
                        keyPoints.Add(new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2));
                        break;
                }
            }
        }
    }

    private static Point ReadPoint(byte[] bytes, int offset) =>
        new(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset + 2, 2)));

    private static double DistSq(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double vx = b.X - a.X, vy = b.Y - a.Y;
        double wx = p.X - a.X, wy = p.Y - a.Y;
        var lenSq = (vx * vx) + (vy * vy);
        var t = lenSq == 0 ? 0 : Math.Clamp(((wx * vx) + (wy * vy)) / lenSq, 0, 1);
        double dx = wx - (t * vx), dy = wy - (t * vy);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}

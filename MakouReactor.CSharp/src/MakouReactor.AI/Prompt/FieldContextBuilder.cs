using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using MakouReactor.AI.Layout;
using MakouReactor.AI.Mapping;
using MakouReactor.Core.Models;

namespace MakouReactor.AI.Prompt;

/// <summary>
/// Describes a real field to the model as compact plain text: walkable area (bounds, centroid,
/// coarse ASCII occupancy grid), existing groups, sample dialog, and exits. Output is
/// deterministic and capped at a character budget so it fits comfortably in a prompt.
/// </summary>
public static class FieldContextBuilder
{
    /// <summary>Occupancy grid width in cells.</summary>
    public const int GridColumns = 16;

    /// <summary>Occupancy grid height in cells.</summary>
    public const int GridRows = 12;

    /// <summary>Maximum existing dialog lines quoted.</summary>
    public const int MaxDialogLines = 8;

    /// <summary>Maximum characters quoted per dialog line.</summary>
    public const int MaxDialogLineChars = 80;

    /// <summary>Gateway destination field id meaning "unused" in INF exit lines.</summary>
    private const ushort UnusedFieldId = 0x7FFF;

    /// <summary>
    /// Build the description. Sections are emitted in priority order (walkmesh first) and the
    /// result never exceeds <paramref name="maxChars"/>; lower-priority sections are dropped or
    /// truncated first.
    /// </summary>
    public static string Describe(Field field, int maxChars = 4000)
    {
        ArgumentNullException.ThrowIfNull(field);

        var sections = new List<string>
        {
            $"Field: {field.Name}",
            DescribeWalkmesh(field.Walkmesh),
            DescribeGroups(field.ScriptsAndTexts),
            DescribeExits(field.Inf),
            DescribeDialog(field.ScriptsAndTexts),
        };

        var sb = new StringBuilder();
        foreach (var section in sections.Where(s => s.Length > 0))
        {
            var separator = sb.Length == 0 ? string.Empty : "\n\n";
            var remaining = maxChars - sb.Length - separator.Length;
            if (remaining <= 0)
                break;

            if (section.Length <= remaining)
            {
                sb.Append(separator).Append(section);
                continue;
            }

            // Truncate on a line boundary and stop: later sections are lower priority.
            const string marker = "\n...";
            var budget = remaining - marker.Length;
            var cut = budget > 0 ? section.LastIndexOf('\n', Math.Min(budget, section.Length - 1)) : -1;
            if (cut > 0)
                sb.Append(separator).Append(section, 0, cut).Append(marker);
            break;
        }

        return sb.Length <= maxChars ? sb.ToString() : sb.ToString(0, maxChars);
    }

    // -----------------------------------------------------------------------
    // Sections
    // -----------------------------------------------------------------------

    private static string DescribeWalkmesh(IdFile? walkmesh)
    {
        var bounds = WalkmeshGeometry.Bounds(walkmesh);
        if (bounds is not Rect b)
            return "Walkmesh: none available (positions are 320x240 screen pixels).";

        var centroid = WalkmeshGeometry.Centroid(walkmesh)!.Value;
        var area = WalkmeshGeometry.Area(walkmesh);
        var coverage = area / ((double)b.W * b.H) * 100;
        var maxX = b.X + b.W - 1;
        var maxY = b.Y + b.H - 1;

        var sb = new StringBuilder();
        sb.Append("Walkmesh (field coordinates; use these for every position):\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"- {walkmesh!.TriangleCount} triangles; X {b.X}..{maxX}, Y {b.Y}..{maxY}\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"- walkable centroid ({centroid.X},{centroid.Y}); walkable area covers {coverage:0}% of the bounding box\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"- occupancy grid ({GridColumns}x{GridRows}, '#' walkable, '.' blocked); row 0 is Y={b.Y}, column 0 is X={b.X}, each cell ~{CellSize(b.W, GridColumns)}x{CellSize(b.H, GridRows)} units:\n");

        for (var row = 0; row < GridRows; row++)
        {
            sb.Append("  ");
            for (var col = 0; col < GridColumns; col++)
                sb.Append(CellWalkable(walkmesh, b, col, row) ? '#' : '.');
            if (row < GridRows - 1)
                sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string DescribeGroups(Section1File? scripts)
    {
        if (scripts == null || scripts.GrpScriptCount == 0)
            return string.Empty;

        var models = FieldModels.Of(scripts);
        var byIndex = models.Groups.ToDictionary(static g => g.GroupIndex);
        var sb = new StringBuilder("Existing groups (do not reuse these names):\n");
        var names = scripts.GrpScripts
            .Select((g, index) => !byIndex.TryGetValue(index, out var model)
                ? g.Name
                : model.PartyCharId is { } charId
                    ? $"{g.Name} (playable: {PartyName(charId)})"
                    : $"{g.Name} (character/NPC)")
            .Where(n => !string.IsNullOrWhiteSpace(n));
        sb.Append("- ").Append(string.Join(", ", names));

        var reusable = models.Groups.Where(static g => g.PartyCharId == null).Select(static g => g.Name).ToList();
        if (reusable.Count > 0)
            sb.Append("\nModels new actors can reuse (actors[].model): ").Append(string.Join(", ", reusable));
        return sb.ToString();
    }

    private static string PartyName(byte charId) => charId switch
    {
        0 => "Cloud",
        1 => "Barret",
        2 => "Tifa",
        3 => "Aeris",
        4 => "Red XIII",
        5 => "Yuffie",
        6 => "Cait Sith",
        7 => "Vincent",
        8 => "Cid",
        _ => $"character {charId}",
    };

    private static string DescribeExits(InfFile? inf)
    {
        if (inf == null)
            return string.Empty;

        var exits = inf.ExitLines
            .Where(e => e.FieldId != UnusedFieldId && !IsZeroLine(e))
            .ToList();
        if (exits.Count == 0)
            return string.Empty;

        var sb = new StringBuilder("Exits (gateway lines; keep them clear):\n");
        for (var i = 0; i < exits.Count; i++)
        {
            var e = exits[i];
            sb.Append(CultureInfo.InvariantCulture,
                $"- to field #{e.FieldId} across ({e.LineA.X},{e.LineA.Y})-({e.LineB.X},{e.LineB.Y})");
            if (i < exits.Count - 1)
                sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string DescribeDialog(Section1File? scripts)
    {
        if (scripts == null || scripts.TextCount == 0)
            return string.Empty;

        var lines = scripts.Texts
            .Select(t => Flatten(t.Value))
            .Where(t => t.Length > 0)
            .Take(MaxDialogLines)
            .Select(t => t.Length > MaxDialogLineChars ? t[..(MaxDialogLineChars - 3)] + "..." : t)
            .ToList();
        if (lines.Count == 0)
            return string.Empty;

        var sb = new StringBuilder("Existing dialog (match the tone; avoid duplicate character names):\n");
        sb.Append(string.Join("\n", lines.Select(l => $"- \"{l}\"")));
        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static int CellSize(int extent, int cells) => Math.Max(1, (int)Math.Ceiling(extent / (double)cells));

    /// <summary>A cell is walkable when its centre or any of its four quarter-points is on the walkmesh.</summary>
    private static bool CellWalkable(IdFile walkmesh, Rect b, int col, int row)
    {
        var cellW = b.W / (double)GridColumns;
        var cellH = b.H / (double)GridRows;
        var x0 = b.X + col * cellW;
        var y0 = b.Y + row * cellH;

        ReadOnlySpan<(double Fx, double Fy)> samples = [(0.5, 0.5), (0.25, 0.25), (0.75, 0.25), (0.25, 0.75), (0.75, 0.75)];
        foreach (var (fx, fy) in samples)
        {
            var x = x0 + fx * cellW;
            var y = y0 + fy * cellH;
            foreach (var t in walkmesh.Triangles)
                if (WalkmeshGeometry.TriangleContains(t, x, y))
                    return true;
        }
        return false;
    }

    private static bool IsZeroLine(ExitLine e) =>
        e.LineA.X == 0 && e.LineA.Y == 0 && e.LineA.Z == 0 &&
        e.LineB.X == 0 && e.LineB.Y == 0 && e.LineB.Z == 0;

    /// <summary>Collapse whitespace/newlines and drop control characters for single-line quoting.</summary>
    private static string Flatten(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                if (!lastWasSpace && sb.Length > 0)
                    sb.Append(' ');
                lastWasSpace = true;
                continue;
            }
            sb.Append(c == '"' ? '\'' : c);
            lastWasSpace = false;
        }
        return sb.ToString().TrimEnd();
    }
}

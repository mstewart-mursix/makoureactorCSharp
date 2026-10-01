using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using MakouReactor.AI.Layout;
using MakouReactor.Core.Models;

namespace MakouReactor.AI.Prompt;

/// <summary>
/// Describes a real field (walkmesh shape, existing groups and dialogue, exits) as compact plain
/// text so the model can place content on walkable ground and match the field's tone.
/// </summary>
public static class FieldContextBuilder
{
    private const int MaxDialogSamples = 8;
    private const int MaxDialogChars = 80;
    private const int GridColumns = 16;
    private const int GridRows = 12;

    /// <summary>
    /// Build the context block. The result is deterministic for a given field and never longer
    /// than <paramref name="maxChars"/>; optional sections are dropped before required ones.
    /// </summary>
    public static string Describe(Field field, int maxChars = 4000)
    {
        ArgumentNullException.ThrowIfNull(field);
        maxChars = Math.Max(200, maxChars);

        var mapName = field.ScriptsAndTexts?.MapName;
        var header = string.IsNullOrWhiteSpace(mapName)
            ? $"Field: {field.Name}"
            : $"Field: {field.Name} (map name: \"{mapName}\")";

        var required = new List<string> { header };
        var optional = new List<string>();

        var mesh = WalkmeshGeometry.From(field);
        if (mesh is null)
        {
            required.Add("Walkmesh: none available. Coordinates are 2D pixels, origin top-left, 320x240.");
        }
        else
        {
            var b = mesh.Bounds;
            required.Add(
                "Coordinates are walkmesh units (not pixels); y grows downward in the picture below.\n" +
                string.Create(CultureInfo.InvariantCulture,
                    $"Walkable bounds: x {b.X}..{b.X + b.Width - 1}, y {b.Y}..{b.Y + b.Height - 1} ({mesh.TriangleCount} triangles). " +
                    $"Center of walkable area: ({mesh.Centroid.X},{mesh.Centroid.Y}).") + "\n" +
                $"Walkable shape ({GridColumns}x{GridRows}, '#' = walkable, '.' = blocked):\n" +
                mesh.RenderAscii(GridColumns, GridRows));
        }

        var groups = field.ScriptsAndTexts?.GrpScripts;
        if (groups is { Count: > 0 })
        {
            var names = groups
                .Select(static g => g.Name)
                .Where(static n => !string.IsNullOrWhiteSpace(n))
                .ToArray();
            if (names.Length > 0)
                optional.Add($"Existing group names ({names.Length}): {string.Join(", ", names)}");
        }

        var exits = DescribeExits(field.Inf);
        if (exits != null)
            optional.Add(exits);

        var dialog = DescribeDialog(field.ScriptsAndTexts);
        if (dialog != null)
            optional.Add(dialog);

        var sb = new StringBuilder();
        foreach (var section in required)
            Append(sb, section);

        foreach (var section in optional)
        {
            if (sb.Length + section.Length + 1 > maxChars)
                continue;
            Append(sb, section);
        }

        var text = sb.ToString();
        return text.Length <= maxChars ? text : text[..maxChars];
    }

    private static string? DescribeExits(InfFile? inf)
    {
        if (inf is null)
            return null;

        var exits = inf.ExitLines
            .Where(static e => e.FieldId != 0x7FFF && e.FieldId != 0)
            .Select(static e => string.Create(CultureInfo.InvariantCulture,
                $"exit #{e.Id} -> field {e.FieldId}"))
            .ToArray();
        return exits.Length == 0 ? null : "Gateways: " + string.Join("; ", exits);
    }

    private static string? DescribeDialog(Section1File? section)
    {
        if (section is null || section.Texts.Count == 0)
            return null;

        var lines = section.Texts
            .Select(static t => OneLine(t.Value))
            .Where(static t => t.Length > 0)
            .Take(MaxDialogSamples)
            .ToArray();
        if (lines.Length == 0)
            return null;

        var sb = new StringBuilder();
        sb.Append(string.Create(CultureInfo.InvariantCulture,
            $"Existing dialog (first {lines.Length} of {section.Texts.Count}):"));
        foreach (var line in lines)
            sb.Append("\n - ").Append(line);
        return sb.ToString();
    }

    private static string OneLine(string text)
    {
        var flat = new string(text.Select(static c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return flat.Length <= MaxDialogChars ? flat : flat[..MaxDialogChars] + "...";
    }

    private static void Append(StringBuilder sb, string section)
    {
        if (sb.Length > 0)
            sb.Append('\n');
        sb.Append(section);
    }
}

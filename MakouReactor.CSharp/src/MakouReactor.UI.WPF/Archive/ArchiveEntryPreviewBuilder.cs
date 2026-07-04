using System.Text;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

namespace MakouReactor.UI.WPF.Archive;

public static class ArchiveEntryPreviewBuilder
{
    public static string Build(LgpArchiveEntry entry, Func<string, byte[]> readRawFile)
    {
        ArgumentNullException.ThrowIfNull(readRawFile);

        try
        {
            var data = readRawFile(entry.FullPath);
            return Build(entry, data);
        }
        catch (Exception ex)
        {
            return $"{entry.FullPath}\nPreview failed: {ex.Message}";
        }
    }

    public static string Build(LgpArchiveEntry entry, byte[] data)
    {
        var sample = data.AsSpan(0, Math.Min(data.Length, 512));
        var header = $"{entry.FullPath}\n{entry.Size:N0} bytes\n\n";

        if (TryBuildModelPreview(entry, data, out var modelPreview))
            return string.Concat(header, modelPreview);

        if (LooksLikeText(sample))
        {
            var text = Encoding.Latin1.GetString(sample).Replace("\0", string.Empty);
            return string.Concat(header, text, data.Length > sample.Length ? "\n..." : string.Empty);
        }

        return string.Concat(header, ToHexDump(sample), data.Length > sample.Length ? "\n..." : string.Empty);
    }

    public static bool LooksLikeText(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return true;

        var printable = 0;
        foreach (var value in data)
        {
            if (value is 0x09 or 0x0A or 0x0D or >= 0x20 and <= 0x7E)
                printable++;
        }

        return printable >= data.Length * 9 / 10;
    }

    public static string ToHexDump(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder();
        for (var offset = 0; offset < data.Length; offset += 16)
        {
            var line = data.Slice(offset, Math.Min(16, data.Length - offset));
            builder.Append($"{offset:X4}: ");
            for (var i = 0; i < line.Length; i++)
                builder.Append($"{line[i]:X2} ");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static bool TryBuildModelPreview(
        LgpArchiveEntry entry,
        byte[] data,
        out string preview)
    {
        preview = string.Empty;
        if (entry.FullPath.Contains('/') || !FieldArchive.IsLikelyFieldName(entry.Name))
            return false;

        try
        {
            var field = FieldPC.OpenCompressed(entry.Name, data);
            if (field.ModelLoader == null)
                return false;

            var builder = new StringBuilder();
            builder.AppendLine("Model preview");
            builder.AppendLine($"{field.ModelLoader.ModelCount:N0} model(s)");
            builder.AppendLine();
            foreach (var model in field.ModelLoader.Models.Take(8))
            {
                builder.AppendLine(
                    $"{model.Id}: {model.CharacterName}  HRC={model.HrcName}  " +
                    $"Scale={model.Scale}  Animations={model.Animations.Count}");
            }

            if (field.ModelLoader.Models.Count > 8)
                builder.AppendLine("...");

            preview = builder.ToString();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace MakouReactor.Core.Models;

public sealed class TutorialFile
{
    private TutorialFile(IReadOnlyList<TutorialEntry> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<TutorialEntry> Entries { get; }
    public int Count => Entries.Count;
    public bool HasTutorials => Entries.Any(static entry => entry.Kind == TutorialEntryKind.Tutorial);
    public bool HasMusic => Entries.Any(static entry => entry.Kind == TutorialEntryKind.Music);

    public static TutorialFile Open(ReadOnlySpan<byte> section1Data)
    {
        if (section1Data.Length <= 8)
            return new TutorialFile(Array.Empty<TutorialEntry>());

        var version = BinaryPrimitives.ReadUInt16LittleEndian(section1Data[..2]);
        var isDemo = version == 0x0301;
        var groupCount = section1Data[2];
        var akaoCount = BinaryPrimitives.ReadUInt16LittleEndian(section1Data.Slice(6, 2));
        var akaoTableOffset = (isDemo ? 24 : 32) + (groupCount * 8);
        var tableSize = checked(akaoCount * 4);

        if (akaoCount == 0 || akaoTableOffset + tableSize > section1Data.Length)
            return new TutorialFile(Array.Empty<TutorialEntry>());

        var positions = new List<uint>(akaoCount + 1);
        for (var i = 0; i < akaoCount; i++)
            positions.Add(BinaryPrimitives.ReadUInt32LittleEndian(section1Data.Slice(akaoTableOffset + (i * 4), 4)));
        positions.Add((uint)section1Data.Length);

        var entries = new List<TutorialEntry>(akaoCount);
        for (var i = 0; i < akaoCount; i++)
        {
            var start = positions[i];
            var end = positions[i + 1];
            if (start >= section1Data.Length || end <= start || end > section1Data.Length)
                continue;

            var data = section1Data.Slice(checked((int)start), checked((int)(end - start))).ToArray();
            entries.Add(ReadEntry(i, data));
        }

        return new TutorialFile(entries);
    }

    private static TutorialEntry ReadEntry(int id, byte[] data)
    {
        var isAkao = data.AsSpan().StartsWith("AKAO"u8);
        if (isAkao)
        {
            var musicId = data.Length >= 6
                ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4, 2))
                : (ushort)0;
            var description = data.Length >= 8
                ? BuildAkaoDescription(data, musicId)
                : "AKAO header is truncated.";
            return new TutorialEntry(id, TutorialEntryKind.Music, data.Length, musicId, false, description, data);
        }

        var broken = data.Length > 0 && data[0] > 0x12 && data[0] < 0xFF;
        return new TutorialEntry(id, TutorialEntryKind.Tutorial, data.Length, null, broken, ParseTutorial(data), data);
    }

    private static string BuildAkaoDescription(byte[] data, ushort musicId)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"totalLength={data.Length}");
        builder.AppendLine($"id={musicId}");
        if (data.Length >= 8)
            builder.AppendLine($"length={BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6, 2))}");
        if (data.Length >= 22)
        {
            var firstPosition = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(20, 2));
            builder.AppendLine($"ChannelCount={firstPosition / 2 + 1}");
            builder.AppendLine(Convert.ToHexString(data.AsSpan(8, Math.Min(8, data.Length - 8))).ToLowerInvariant());
            builder.AppendLine(Convert.ToHexString(data.AsSpan(16, Math.Min(4, data.Length - 16))).ToLowerInvariant());
        }

        return builder.ToString().TrimEnd();
    }

    private static string ParseTutorial(byte[] data)
    {
        var builder = new StringBuilder();
        var offset = 0;
        while (offset < data.Length)
        {
            var opcode = data[offset++];
            switch (opcode)
            {
                case 0x00 when offset + 2 <= data.Length:
                    builder.AppendLine($"PAUSE({BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2))})");
                    offset += 2;
                    break;
                case 0x02:
                    builder.AppendLine("[UP]");
                    break;
                case 0x03:
                    builder.AppendLine("[DOWN]");
                    break;
                case 0x04:
                    builder.AppendLine("[LEFT]");
                    break;
                case 0x05:
                    builder.AppendLine("[RIGHT]");
                    break;
                case 0x06:
                    builder.AppendLine("[MENU]");
                    break;
                case 0x07:
                    builder.AppendLine("[CANCEL]");
                    break;
                case 0x08:
                    builder.AppendLine("[CHANGE]");
                    break;
                case 0x09:
                    builder.AppendLine("[OK]");
                    break;
                case 0x10:
                    var end = Array.IndexOf(data, (byte)0xFF, offset);
                    if (end < 0)
                        end = data.Length;
                    builder.AppendLine($"TEXT(\"{FF7TextCodec.Decode(data.AsSpan(offset, end - offset)).Replace("\"", "\\\"", StringComparison.Ordinal)}\")");
                    offset = Math.Min(end + 1, data.Length);
                    break;
                case 0x11:
                    builder.AppendLine("{FINISH}");
                    break;
                case 0x12 when offset + 4 <= data.Length:
                    builder.AppendLine(
                        $"MOVE({BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2))}," +
                        $"{BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 2, 2))})");
                    offset += 4;
                    break;
                case 0xFF:
                    builder.AppendLine("{NOP}");
                    break;
                default:
                    builder.AppendLine($"[{opcode:X2}]");
                    break;
            }
        }

        return builder.ToString().TrimEnd();
    }
}

public enum TutorialEntryKind
{
    Tutorial,
    Music,
}

public sealed record TutorialEntry(
    int Id,
    TutorialEntryKind Kind,
    int Size,
    ushort? MusicId,
    bool Broken,
    string Description,
    byte[] RawData)
{
    public string Name => Kind == TutorialEntryKind.Music ? $"Music {Id}" : $"Tuto {Id}";
    public string MusicIdLabel => MusicId.HasValue ? MusicId.Value.ToString() : string.Empty;
}

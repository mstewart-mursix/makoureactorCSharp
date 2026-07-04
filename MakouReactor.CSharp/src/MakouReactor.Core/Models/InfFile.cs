using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace MakouReactor.Core.Models;

public sealed class InfFile
{
    private const int InternationalSize = 740;
    private const int JapaneseSize = 536;
    private const int DoorCount = 12;
    private const int TriggerCount = 12;
    private const int ArrowCount = 12;
    private const int DoorOffset = 56;
    private const int DoorSize = 24;
    private const int TriggerOffset = DoorOffset + (DoorCount * DoorSize);
    private const int TriggerSize = 16;
    private const int DisplayArrowOffset = TriggerOffset + (TriggerCount * TriggerSize);
    private const int ArrowOffset = DisplayArrowOffset + ArrowCount;
    private const int ArrowSize = 16;

    private InfFile(
        byte[] rawData,
        string mapName,
        byte control,
        short cameraFocusHeight,
        InfRange cameraRange,
        BackgroundLayerInfo backgroundLayers,
        IReadOnlyList<ExitLine> exitLines,
        IReadOnlyList<FieldTrigger> triggers,
        IReadOnlyList<FieldArrow> arrows)
    {
        RawData = rawData;
        MapName = mapName;
        Control = control;
        CameraFocusHeight = cameraFocusHeight;
        CameraRange = cameraRange;
        BackgroundLayers = backgroundLayers;
        ExitLines = exitLines;
        Triggers = triggers;
        Arrows = arrows;
    }

    public ReadOnlyMemory<byte> RawData { get; private set; }
    public int Size => RawData.Length;
    public bool IsJapanese => Size == JapaneseSize;
    public string MapName { get; private set; }
    public byte Control { get; private set; }
    public short CameraFocusHeight { get; private set; }
    public InfRange CameraRange { get; private set; }
    public BackgroundLayerInfo BackgroundLayers { get; }
    public IReadOnlyList<ExitLine> ExitLines { get; }
    public IReadOnlyList<FieldTrigger> Triggers { get; }
    public IReadOnlyList<FieldArrow> Arrows { get; }

    public static InfFile Open(ReadOnlySpan<byte> data)
    {
        if (data.Length is not InternationalSize and not JapaneseSize)
            throw new InvalidDataException($"INF section must be {InternationalSize} or {JapaneseSize} bytes.");

        var raw = data.ToArray();
        var mapName = ReadFixedLatin1(data[..9]);
        var cameraRange = new InfRange(
            ReadInt16(data, 12),
            ReadInt16(data, 14),
            ReadInt16(data, 16),
            ReadInt16(data, 18));
        var backgroundLayers = new BackgroundLayerInfo(
            data[20],
            data[21],
            data[22],
            data[23],
            ReadInt16(data, 24),
            ReadInt16(data, 26),
            ReadInt16(data, 28),
            ReadInt16(data, 30),
            ReadInt16(data, 32),
            ReadInt16(data, 34),
            ReadInt16(data, 36),
            ReadInt16(data, 38),
            ReadInt16(data, 40),
            ReadInt16(data, 42),
            ReadInt16(data, 44),
            ReadInt16(data, 46));

        return new InfFile(
            raw,
            mapName,
            data[9],
            ReadInt16(data, 10),
            cameraRange,
            backgroundLayers,
            ReadExitLines(data),
            ReadTriggers(data),
            data.Length == InternationalSize ? ReadArrows(data) : Array.Empty<FieldArrow>());
    }

    public void SetGeneralMetadata(string mapName, byte control, short cameraFocusHeight, InfRange cameraRange)
    {
        if (Encoding.Latin1.GetByteCount(mapName) > 9)
            throw new InvalidDataException("INF map name must be 9 Latin-1 bytes or fewer.");

        MapName = mapName;
        Control = control;
        CameraFocusHeight = cameraFocusHeight;
        CameraRange = cameraRange;
        var data = RawData.ToArray();
        data.AsSpan(0, 9).Clear();
        Encoding.Latin1.GetBytes(mapName, data.AsSpan(0, Math.Min(9, mapName.Length)));
        data[9] = control;
        WriteInt16(data, 10, cameraFocusHeight);
        WriteInt16(data, 12, cameraRange.Left);
        WriteInt16(data, 14, cameraRange.Top);
        WriteInt16(data, 16, cameraRange.Right);
        WriteInt16(data, 18, cameraRange.Bottom);
        RawData = data;
    }

    public byte[] Save() => RawData.ToArray();

    private static IReadOnlyList<ExitLine> ReadExitLines(ReadOnlySpan<byte> data)
    {
        var result = new List<ExitLine>(DoorCount);
        for (var i = 0; i < DoorCount; i++)
        {
            var offset = DoorOffset + (i * DoorSize);
            result.Add(new ExitLine(
                i,
                ReadVertex(data, offset),
                ReadVertex(data, offset + 6),
                ReadVertex(data, offset + 12),
                ReadUInt16(data, offset + 18),
                data[offset + 20],
                data[offset + 21],
                data[offset + 22],
                data[offset + 23]));
        }

        return result;
    }

    private static IReadOnlyList<FieldTrigger> ReadTriggers(ReadOnlySpan<byte> data)
    {
        var result = new List<FieldTrigger>(TriggerCount);
        for (var i = 0; i < TriggerCount; i++)
        {
            var offset = TriggerOffset + (i * TriggerSize);
            result.Add(new FieldTrigger(
                i,
                ReadVertex(data, offset),
                ReadVertex(data, offset + 6),
                data[offset + 12],
                data[offset + 13],
                data[offset + 14],
                data[offset + 15]));
        }

        return result;
    }

    private static IReadOnlyList<FieldArrow> ReadArrows(ReadOnlySpan<byte> data)
    {
        var result = new List<FieldArrow>(ArrowCount);
        for (var i = 0; i < ArrowCount; i++)
        {
            var offset = ArrowOffset + (i * ArrowSize);
            result.Add(new FieldArrow(
                i,
                (data[DisplayArrowOffset + i] & 1) != 0,
                BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 4, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 8, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 12, 4))));
        }

        return result;
    }

    private static InfVertex ReadVertex(ReadOnlySpan<byte> data, int offset) =>
        new(ReadInt16(data, offset), ReadInt16(data, offset + 2), ReadInt16(data, offset + 4));

    private static short ReadInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));

    private static void WriteInt16(byte[] data, int offset, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset, 2), value);

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));

    private static string ReadFixedLatin1(ReadOnlySpan<byte> data)
    {
        var end = data.IndexOf((byte)0);
        if (end < 0)
            end = data.Length;

        return Encoding.Latin1.GetString(data[..end]);
    }
}

public sealed record InfVertex(short X, short Y, short Z)
{
    public string Display => $"{X}, {Y}, {Z}";
}

public sealed record InfRange(short Left, short Top, short Right, short Bottom)
{
    public string Display => $"{Left}, {Top}, {Right}, {Bottom}";
}

public sealed record BackgroundLayerInfo(
    byte Layer1Flag,
    byte Layer2Flag,
    byte Layer3Flag,
    byte Layer4Flag,
    short Layer3Width,
    short Layer3Height,
    short Layer4Width,
    short Layer4Height,
    short Layer3X,
    short Layer3Y,
    short Layer4X,
    short Layer4Y,
    short Layer3XMultiplier,
    short Layer3YMultiplier,
    short Layer4XMultiplier,
    short Layer4YMultiplier);

public sealed record ExitLine(
    int Id,
    InfVertex LineA,
    InfVertex LineB,
    InfVertex Destination,
    ushort FieldId,
    byte Direction,
    byte DirectionCopy1,
    byte DirectionCopy2,
    byte DirectionCopy3);

public sealed record FieldTrigger(
    int Id,
    InfVertex LineA,
    InfVertex LineB,
    byte BackgroundParameter,
    byte BackgroundState,
    byte Behavior,
    byte SoundId);

public sealed record FieldArrow(
    int Id,
    bool Displayed,
    int PositionX,
    int PositionZ,
    int PositionY,
    uint Type)
{
    public string Position => $"{PositionX}, {PositionZ}, {PositionY}";
}

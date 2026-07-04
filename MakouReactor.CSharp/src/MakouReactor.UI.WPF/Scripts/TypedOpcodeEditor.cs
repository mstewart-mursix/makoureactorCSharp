using System.Buffers.Binary;

using MakouReactor.Core.Models;

namespace MakouReactor.UI.WPF.Scripts;

public sealed record TypedOpcodeField(string Label, int Value, int MaxValue, int MinValue = 0);

public static class TypedOpcodeEditor
{
    private static readonly HashSet<string> BinaryVariableOpcodes =
    [
        "SETBYTE", "SETWORD", "BITON", "BITOFF", "BITXOR",
        "PLUS", "PLUS2", "MINUS", "MINUS2", "MUL", "MUL2",
        "DIV", "DIV2", "MOD", "MOD2", "AND", "AND2",
        "OR", "OR2", "XOR", "XOR2"
    ];

    private static readonly HashSet<string> UnaryVariableOpcodes =
    [
        "INC", "INC2", "DEC", "DEC2", "RANDOM"
    ];

    private static readonly Dictionary<string, string[]> MediaOpcodeLabels = new()
    {
        ["MUSIC"] = ["Music"],
        ["SOUND"] = ["Sound", "Volume", "Pan", "Channel"],
        ["MUSVT"] = ["Volume"],
        ["MUSVM"] = ["Volume"],
        ["MULCK"] = ["Locked"],
        ["BMUSC"] = ["Music"],
        ["CHMPH"] = ["Phase", "Value", "Speed"],
        ["PMVIE"] = ["Movie"],
        ["MVIEF"] = ["Movie", "Flags"],
        ["MVCAM"] = ["Camera"],
        ["FMUSC"] = ["Music"],
        ["CMUSC"] = ["Music", "Volume", "Pan", "Tempo", "Fade", "Mode", "Flags"],
        ["CHMST"] = ["Music", "State"]
    };

    private static readonly Dictionary<string, string[]> ModelAnimationOpcodeLabels = new()
    {
        ["PC"] = ["Character"],
        ["CHAR"] = ["Model"],
        ["DFANM"] = ["Animation", "Frame"],
        ["ANIME1"] = ["Animation", "Mode"],
        ["VISI"] = ["Visible"],
        ["MOVA"] = ["Animation"],
        ["TURA"] = ["Target", "Speed", "Flags"],
        ["FMOVE"] = ["X", "X high", "Y", "Y high", "Speed"],
        ["ANIME2"] = ["Animation", "Mode"],
        ["ANIM!1"] = ["Animation", "Mode"],
        ["CANIM1"] = ["Animation", "Start", "End", "Speed"],
        ["CANM!1"] = ["Animation", "Start", "End", "Speed"],
        ["MSPED"] = ["Speed", "Speed high", "Mode"],
        ["DIR"] = ["Direction", "Flags"],
        ["TURNGEN"] = ["Direction", "Direction high", "Speed", "Speed high", "Mode"],
        ["TURN"] = ["Direction", "Direction high", "Speed", "Speed high", "Mode"],
        ["DIRA"] = ["Actor"],
        ["GETDIR"] = ["Bank", "Address", "Actor"],
        ["GETAI"] = ["Bank", "Address", "Actor"],
        ["ANIM!2"] = ["Animation", "Mode"],
        ["CANIM2"] = ["Animation", "Start", "End", "Speed"],
        ["CANM!2"] = ["Animation", "Start", "End", "Speed"],
        ["ASPED"] = ["Speed", "Speed high", "Mode"],
        ["CCANM"] = ["Animation", "Mode", "Flags"]
    };

    private static readonly Dictionary<string, string[]> ReferenceOpcodeLabels = new()
    {
        ["LINON"] = ["Enabled"],
        ["MPJPO"] = ["Disabled"],
        ["SOLID"] = ["Disabled"],
        ["FCFIX"] = ["Disabled"],
        ["PMJMP"] = ["Field"],
        ["TALKR"] = ["Bank", "Low flags", "Range"],
        ["SLIDR"] = ["Bank", "Low flags", "Range"],
        ["TLKR2"] = ["Bank", "Low flags", "Range"],
        ["SLDR2"] = ["Bank", "Low flags", "Range"]
    };

    private static readonly Dictionary<byte, string> SpecialSubkeyNames = new()
    {
        [0xF5] = "ARROW",
        [0xF6] = "PNAME",
        [0xF7] = "GMSPD",
        [0xF8] = "SMSPD",
        [0xF9] = "FLMAT",
        [0xFA] = "FLITM",
        [0xFB] = "BTLCK",
        [0xFC] = "MVLCK",
        [0xFD] = "SPCNM",
        [0xFE] = "RSGLB",
        [0xFF] = "CLITM"
    };

    public static bool Supports(RawOpcode opcode) => GetFields(opcode).Count > 0;

    public static IReadOnlyList<TypedOpcodeField> GetFields(RawOpcode opcode)
    {
        if (opcode.Name is "MESSAGE" && opcode.Bytes.Length == 3)
        {
            return
            [
                ByteField("Window", opcode.Bytes[1]),
                ByteField("Text", opcode.Bytes[2])
            ];
        }

        if (opcode.Name is "WINDOW" && opcode.Bytes.Length == 10)
        {
            return
            [
                ByteField("Window", opcode.Bytes[1]),
                WordField("X", ReadUInt16(opcode.Bytes, 2)),
                WordField("Y", ReadUInt16(opcode.Bytes, 4)),
                WordField("Width", ReadUInt16(opcode.Bytes, 6)),
                WordField("Height", ReadUInt16(opcode.Bytes, 8))
            ];
        }

        if (opcode.Name is "WMOVE" && opcode.Bytes.Length == 6)
        {
            return
            [
                ByteField("Window", opcode.Bytes[1]),
                WordField("X", ReadUInt16(opcode.Bytes, 2)),
                WordField("Y", ReadUInt16(opcode.Bytes, 4))
            ];
        }

        if (opcode.Name is "MAPJUMP" && opcode.Bytes.Length == 10)
        {
            return
            [
                WordField("Field", ReadUInt16(opcode.Bytes, 1)),
                WordField("X", ReadUInt16(opcode.Bytes, 3)),
                WordField("Y", ReadUInt16(opcode.Bytes, 5)),
                WordField("Triangle", ReadUInt16(opcode.Bytes, 7)),
                ByteField("Direction", opcode.Bytes[9])
            ];
        }

        if (opcode.Name is "LINE" && opcode.Bytes.Length == 13)
        {
            return
            [
                ShortField("Point 1 X", ReadInt16(opcode.Bytes, 1)),
                ShortField("Point 1 Y", ReadInt16(opcode.Bytes, 3)),
                ShortField("Point 1 Z", ReadInt16(opcode.Bytes, 5)),
                ShortField("Point 2 X", ReadInt16(opcode.Bytes, 7)),
                ShortField("Point 2 Y", ReadInt16(opcode.Bytes, 9)),
                ShortField("Point 2 Z", ReadInt16(opcode.Bytes, 11))
            ];
        }

        if (opcode.Name is "PMJMP" && opcode.Bytes.Length == 3)
        {
            return [WordField("Field", ReadUInt16(opcode.Bytes, 1))];
        }

        if (opcode.Name is "LINON" or "MPJPO" or "SOLID" or "FCFIX" && opcode.Bytes.Length == 2)
        {
            return [ByteField(ReferenceOpcodeLabels[opcode.Name][0], opcode.Bytes[1])];
        }

        if (opcode.Name is "TALKR" or "SLIDR" && opcode.Bytes.Length == 3)
        {
            return
            [
                NibbleField("Bank", HighNibble(opcode.Bytes[1])),
                NibbleField("Low flags", LowNibble(opcode.Bytes[1])),
                ByteField("Range", opcode.Bytes[2])
            ];
        }

        if (opcode.Name == "SPECIAL" && opcode.Bytes.Length == 2)
        {
            var subkey = opcode.Bytes[1];
            var label = SpecialSubkeyNames.TryGetValue(subkey, out var name)
                ? $"Subkey ({name})"
                : "Subkey";
            return [ByteField(label, subkey)];
        }

        if (opcode.Name is "TLKR2" or "SLDR2" && opcode.Bytes.Length == 4)
        {
            return
            [
                NibbleField("Bank", HighNibble(opcode.Bytes[1])),
                NibbleField("Low flags", LowNibble(opcode.Bytes[1])),
                WordField("Range", ReadUInt16(opcode.Bytes, 2))
            ];
        }

        if (BinaryVariableOpcodes.Contains(opcode.Name) && opcode.Bytes.Length is 4 or 5)
        {
            var banks = opcode.Bytes[1];
            var fields = new List<TypedOpcodeField>
            {
                NibbleField("Dest bank", LowNibble(banks)),
                ByteField("Dest address", opcode.Bytes[2]),
                NibbleField("Source bank", HighNibble(banks)),
                ByteField("Source/value", opcode.Bytes[3])
            };

            if (opcode.Bytes.Length == 5)
                fields.Add(ByteField("Extra", opcode.Bytes[4]));

            return fields;
        }

        if (UnaryVariableOpcodes.Contains(opcode.Name) && opcode.Bytes.Length == 3)
        {
            return
            [
                NibbleField("Bank", HighNibble(opcode.Bytes[1])),
                NibbleField("Low flags", LowNibble(opcode.Bytes[1])),
                ByteField("Address", opcode.Bytes[2])
            ];
        }

        if (TryGetFixedByteLabels(opcode.Name, out var labels) &&
            opcode.Bytes.Length == labels.Length + 1)
        {
            return labels
                .Select((label, index) => ByteField(label, opcode.Bytes[index + 1]))
                .ToArray();
        }

        return [];
    }

    public static bool TryBuild(RawOpcode opcode, IReadOnlyList<int> values, out byte[] bytes, out string error)
    {
        bytes = [];
        error = string.Empty;
        var fields = GetFields(opcode);
        if (fields.Count == 0)
            return true;

        if (values.Count != fields.Count)
        {
            error = $"Expected {fields.Count} typed value(s).";
            return false;
        }

        for (var index = 0; index < fields.Count; index++)
        {
            if (values[index] < fields[index].MinValue || values[index] > fields[index].MaxValue)
            {
                error = $"{fields[index].Label} must be between {fields[index].MinValue} and {fields[index].MaxValue}.";
                return false;
            }
        }

        if (opcode.Name == "MESSAGE")
            return TryBuildMessage((byte)values[0], (byte)values[1], out bytes, out error);

        if (opcode.Name == "WINDOW")
            return TryBuildWindow((byte)values[0], (ushort)values[1], (ushort)values[2], (ushort)values[3], (ushort)values[4], out bytes, out error);

        if (opcode.Name == "WMOVE")
        {
            bytes = new byte[6];
            bytes[0] = opcode.Id;
            bytes[1] = (byte)values[0];
            WriteUInt16(bytes, 2, (ushort)values[1]);
            WriteUInt16(bytes, 4, (ushort)values[2]);
            return true;
        }

        if (opcode.Name == "MAPJUMP")
        {
            bytes = new byte[10];
            bytes[0] = opcode.Id;
            WriteUInt16(bytes, 1, (ushort)values[0]);
            WriteUInt16(bytes, 3, (ushort)values[1]);
            WriteUInt16(bytes, 5, (ushort)values[2]);
            WriteUInt16(bytes, 7, (ushort)values[3]);
            bytes[9] = (byte)values[4];
            return true;
        }

        if (opcode.Name == "LINE")
        {
            bytes = new byte[13];
            bytes[0] = opcode.Id;
            WriteInt16(bytes, 1, (short)values[0]);
            WriteInt16(bytes, 3, (short)values[1]);
            WriteInt16(bytes, 5, (short)values[2]);
            WriteInt16(bytes, 7, (short)values[3]);
            WriteInt16(bytes, 9, (short)values[4]);
            WriteInt16(bytes, 11, (short)values[5]);
            return true;
        }

        if (opcode.Name == "PMJMP")
        {
            bytes = [opcode.Id, (byte)values[0], (byte)(values[0] >> 8)];
            return true;
        }

        if (opcode.Name is "LINON" or "MPJPO" or "SOLID" or "FCFIX")
        {
            bytes = [opcode.Id, (byte)values[0]];
            return true;
        }

        if (opcode.Name is "TALKR" or "SLIDR")
        {
            bytes = [opcode.Id, PackNibbles(values[1], values[0]), (byte)values[2]];
            return true;
        }

        if (opcode.Name == "SPECIAL")
        {
            bytes = [opcode.Id, (byte)values[0]];
            return true;
        }

        if (opcode.Name is "TLKR2" or "SLDR2")
        {
            bytes = new byte[4];
            bytes[0] = opcode.Id;
            bytes[1] = PackNibbles(values[1], values[0]);
            WriteUInt16(bytes, 2, (ushort)values[2]);
            return true;
        }

        if (BinaryVariableOpcodes.Contains(opcode.Name))
        {
            bytes = new byte[opcode.Bytes.Length];
            bytes[0] = opcode.Id;
            bytes[1] = PackNibbles(values[0], values[2]);
            bytes[2] = (byte)values[1];
            bytes[3] = (byte)values[3];
            if (bytes.Length == 5)
                bytes[4] = (byte)values[4];
            return true;
        }

        if (UnaryVariableOpcodes.Contains(opcode.Name))
        {
            bytes = new byte[3];
            bytes[0] = opcode.Id;
            bytes[1] = PackNibbles(values[1], values[0]);
            bytes[2] = (byte)values[2];
            return true;
        }

        if (TryGetFixedByteLabels(opcode.Name, out _))
        {
            bytes = new byte[values.Count + 1];
            bytes[0] = opcode.Id;
            for (var index = 0; index < values.Count; index++)
                bytes[index + 1] = (byte)values[index];
            return true;
        }

        return true;
    }

    public static bool TryBuildMessage(byte windowId, byte textId, out byte[] bytes, out string error)
    {
        bytes = [0x40, windowId, textId];
        error = string.Empty;
        return true;
    }

    public static bool TryBuildWindow(
        byte windowId,
        ushort x,
        ushort y,
        ushort width,
        ushort height,
        out byte[] bytes,
        out string error)
    {
        bytes = new byte[10];
        bytes[0] = 0x50;
        bytes[1] = windowId;
        WriteUInt16(bytes, 2, x);
        WriteUInt16(bytes, 4, y);
        WriteUInt16(bytes, 6, width);
        WriteUInt16(bytes, 8, height);
        error = string.Empty;
        return true;
    }

    public static ushort ReadUInt16(byte[] bytes, int offset) =>
        bytes.Length >= offset + 2
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2))
            : (ushort)0;

    public static short ReadInt16(byte[] bytes, int offset) =>
        bytes.Length >= offset + 2
            ? BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset, 2))
            : (short)0;

    private static TypedOpcodeField ByteField(string label, byte value) => new(label, value, byte.MaxValue);
    private static TypedOpcodeField NibbleField(string label, byte value) => new(label, value, 0x0F);
    private static TypedOpcodeField WordField(string label, ushort value) => new(label, value, ushort.MaxValue);
    private static TypedOpcodeField ShortField(string label, short value) => new(label, value, short.MaxValue, short.MinValue);

    private static byte LowNibble(byte value) => (byte)(value & 0x0F);
    private static byte HighNibble(byte value) => (byte)(value >> 4);

    private static byte PackNibbles(int low, int high) => (byte)((high << 4) | low);

    private static void WriteUInt16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), value);

    private static void WriteInt16(byte[] bytes, int offset, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset, 2), value);

    private static bool TryGetFixedByteLabels(string opcodeName, out string[] labels) =>
        MediaOpcodeLabels.TryGetValue(opcodeName, out labels!) ||
        ModelAnimationOpcodeLabels.TryGetValue(opcodeName, out labels!);
}

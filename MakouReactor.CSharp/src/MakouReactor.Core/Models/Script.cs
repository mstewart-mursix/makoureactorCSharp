using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace MakouReactor.Core.Models;

// ──────────────────────────────────────────────
// Script (from src/core/field/Script.h)
// ──────────────────────────────────────────────

public enum MoveDirection
{
    Up,
    Down,
}

public sealed class Script
{
    private readonly List<Opcode> _opcodes = [];
    private readonly byte[] _rawBytes = [];
    private IReadOnlyList<RawOpcode>? _rawOpcodes;
    public string LastError { get; private set; } = string.Empty;
    public bool Valid { get; private set; } = true;

    public Script()
    {
    }

    public Script(byte[] rawBytes)
    {
        _rawBytes = rawBytes.ToArray();
    }

    public int Size => _rawBytes.Length > 0 ? _rawBytes.Length : _opcodes.Count;
    public bool IsEmpty => _rawBytes.Length == 0 && _opcodes.Count == 0;
    public bool IsValid => Valid;
    public IReadOnlyList<Opcode> Opcodes => _opcodes.AsReadOnly();
    public ReadOnlyMemory<byte> RawBytes => _rawBytes;
    public IReadOnlyList<RawOpcode> RawOpcodes =>
        _rawOpcodes ??= RawOpcodeReader.Read(_rawBytes);

    public Opcode? Opcode(int index) => index >= 0 && index < _opcodes.Count ? _opcodes[index] : null;

    public void SetOpcode(int index, Opcode opcode)
    {
        if (index >= 0 && index < _opcodes.Count)
            _opcodes[index] = opcode;
    }

    public void RemoveOpcode(int index)
    {
        if (index >= 0 && index < _opcodes.Count)
            _opcodes.RemoveAt(index);
    }

    public void InsertOpcode(int index, Opcode opcode)
    {
        if (index >= 0 && index <= _opcodes.Count)
            _opcodes.Insert(index, opcode);
    }

    public void MoveOpcode(int fromIndex, int toIndex)
    {
        if (fromIndex >= 0 && fromIndex < _opcodes.Count && toIndex >= 0 && toIndex <= _opcodes.Count)
        {
            var opcode = _opcodes[fromIndex];
            _opcodes.RemoveAt(fromIndex);
            _opcodes.Insert(toIndex, opcode);
        }
    }

    /// <summary>Compile the script to a byte array.</summary>
    public byte[] Compile()
    {
        if (_rawBytes.Length > 0)
            return _rawBytes.ToArray();

        var result = new List<byte>();
        foreach (var opcode in _opcodes)
            result.AddRange(opcode.Inner.ToByteArray());
        return [.. result];
    }

    public byte[] ToByteArray() => Compile();

    public bool ReplaceRawOpcodeBytes(int offset, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (_rawBytes.Length == 0 || offset < 0 || offset >= _rawBytes.Length)
            return false;

        var opcode = RawOpcodes.FirstOrDefault(item => item.Offset == offset);
        if (opcode == null || opcode.Bytes.Length != bytes.Length)
            return false;

        bytes.CopyTo(_rawBytes.AsSpan(offset, bytes.Length));
        _rawOpcodes = null;
        return true;
    }

    public bool ShiftTextIds(int textId, int steps)
    {
        var changed = false;
        if (_rawBytes.Length > 0)
        {
            foreach (var opcode in RawOpcodes)
            {
                var textOffset = opcode.Name switch
                {
                    "MESSAGE" when opcode.Bytes.Length >= 3 => 2,
                    "MPNAM" when opcode.Bytes.Length >= 2 => 1,
                    "ASK" when opcode.Bytes.Length >= 4 => 3,
                    _ => -1,
                };
                if (textOffset < 0)
                    continue;

                var current = _rawBytes[opcode.Offset + textOffset];
                if (current <= textId)
                    continue;

                _rawBytes[opcode.Offset + textOffset] = ClampByte(current + steps);
                changed = true;
            }

            if (changed)
                _rawOpcodes = null;
            return changed;
        }

        foreach (var opcode in _opcodes)
        {
            if (opcode.Inner is not OpcodeSTRAY stray || stray.ScriptId <= textId)
                continue;

            stray.ScriptId = ClampByte(stray.ScriptId + steps);
            changed = true;
        }

        return changed;
    }

    public int AutosizeTextWindows(IReadOnlyList<FF7String> texts)
    {
        if (_rawBytes.Length == 0)
            return 0;

        var changed = 0;
        var windows = RawOpcodes
            .Where(static opcode => opcode.Name == "WINDOW" && opcode.Bytes.Length >= 10)
            .ToDictionary(static opcode => opcode.Bytes[1], static opcode => opcode.Offset);

        foreach (var message in RawOpcodes
            .Where(static opcode => opcode.Name == "MESSAGE" && opcode.Bytes.Length >= 3)
            .ToArray())
        {
            var windowId = message.Bytes[1];
            var textId = message.Bytes[2];
            if (!windows.TryGetValue(windowId, out var windowOffset) ||
                textId >= texts.Count)
            {
                continue;
            }

            var (width, height) = EstimateTextWindowSize(texts[textId].Value);
            var x = BinaryPrimitives.ReadInt16LittleEndian(_rawBytes.AsSpan(windowOffset + 2, 2));
            var y = BinaryPrimitives.ReadInt16LittleEndian(_rawBytes.AsSpan(windowOffset + 4, 2));
            var (clampedX, clampedY) = ClampWindowPosition(x, y, width, height);

            if (BinaryPrimitives.ReadUInt16LittleEndian(_rawBytes.AsSpan(windowOffset + 6, 2)) == width &&
                BinaryPrimitives.ReadUInt16LittleEndian(_rawBytes.AsSpan(windowOffset + 8, 2)) == height &&
                x == clampedX &&
                y == clampedY)
            {
                continue;
            }

            BinaryPrimitives.WriteInt16LittleEndian(_rawBytes.AsSpan(windowOffset + 2, 2), clampedX);
            BinaryPrimitives.WriteInt16LittleEndian(_rawBytes.AsSpan(windowOffset + 4, 2), clampedY);
            BinaryPrimitives.WriteUInt16LittleEndian(_rawBytes.AsSpan(windowOffset + 6, 2), width);
            BinaryPrimitives.WriteUInt16LittleEndian(_rawBytes.AsSpan(windowOffset + 8, 2), height);
            changed++;
        }

        if (changed > 0)
            _rawOpcodes = null;
        return changed;
    }

    /// <summary>Check if the script is void (only contains NOP/RET).</summary>
    public bool IsVoid()
    {
        foreach (var opcode in _opcodes)
        {
            if (opcode.Id != OpcodeKey.NOP && opcode.Id != OpcodeKey.RET)
                return false;
        }
        return true;
    }

    // ── Search methods ──

    public List<int> SearchOpcode(OpcodeKey key)
    {
        var results = new List<int>();
        for (int i = 0; i < _opcodes.Count; i++)
            if (_opcodes[i].Id == key)
                results.Add(i);
        return results;
    }

    public List<int> SearchVar(byte bank, ushort address)
    {
        // Simplified: scan for opcodes that reference the given variable
        var results = new List<int>();
        // (Full implementation would inspect each opcode's parameters)
        return results;
    }

    public List<int> SearchTextInScripts(string text)
    {
        // Simplified: scan STRAY opcodes for matching text ID
        var results = new List<int>();
        return results;
    }

    public List<int> ListUsedTexts()
    {
        var texts = new HashSet<int>();
        foreach (var opcode in RawOpcodes)
        {
            var textId = opcode.Name switch
            {
                "MESSAGE" when opcode.Bytes.Length >= 3 => opcode.Bytes[2],
                "MPNAM" when opcode.Bytes.Length >= 2 => opcode.Bytes[1],
                "ASK" when opcode.Bytes.Length >= 4 => opcode.Bytes[3],
                _ => -1,
            };
            if (textId >= 0)
                texts.Add(textId);
        }

        foreach (var opcode in _opcodes)
        {
            if (opcode.Inner is OpcodeSTRAY stray)
                texts.Add(stray.ScriptId);
        }
        return [.. texts];
    }

    public override string ToString()
    {
        return string.Join(", ", _opcodes.Select(o => o.Id.ToString()));
    }

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);

    /// <summary>Estimate the window size (pixels) needed to show <paramref name="text"/>.</summary>
    public static (ushort Width, ushort Height) EstimateTextWindowSize(string text)
    {
        var lines = text
            .Replace("{NEW}", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("{NewPage}", "\n", StringComparison.OrdinalIgnoreCase)
            .Split('\n');
        var maxChars = Math.Max(1, lines.Max(static line => VisibleCharacterCount(line)));
        var lineCount = Math.Max(1, lines.Length);
        var width = Math.Clamp((maxChars * 8) + 24, 32, 304);
        var height = Math.Clamp((lineCount * 16) + 24, 32, 216);
        return ((ushort)width, (ushort)height);
    }

    private static int VisibleCharacterCount(string text)
    {
        var count = 0;
        var inToken = false;
        foreach (var character in text)
        {
            if (character == '{')
            {
                inToken = true;
                continue;
            }

            if (inToken)
            {
                if (character == '}')
                    inToken = false;
                continue;
            }

            count++;
        }

        return count;
    }

    /// <summary>Move a window so it stays inside the visible field area.</summary>
    public static (short X, short Y) ClampWindowPosition(short x, short y, ushort width, ushort height)
    {
        var clampedX = x;
        var clampedY = y;
        if (clampedX + width > 312)
            clampedX = (short)(312 - width);
        if (clampedY + height > 223)
            clampedY = (short)(223 - height);
        if (clampedX < 8)
            clampedX = 8;
        if (clampedY < 8)
            clampedY = 8;
        return (clampedX, clampedY);
    }
}

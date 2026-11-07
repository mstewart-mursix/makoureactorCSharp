using System;
using System.Collections.Generic;
using System.Linq;

namespace MakouReactor.Core.Models;

/// <summary>
/// Builds raw field-script bytes opcode by opcode, with forward jump labels resolved on
/// <see cref="Compile"/>. Every opcode id, field order and size below was checked against the C++
/// reference: ids against the <c>OPCODE_GENERATE_LIST</c> X-macro that defines
/// <c>enum OpcodeKey</c> (<c>src/core/field/Opcode.h</c> lines 125-205, values counted from
/// <c>RET = 0</c>), layouts against the packed <c>Opcode*</c> structs in <c>Opcode.h</c> and sizes
/// against the <c>length[]</c> table in <c>src/core/field/Opcode.cpp</c> (lines 5300-5555).
/// <c>Opcode::toByteArray()</c> (<c>Opcode.cpp</c> line 159) writes the id byte followed by the
/// packed struct fields (little endian) truncated to the fixed size, so the in-memory-only
/// <c>_label</c>/<c>_badJump</c> members of jump opcodes are never written.
/// </summary>
/// <remarks>
/// Label handling ports <c>Script::compile()</c> (<c>src/core/field/Script.cpp</c> lines 155-225):
/// a jump's value is <c>labelPosition - opcodePosition</c>, stored as that value minus
/// <c>Opcode::jumpShift()</c> (<c>Opcode.cpp</c> line 1251), and a jump whose stored value no
/// longer fits in one byte is widened to its long form (<c>Opcode::setJump</c>, line 1331), after
/// which positions are recomputed until no size changes. Only forward jumps are emitted.
/// </remarks>
public sealed class ScriptBuilder
{
    // Opcode ids (Opcode.h OPCODE_GENERATE_LIST; lengths from Opcode.cpp length[]).
    /// <summary><c>RET</c>, Opcode.h line 126 (0x00); length 1 (Opcode.cpp line 5300).</summary>
    public const byte RET = 0x00;
    /// <summary><c>REQEW</c>, Opcode.h line 126 (0x03); <c>OpcodeExec</c> {groupID, scriptIDAndPriority}; length 3 (line 5303).</summary>
    public const byte REQEW = 0x03;
    /// <summary><c>JMPF</c>, Opcode.h line 131 (0x10); <c>OpcodeShortJump</c> {jump}; length 2 (line 5317).</summary>
    public const byte JMPF = 0x10;
    /// <summary><c>JMPFL</c>, Opcode.h line 131 (0x11); <c>OpcodeLongJump</c> {quint16 jump}; length 3 (line 5318).</summary>
    public const byte JMPFL = 0x11;
    /// <summary><c>IFUB</c>, Opcode.h line 132 (0x14); {banks, value1, value2, oper, jump}; length 6 (line 5321).</summary>
    public const byte IFUB = 0x14;
    /// <summary><c>IFUBL</c>, Opcode.h line 132 (0x15); {banks, value1, value2, oper, quint16 jump}; length 7 (line 5322).</summary>
    public const byte IFUBL = 0x15;
    /// <summary><c>WAIT</c>, Opcode.h line 137 (0x24); {quint16 frameCount}; length 3 (line 5338).</summary>
    public const byte WAIT = 0x24;
    /// <summary><c>MESSAGE</c>, Opcode.h line 146 (0x40); {windowID, textID}; length 3 (line 5368).</summary>
    public const byte MESSAGE = 0x40;
    /// <summary><c>WINDOW</c>, Opcode.h line 151 (0x50); {windowID, qint16 x, qint16 y, quint16 w, quint16 h}; length 10 (line 5385).</summary>
    public const byte WINDOW = 0x50;
    /// <summary><c>STITM</c>, Opcode.h line 153 (0x58); <c>OpcodeItem</c> {banks, quint16 itemID, quantity}; length 5 (line 5393).</summary>
    public const byte STITM = 0x58;
    /// <summary><c>BATTLE</c>, Opcode.h line 161 (0x70); {banks, quint16 battleID}; length 4 (line 5419).</summary>
    public const byte BATTLE = 0x70;
    /// <summary><c>BITON</c>, Opcode.h line 166 (0x82); <c>OpcodeBitOperation</c> {banks, var, position}; length 4 (line 5438).</summary>
    public const byte BITON = 0x82;
    /// <summary><c>BITOFF</c>, Opcode.h line 166 (0x83); <c>OpcodeBitOperation</c>; length 4 (line 5439).</summary>
    public const byte BITOFF = 0x83;
    /// <summary><c>CHAR_</c> ("CHAR"), Opcode.h line 176 (0xA1); {object3DID}; length 2 (line 5471).</summary>
    public const byte CHAR = 0xA1;
    /// <summary><c>XYZI</c>, Opcode.h line 177 (0xA5); {banks[2], qint16 x, y, z, quint16 triangle}; length 11 (line 5475).</summary>
    public const byte XYZI = 0xA5;
    /// <summary><c>MOVE</c>, Opcode.h line 178 (0xA8); <c>OpcodeMove</c> {banks, qint16 x, qint16 y}; length 6 (line 5478).</summary>
    public const byte MOVE = 0xA8;
    /// <summary><c>DIR</c>, Opcode.h line 181 (0xB3); {banks, direction}; length 3 (line 5490).</summary>
    public const byte DIR = 0xB3;
    /// <summary>
    /// <c>SOLID</c>, Opcode.h line 187 (0xC7); <c>OpcodeDisable</c> {disabled}; length 2. The shipped
    /// fields use <c>C7 01</c> for entities other characters walk through.
    /// </summary>
    public const byte SOLID = 0xC7;
    /// <summary><c>MUSIC</c>, Opcode.h line 201 (0xF0); <c>OpcodeMusic</c> {musicID}; length 2 (line 5555).</summary>
    public const byte MUSIC = 0xF0;

    /// <summary><c>LINE</c> (Opcode.h line 191, <c>OpcodeLINE</c>: two qint16 X/Y/Z points; size 13 in Opcode.cpp).</summary>
    public const byte LINE = 0xD0;

    /// <summary><c>Opcode::Operator::BitOn</c> (Opcode.h line 1564: Equal=0 ... BitOn=9, BitOff=10).</summary>
    public const byte OperatorBitOn = 9;
    /// <summary><c>Opcode::Operator::BitOff</c> (Opcode.h line 1564).</summary>
    public const byte OperatorBitOff = 10;

    private readonly List<Item> _items = [];
    private int _labelCount;

    /// <summary>Number of opcodes emitted so far (labels excluded).</summary>
    public int OpcodeCount => _items.Count(static item => item is not LabelItem);

    /// <summary>True when no opcode has been emitted.</summary>
    public bool IsEmpty => OpcodeCount == 0;

    /// <summary>Allocates a new label; place it with <see cref="MarkLabel"/>.</summary>
    /// <returns>The label id.</returns>
    public int NewLabel() => ++_labelCount;

    /// <summary>Places <paramref name="label"/> before the next emitted opcode.</summary>
    /// <param name="label">A label from <see cref="NewLabel"/>.</param>
    public ScriptBuilder MarkLabel(int label)
    {
        if (label <= 0 || label > _labelCount)
            throw new ArgumentOutOfRangeException(nameof(label));
        if (_items.Any(item => item is LabelItem existing && existing.Label == label))
            throw new InvalidOperationException($"Label {label} is declared several times.");
        _items.Add(new LabelItem(label));
        return this;
    }

    /// <summary>
    /// Appends the opcodes and labels of <paramref name="other"/>; its labels are renumbered so
    /// they cannot clash with this builder's.
    /// </summary>
    /// <param name="other">The builder to copy from (left unchanged).</param>
    public ScriptBuilder Append(ScriptBuilder other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var offset = _labelCount;
        foreach (var item in other._items)
        {
            _items.Add(item switch
            {
                LabelItem label => new LabelItem(label.Label + offset),
                JumpItem jump => jump with { Label = jump.Label + offset },
                _ => item,
            });
        }

        _labelCount += other._labelCount;
        return this;
    }

    /// <summary>Emits <c>RET</c>.</summary>
    public ScriptBuilder Ret() => Raw(RET);

    /// <summary>
    /// Emits <c>REQEW</c>: run file script <paramref name="scriptId"/> of group
    /// <paramref name="groupId"/> and wait for it to end. The second byte packs the script id
    /// (bits 0-4) and priority (bits 5-7) like <c>SCRIPT_AND_PRIORITY</c> (Opcode.h line 35).
    /// </summary>
    public ScriptBuilder ReqEw(byte groupId, int scriptId, int priority)
    {
        if (scriptId is < 0 or > 0x1F)
            throw new ArgumentOutOfRangeException(nameof(scriptId));
        if (priority is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(priority));
        return Raw(REQEW, groupId, (byte)((scriptId & 0x1F) | ((priority & 0x7) << 5)));
    }

    /// <summary>Emits <c>WAIT</c> for <paramref name="frames"/> frames.</summary>
    public ScriptBuilder Wait(ushort frames) => Raw(WAIT, Lo(frames), Hi(frames));

    /// <summary>Emits <c>WINDOW</c> (create window <paramref name="windowId"/>).</summary>
    public ScriptBuilder Window(byte windowId, short x, short y, ushort width, ushort height) =>
        Raw(WINDOW, windowId, Lo(x), Hi(x), Lo(y), Hi(y), Lo(width), Hi(width), Lo(height), Hi(height));

    /// <summary>Emits <c>MESSAGE</c> (show text <paramref name="textId"/> in window <paramref name="windowId"/>).</summary>
    public ScriptBuilder Message(byte windowId, byte textId) => Raw(MESSAGE, windowId, textId);

    /// <summary>Emits <c>STITM</c> with constant operands (banks = 0).</summary>
    public ScriptBuilder StItm(ushort itemId, byte quantity) =>
        Raw(STITM, 0x00, Lo(itemId), Hi(itemId), quantity);

    /// <summary>Emits <c>BATTLE</c> with a constant battle id (banks = 0).</summary>
    public ScriptBuilder Battle(ushort battleId) => Raw(BATTLE, 0x00, Lo(battleId), Hi(battleId));

    /// <summary>Emits <c>BITON</c>: set bit <paramref name="bit"/> of variable <paramref name="bank"/>:<paramref name="address"/>.</summary>
    public ScriptBuilder BitOn(byte bank, byte address, byte bit) => BitOperation(BITON, bank, address, bit);

    /// <summary>Emits <c>BITOFF</c>: clear bit <paramref name="bit"/> of variable <paramref name="bank"/>:<paramref name="address"/>.</summary>
    public ScriptBuilder BitOff(byte bank, byte address, byte bit) => BitOperation(BITOFF, bank, address, bit);

    /// <summary>Emits <c>CHAR</c>: this group is field model <paramref name="modelId"/>.</summary>
    public ScriptBuilder Char(byte modelId) => Raw(CHAR, modelId);

    /// <summary>Emits <c>XYZI</c> with constant operands (both bank bytes = 0).</summary>
    public ScriptBuilder Xyzi(short x, short y, short z, ushort triangle) =>
        Raw(XYZI, 0x00, 0x00, Lo(x), Hi(x), Lo(y), Hi(y), Lo(z), Hi(z), Lo(triangle), Hi(triangle));

    /// <summary>Emits <c>LINE</c> between two field points (no bank byte: operands are always constants).</summary>
    public ScriptBuilder Line(short x1, short y1, short z1, short x2, short y2, short z2) =>
        Raw(LINE, Lo(x1), Hi(x1), Lo(y1), Hi(y1), Lo(z1), Hi(z1), Lo(x2), Hi(x2), Lo(y2), Hi(y2), Lo(z2), Hi(z2));

    /// <summary>Emits <c>MOVE</c> with constant operands (banks = 0).</summary>
    public ScriptBuilder Move(short x, short y) => Raw(MOVE, 0x00, Lo(x), Hi(x), Lo(y), Hi(y));

    /// <summary>Emits <c>DIR</c> with a constant direction (banks = 0).</summary>
    public ScriptBuilder Dir(byte direction) => Raw(DIR, 0x00, direction);

    /// <summary>Emits <c>SOLID</c>: whether other characters collide with this entity.</summary>
    public ScriptBuilder Solid(bool solid) => Raw(SOLID, solid ? (byte)0 : (byte)1);

    /// <summary>Emits <c>MUSIC</c> (field music list index).</summary>
    public ScriptBuilder Music(byte musicId) => Raw(MUSIC, musicId);

    /// <summary>Emits <c>JMPF</c> (or <c>JMPFL</c> when needed) to <paramref name="label"/>.</summary>
    public ScriptBuilder JumpForward(int label)
    {
        _items.Add(new JumpItem(JMPF, JMPFL, [], 1, label));
        return this;
    }

    /// <summary>
    /// Emits <c>IFUB</c> (or <c>IFUBL</c>) testing bit <paramref name="bit"/> of
    /// <paramref name="bank"/>:<paramref name="address"/>; execution falls through when the test
    /// holds and jumps to <paramref name="elseLabel"/> otherwise ("else goto label",
    /// <c>Opcode::toStringIFUB</c>, Opcode.cpp line 2364). The banks byte is
    /// <c>BANK(bank, 0)</c> (Opcode.h line 33; <c>B1</c> = high nibble for value1, <c>B2</c> = low
    /// nibble for value2), so value2 is the constant bit position, the shape
    /// <c>Opcode::searchVar</c> treats as a bit compare (Opcode.cpp lines 296-306).
    /// </summary>
    public ScriptBuilder IfBit(byte bank, byte address, byte bit, bool bitOn, int elseLabel)
    {
        ValidateBank(bank);
        ValidateBit(bit);
        var oper = bitOn ? OperatorBitOn : OperatorBitOff;
        _items.Add(new JumpItem(IFUB, IFUBL, [(byte)(bank << 4), address, bit, oper], 5, elseLabel));
        return this;
    }

    /// <summary>
    /// Resolves labels and returns the script bytes.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown for a missing label or an oversized jump.</exception>
    public byte[] Compile()
    {
        var longJumps = new HashSet<int>();
        while (true)
        {
            var positions = new int[_items.Count];
            var labels = new Dictionary<int, int>();
            var pos = 0;
            for (var index = 0; index < _items.Count; index++)
            {
                positions[index] = pos;
                switch (_items[index])
                {
                    case LabelItem label:
                        labels[label.Label] = pos;
                        break;
                    case RawItem raw:
                        pos += raw.Bytes.Length;
                        break;
                    case JumpItem jump:
                        pos += jump.Size(longJumps.Contains(index));
                        break;
                }
            }

            var widened = false;
            var output = new List<byte>(pos);
            for (var index = 0; index < _items.Count; index++)
            {
                switch (_items[index])
                {
                    case RawItem raw:
                        output.AddRange(raw.Bytes);
                        break;
                    case JumpItem jump:
                        if (!labels.TryGetValue(jump.Label, out var target))
                            throw new InvalidOperationException($"Label {jump.Label} not found.");

                        var isLong = longJumps.Contains(index);
                        var stored = target - positions[index] - jump.Shift;
                        if (stored < 0)
                            throw new InvalidOperationException($"Label {jump.Label} is before its jump; only forward jumps are supported.");
                        if (stored > ushort.MaxValue)
                            throw new InvalidOperationException($"Jump to label {jump.Label} is too long.");
                        if (!isLong && stored > byte.MaxValue)
                        {
                            longJumps.Add(index);
                            widened = true;
                            break;
                        }

                        output.Add(isLong ? jump.LongId : jump.ShortId);
                        output.AddRange(jump.Prefix);
                        output.Add((byte)stored);
                        if (isLong)
                            output.Add((byte)(stored >> 8));
                        break;
                }
            }

            if (!widened)
                return [.. output];
        }
    }

    private ScriptBuilder BitOperation(byte id, byte bank, byte address, byte bit)
    {
        ValidateBank(bank);
        ValidateBit(bit);
        return Raw(id, (byte)(bank << 4), address, bit);
    }

    private ScriptBuilder Raw(params byte[] bytes)
    {
        _items.Add(new RawItem(bytes));
        return this;
    }

    private static void ValidateBank(byte bank)
    {
        if (bank is < 1 or > 15)
            throw new ArgumentOutOfRangeException(nameof(bank), "Variable banks are 1-15.");
    }

    private static void ValidateBit(byte bit)
    {
        if (bit > 7)
            throw new ArgumentOutOfRangeException(nameof(bit), "Bit positions are 0-7.");
    }

    private static byte Lo(int value) => (byte)value;
    private static byte Hi(int value) => (byte)(value >> 8);

    private abstract record Item;

    private sealed record LabelItem(int Label) : Item;

    private sealed record RawItem(byte[] Bytes) : Item;

    private sealed record JumpItem(byte ShortId, byte LongId, byte[] Prefix, int Shift, int Label) : Item
    {
        public int Size(bool isLong) => 1 + Prefix.Length + (isLong ? 2 : 1);
    }
}

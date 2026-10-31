using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MakouReactor.Core.Models;

// ──────────────────────────────────────────────
// Section1File (from src/core/field/Section1File.h)
// ──────────────────────────────────────────────

public sealed class Section1File
{
    public List<GrpScript> GrpScripts { get; } = [];
    public List<FF7String> Texts { get; } = [];
    public TutorialFile? TutorialsAndSounds { get; private set; }
    public string Author { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public ushort Scale { get; set; }
    public ushort Version { get; set; }
    public byte ModelCount { get; set; }
    public ushort AkaoCount { get; set; }
    public ushort TextSectionOffset { get; set; }
    public byte[] RawData { get; private set; } = [];
    public bool IsModified { get; private set; }
    public bool JapaneseText { get; private set; }

    public const int MaxGrpScriptCount = 256;
    public const int MaxTextCount = 256;

    public int GrpScriptCount => GrpScripts.Count;
    public int TextCount => Texts.Count;
    private bool _isDemo;
    private uint _firstAkaoOffset;

    public static Section1File Open(ReadOnlySpan<byte> data, bool japaneseText = false)
    {
        var section = new Section1File();
        section.Load(data, japaneseText);
        return section;
    }

    public void Load(ReadOnlySpan<byte> data, bool japaneseText = false)
    {
        if (data.Length < 32)
            throw new InvalidDataException("Section 1 data is too short.");

        RawData = data.ToArray();
        IsModified = false;
        JapaneseText = japaneseText;
        GrpScripts.Clear();
        Texts.Clear();
        TutorialsAndSounds = null;

        Version = BinaryPrimitives.ReadUInt16LittleEndian(data[..2]);
        var isDemo = Version == 0x0301;
        _isDemo = isDemo;
        var groupCount = data[2];
        ModelCount = data[3];
        TextSectionOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(4, 2));
        AkaoCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2));

        if (TextSectionOffset < 32 || TextSectionOffset > data.Length)
            throw new InvalidDataException("Section 1 text offset is out of range.");

        var cursor = isDemo ? 8 : 16;
        if (!isDemo)
            Scale = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2));

        if (cursor + 16 > data.Length)
            throw new InvalidDataException("Section 1 author/map name block is out of range.");

        Author = ReadFixedLatin1(data.Slice(cursor, 8));
        MapName = ReadFixedLatin1(data.Slice(cursor + 8, 8));
        cursor += 16;

        var groupNamesOffset = cursor;
        var akaoPositionsOffset = groupNamesOffset + (8 * groupCount);
        var scriptOffsetsOffset = akaoPositionsOffset + (4 * AkaoCount);
        var scriptCount = isDemo ? 16 : 32;
        var scriptOffsetsSize = groupCount * scriptCount * 2;

        if (scriptOffsetsOffset + scriptOffsetsSize > TextSectionOffset)
            throw new InvalidDataException("Section 1 script offset table exceeds the text section.");

        var firstAkaoOffset = AkaoCount > 0
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(akaoPositionsOffset, 4))
            : (uint)data.Length;
        _firstAkaoOffset = firstAkaoOffset;
        LoadScripts(data, groupNamesOffset, scriptOffsetsOffset, groupCount, scriptCount, firstAkaoOffset);

        LoadTexts(data, TextSectionOffset, firstAkaoOffset);
        TutorialsAndSounds = TutorialFile.Open(data);
    }

    public GrpScript? GrpScript(int groupId) =>
        groupId >= 0 && groupId < GrpScripts.Count ? GrpScripts[groupId] : null;

    public void InsertGrpScript(int index, GrpScript grpScript)
    {
        if (index >= 0 && index <= GrpScripts.Count && GrpScripts.Count < MaxGrpScriptCount)
            GrpScripts.Insert(index, grpScript);
    }

    public void RemoveGrpScript(int index)
    {
        if (index >= 0 && index < GrpScripts.Count)
            GrpScripts.RemoveAt(index);
    }

    public FF7String? Text(int textId) =>
        textId >= 0 && textId < Texts.Count ? Texts[textId] : null;

    public void SetText(int textId, FF7String text)
    {
        if (textId >= 0 && textId < Texts.Count)
        {
            Texts[textId] = text;
            IsModified = true;
        }
    }

    public void SetJapaneseText(bool japaneseText)
    {
        if (JapaneseText == japaneseText)
            return;

        JapaneseText = japaneseText;
        for (var index = 0; index < Texts.Count; index++)
        {
            var raw = Texts[index].RawBytes;
            if (raw != null)
                Texts[index] = FF7String.FromRaw(raw, japaneseText);
        }
    }

    public void InsertText(int index, FF7String text)
    {
        if (index >= 0 && index <= Texts.Count && Texts.Count < MaxTextCount)
        {
            Texts.Insert(index, text);
            ShiftScriptTextIds(index - 1, +1);
            IsModified = true;
        }
    }

    public void DeleteText(int textId)
    {
        if (textId >= 0 && textId < Texts.Count)
        {
            Texts.RemoveAt(textId);
            ShiftScriptTextIds(textId, -1);
            IsModified = true;
        }
    }

    public void ClearTexts()
    {
        Texts.Clear();
        IsModified = true;
    }

    public void MarkModified() => IsModified = true;

    public IReadOnlySet<int> ListUsedTexts()
    {
        var used = new HashSet<int>();
        foreach (var group in GrpScripts)
        {
            foreach (var script in group.Scripts)
            {
                foreach (var textId in script.ListUsedTexts())
                    used.Add(textId);
            }
        }

        return used;
    }

    public int CleanUnusedTexts()
    {
        var used = ListUsedTexts();
        var changed = 0;
        for (var index = 0; index < Texts.Count; index++)
        {
            if (used.Contains(index) || Texts[index].Value.Length == 0)
                continue;

            Texts[index] = new FF7String();
            changed++;
        }

        if (changed > 0)
            IsModified = true;
        return changed;
    }

    public int EmptyTexts()
    {
        var changed = 0;
        for (var index = 0; index < Texts.Count; index++)
        {
            if (Texts[index].Value.Length == 0)
                continue;

            Texts[index] = new FF7String();
            changed++;
        }

        if (changed > 0)
            IsModified = true;
        return changed;
    }

    public int AutosizeTextWindows()
    {
        var changed = 0;
        foreach (var group in GrpScripts)
        {
            foreach (var script in group.Scripts)
                changed += script.AutosizeTextWindows(Texts);
        }

        if (changed > 0)
            IsModified = true;
        return changed;
    }

    private void ShiftScriptTextIds(int textId, int steps)
    {
        foreach (var group in GrpScripts)
        {
            foreach (var script in group.Scripts)
                script.ShiftTextIds(textId, steps);
        }
    }

    /// <summary>
    /// Serializes the section. Unmodified sections return the original bytes; modified sections
    /// are rebuilt exactly as <c>Section1File::save()</c> does: header, group names, AKAO/tutorial
    /// offsets, script offsets, scripts, text table (each text terminated by 0xFF, word-padded
    /// when AKAO/tutorial entries follow) and AKAO/tutorial blocks (AKAO blocks 4-byte aligned).
    /// </summary>
    /// <returns>The section 1 bytes.</returns>
    /// <exception cref="InvalidDataException">Thrown when offsets overflow 16 bits.</exception>
    /// <exception cref="NotSupportedException">Thrown when saving a modified PS demo (0x0301) section.</exception>
    public byte[] Save()
    {
        if (!IsModified)
            return RawData.ToArray();

        if (_isDemo)
            throw new NotSupportedException("Saving modified PlayStation demo (0x0301) script sections is not supported.");

        if (GrpScripts.Count > byte.MaxValue)
            throw new InvalidDataException("Section 1 cannot contain more than 255 groups.");

        var tutorials = TutorialsAndSounds?.Entries.Select(static entry => entry.RawData).ToArray() ?? [];
        var groupCount = GrpScripts.Count;
        var akaoCount = tutorials.Length;
        var posScripts = 32 + (groupCount * 72) + (akaoCount * 4);

        var groupNames = new List<byte>(groupCount * 8);
        var scriptPositions = new List<byte>(groupCount * 64);
        var allScripts = new List<byte>();
        var pos32 = (uint)posScripts;
        byte modelCount = 0;

        foreach (var group in GrpScripts)
        {
            groupNames.AddRange(FixedLatin1(group.Name, 8, forceTerminator: false));
            for (var fileScript = 0; fileScript < 32; fileScript++)
            {
                var script = group.ToByteArray(fileScript);
                if (script.Length > 0)
                    pos32 = (uint)(posScripts + allScripts.Count);
                if (pos32 > ushort.MaxValue)
                    throw new InvalidDataException("Section 1 script size overflow.");

                AddUInt16(scriptPositions, (ushort)pos32);
                allScripts.AddRange(script);
            }

            group.DetectType();
            if (group.Type == GrpScriptType.Model)
                modelCount++;
        }

        var posTexts32 = posScripts + allScripts.Count;
        if (posTexts32 > ushort.MaxValue)
            throw new InvalidDataException("Section 1 script size overflow.");

        if (Texts.Count > MaxTextCount)
            throw new InvalidDataException($"Section 1 text count cannot exceed {MaxTextCount}.");

        var textCount = Texts.Count;
        var textPositions = new List<byte>(textCount * 2);
        var allTexts = new List<byte>();
        foreach (var text in Texts)
        {
            var position = 2 + (textCount * 2) + allTexts.Count;
            if (position > ushort.MaxValue)
                throw new InvalidDataException("Section 1 script + text size overflow.");

            AddUInt16(textPositions, (ushort)position);
            allTexts.AddRange(TextData(text));
            allTexts.Add(0xFF);
        }

        var scriptsAndTextsSize = scriptPositions.Count + allScripts.Count + 2 + textPositions.Count + allTexts.Count;
        if (scriptsAndTextsSize % 4 != 0 && akaoCount > 0)
            allTexts.AddRange(new byte[4 - (scriptsAndTextsSize % 4)]);

        var posAkaos = (uint)(posTexts32 + 2 + (textCount * 2) + allTexts.Count);
        var akaoPositions = new List<byte>(akaoCount * 4);
        var allAkaos = new List<byte>();
        foreach (var tutorial in tutorials)
        {
            AddUInt32(akaoPositions, (uint)(posAkaos + allAkaos.Count));
            allAkaos.AddRange(tutorial);
            if (tutorial.AsSpan().StartsWith("AKAO"u8) && tutorial.Length % 4 != 0)
                allAkaos.AddRange(new byte[4 - (tutorial.Length % 4)]);
        }

        var output = new List<byte>(posAkaos > 0 ? (int)posAkaos + allAkaos.Count : 0);
        AddUInt16(output, Version);
        output.Add((byte)groupCount);
        output.Add(modelCount);
        AddUInt16(output, (ushort)posTexts32);
        AddUInt16(output, (ushort)akaoCount);
        AddUInt16(output, Scale);
        output.AddRange(new byte[6]);
        output.AddRange(FixedLatin1(Author, 8, forceTerminator: true));
        output.AddRange(FixedLatin1(MapName, 8, forceTerminator: true));
        output.AddRange(groupNames);
        output.AddRange(akaoPositions);
        output.AddRange(scriptPositions);
        output.AddRange(allScripts);
        AddUInt16(output, (ushort)textCount);
        output.AddRange(textPositions);
        output.AddRange(allTexts);
        output.AddRange(allAkaos);
        return [.. output];
    }

    /// <summary>
    /// Returns the bytes <c>FF7String::data()</c> would hold: loaded texts keep their raw bytes up
    /// to (not including) the 0xFF terminator; edited texts are encoded from their value.
    /// </summary>
    private byte[] TextData(FF7String text)
    {
        if (text.RawBytes is { } raw)
            return raw[..FF7TextCodec.FindTerminator(raw)];

        return FF7TextCodec.Encode(text.Value, includeTerminator: false, japanese: JapaneseText);
    }

    private static byte[] FixedLatin1(string value, int size, bool forceTerminator)
    {
        var buffer = new byte[size];
        var bytes = Encoding.Latin1.GetBytes(value ?? string.Empty);
        bytes.AsSpan(0, Math.Min(bytes.Length, size)).CopyTo(buffer);
        if (forceTerminator)
            buffer[size - 1] = 0;
        return buffer;
    }

    private static void AddUInt16(List<byte> output, ushort value)
    {
        output.Add((byte)value);
        output.Add((byte)(value >> 8));
    }

    private static void AddUInt32(List<byte> output, uint value)
    {
        output.Add((byte)value);
        output.Add((byte)(value >> 8));
        output.Add((byte)(value >> 16));
        output.Add((byte)(value >> 24));
    }

    public int? ModelId(int grpScriptId)
    {
        // Simplified: would look up the model ID from the group script
        return null;
    }

    /// <summary>
    /// Port of the script part of <c>Section1File::open</c>, including its handling of empty
    /// groups, the Android "after scripts" detection and the ownership of repeated pointers (a
    /// script block belongs to the first slot of a run of identical pointers).
    /// </summary>
    private void LoadScripts(
        ReadOnlySpan<byte> data,
        int groupNamesOffset,
        int scriptOffsetsOffset,
        int groupCount,
        int scriptCount,
        uint posAkao)
    {
        var posTexts = TextSectionOffset;
        var posAfterScripts = (ushort)Math.Min(posAkao, posTexts);
        var posAfterScriptsAndroid = -1;
        if (posAkao < posTexts && posTexts + 4 < data.Length)
            posAfterScriptsAndroid = data[..Math.Min((int)posAkao + 4, data.Length)]
                .LastIndexOf(data.Slice(posTexts, 4));

        var positions = new ushort[scriptCount + 1];
        var emptyGroups = 0;

        ushort EndOfLastGroup(ushort lastStart) =>
            posAfterScriptsAndroid >= lastStart ? (ushort)posAfterScriptsAndroid : posAfterScripts;

        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var groupName = ReadFixedLatin1(data.Slice(groupNamesOffset + (8 * groupIndex), 8));
            var group = new GrpScript(groupName);

            if (emptyGroups > 1)
            {
                emptyGroups--;
                GrpScripts.Add(group);
                continue;
            }

            for (var scriptIndex = 0; scriptIndex < scriptCount; scriptIndex++)
                positions[scriptIndex] = ReadPosition(data, scriptOffsetsOffset, scriptCount, groupIndex, scriptIndex);

            var lastStart = positions[scriptCount - 1];
            if (groupIndex == groupCount - 1)
            {
                positions[scriptCount] = EndOfLastGroup(lastStart);
            }
            else
            {
                var next = ReadPosition(data, scriptOffsetsOffset, scriptCount, groupIndex + 1, 0);
                if (next > lastStart)
                {
                    positions[scriptCount] = next;
                }
                else
                {
                    emptyGroups = 1;
                    while (next <= lastStart && groupIndex + emptyGroups < groupCount - 1)
                    {
                        next = ReadPosition(data, scriptOffsetsOffset, scriptCount, groupIndex + emptyGroups + 1, 0);
                        emptyGroups++;
                    }

                    positions[scriptCount] = groupIndex + emptyGroups == groupCount
                        ? EndOfLastGroup(lastStart)
                        : next;
                }
            }

            var slot = 0;
            for (var scriptIndex = 0; scriptIndex < scriptCount; scriptIndex++)
            {
                var start = positions[scriptIndex];
                var end = positions[scriptIndex + 1];
                if (end <= start)
                    continue;
                if (end > data.Length)
                    throw new InvalidDataException($"Section 1 script {groupIndex}:{scriptIndex} is out of range.");

                var bytes = data.Slice(start, end - start);
                if (slot == 0)
                {
                    var (init, main) = Script.SplitAtReturn(bytes);
                    group.SetScript(0, new Script(init));
                    group.SetScript(1, new Script(main));
                }
                else
                {
                    group.SetScript(slot + 1, new Script(bytes.ToArray()));
                }

                slot = scriptIndex + 1;
            }

            GrpScripts.Add(group);
        }
    }

    private static ushort ReadPosition(
        ReadOnlySpan<byte> data,
        int scriptOffsetsOffset,
        int scriptCount,
        int groupIndex,
        int scriptIndex) =>
        BinaryPrimitives.ReadUInt16LittleEndian(
            data.Slice(scriptOffsetsOffset + (((groupIndex * scriptCount) + scriptIndex) * 2), 2));

    private void LoadTexts(ReadOnlySpan<byte> data, int posTexts, uint posAkao)
    {
        var textSectionSize = posAkao >= posTexts
            ? (int)posAkao - posTexts
            : data.Length - posTexts;

        if (textSectionSize <= 4 || posTexts + textSectionSize > data.Length)
            return;

        var textSection = data.Slice(posTexts, textSectionSize);
        var firstTextOffset = BinaryPrimitives.ReadUInt16LittleEndian(textSection.Slice(2, 2));
        if (firstTextOffset == 0 || firstTextOffset > textSection.Length)
            return;

        var textCount = (firstTextOffset / 2) - 1;
        if (textCount <= 0)
            return;

        var offsets = new List<ushort>(textCount + 1);
        for (var i = 0; i < textCount; i++)
        {
            var offsetPosition = 2 + (i * 2);
            if (offsetPosition + 2 > textSection.Length)
                return;
            offsets.Add(BinaryPrimitives.ReadUInt16LittleEndian(textSection.Slice(offsetPosition, 2)));
        }

        offsets.Add((ushort)textSection.Length);

        for (var i = 0; i < textCount; i++)
        {
            var start = offsets[i];
            var end = offsets[i + 1];
            if (end < start || end > textSection.Length)
                break;

            var raw = textSection.Slice(start, end - start);
            Texts.Add(FF7String.FromRaw(raw, JapaneseText));
        }
    }

    private static string ReadFixedLatin1(ReadOnlySpan<byte> bytes)
    {
        var length = bytes.IndexOf((byte)0);
        if (length < 0)
            length = bytes.Length;
        return Encoding.Latin1.GetString(bytes[..length]);
    }

}

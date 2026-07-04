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
    private int _akaoPositionsOffset;
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
        _akaoPositionsOffset = akaoPositionsOffset;
        var scriptOffsetsOffset = akaoPositionsOffset + (4 * AkaoCount);
        var scriptCount = isDemo ? 16 : 32;
        var scriptOffsetsSize = groupCount * scriptCount * 2;

        if (scriptOffsetsOffset + scriptOffsetsSize > TextSectionOffset)
            throw new InvalidDataException("Section 1 script offset table exceeds the text section.");

        var firstAkaoOffset = AkaoCount > 0
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(akaoPositionsOffset, 4))
            : (uint)data.Length;
        _firstAkaoOffset = firstAkaoOffset;
        var afterScripts = (int)Math.Min(firstAkaoOffset, TextSectionOffset);

        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var groupName = ReadFixedLatin1(data.Slice(groupNamesOffset + (8 * groupIndex), 8));
            var group = new GrpScript(groupName);
            var positions = new ushort[scriptCount + 1];
            var groupOffsetsStart = scriptOffsetsOffset + (groupIndex * scriptCount * 2);
            for (var scriptIndex = 0; scriptIndex < scriptCount; scriptIndex++)
            {
                positions[scriptIndex] = BinaryPrimitives.ReadUInt16LittleEndian(
                    data.Slice(groupOffsetsStart + (scriptIndex * 2), 2));
            }

            positions[scriptCount] = FindNextGroupStart(data, scriptOffsetsOffset, groupIndex, groupCount, scriptCount, positions[scriptCount - 1], afterScripts);

            for (var scriptIndex = 0; scriptIndex < scriptCount; scriptIndex++)
            {
                var start = positions[scriptIndex];
                var end = positions[scriptIndex + 1];
                if (end <= start)
                    continue;
                if (start < scriptOffsetsOffset || end > data.Length)
                    throw new InvalidDataException($"Section 1 script {groupIndex}:{scriptIndex} is out of range.");

                group.SetScript(scriptIndex, new Script(data.Slice(start, end - start).ToArray()));
            }

            GrpScripts.Add(group);
        }

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

    public byte[] Save()
    {
        if (!IsModified)
            return RawData.ToArray();

        var textStart = TextSectionOffset;
        var oldTextEnd = _firstAkaoOffset >= textStart && _firstAkaoOffset <= RawData.Length
            ? (int)_firstAkaoOffset
            : RawData.Length;
        var oldTextLength = oldTextEnd - textStart;
        var newTextSection = BuildTextSection(Texts);
        var delta = newTextSection.Length - oldTextLength;

        var prefix = RawData[..textStart].ToArray();
        ApplyCurrentScriptBytes(prefix);
        var output = new byte[prefix.Length + newTextSection.Length + (RawData.Length - oldTextEnd)];
        prefix.CopyTo(output, 0);

        if (delta != 0 && AkaoCount > 0)
            ShiftAkaoOffsets(output.AsSpan(0, prefix.Length), oldTextEnd, delta);

        newTextSection.CopyTo(output, textStart);
        RawData.AsSpan(oldTextEnd).CopyTo(output.AsSpan(textStart + newTextSection.Length));
        return output;
    }

    private void ApplyCurrentScriptBytes(byte[] prefix)
    {
        var groupCount = GrpScripts.Count;
        var scriptCount = _isDemo ? 16 : 32;
        var groupNamesOffset = _isDemo ? 24 : 32;
        var scriptOffsetsOffset = groupNamesOffset + (groupCount * 8) + (AkaoCount * 4);

        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var offsetsStart = scriptOffsetsOffset + (groupIndex * scriptCount * 2);
            for (var scriptIndex = 0; scriptIndex < scriptCount; scriptIndex++)
            {
                var offsetPosition = offsetsStart + (scriptIndex * 2);
                if (offsetPosition + 2 > prefix.Length)
                    return;

                var start = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(offsetPosition, 2));
                var end = FindScriptEnd(prefix, scriptOffsetsOffset, groupIndex, scriptIndex, groupCount, scriptCount, start);
                if (end <= start || end > prefix.Length)
                    continue;

                var scriptBytes = GrpScripts[groupIndex].Scripts[scriptIndex].Compile();
                if (scriptBytes.Length != end - start)
                    continue;

                scriptBytes.CopyTo(prefix.AsSpan(start, scriptBytes.Length));
            }
        }
    }

    private static int FindScriptEnd(
        byte[] prefix,
        int scriptOffsetsOffset,
        int groupIndex,
        int scriptIndex,
        int groupCount,
        int scriptCount,
        ushort start)
    {
        for (var currentGroup = groupIndex; currentGroup < groupCount; currentGroup++)
        {
            var firstScript = currentGroup == groupIndex ? scriptIndex + 1 : 0;
            var offsetsStart = scriptOffsetsOffset + (currentGroup * scriptCount * 2);
            for (var currentScript = firstScript; currentScript < scriptCount; currentScript++)
            {
                var offsetPosition = offsetsStart + (currentScript * 2);
                if (offsetPosition + 2 > prefix.Length)
                    return prefix.Length;

                var candidate = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(offsetPosition, 2));
                if (candidate > start)
                    return candidate;
            }
        }

        return prefix.Length;
    }

    public int? ModelId(int grpScriptId)
    {
        // Simplified: would look up the model ID from the group script
        return null;
    }

    private static ushort FindNextGroupStart(
        ReadOnlySpan<byte> data,
        int scriptOffsetsOffset,
        int groupIndex,
        int groupCount,
        int scriptCount,
        ushort lastScriptStart,
        int afterScripts)
    {
        for (var nextGroup = groupIndex + 1; nextGroup < groupCount; nextGroup++)
        {
            var candidate = BinaryPrimitives.ReadUInt16LittleEndian(
                data.Slice(scriptOffsetsOffset + (nextGroup * scriptCount * 2), 2));
            if (candidate > lastScriptStart)
                return candidate;
        }

        return checked((ushort)afterScripts);
    }

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

    private void ShiftAkaoOffsets(Span<byte> prefix, int oldTextEnd, int delta)
    {
        for (var index = 0; index < AkaoCount; index++)
        {
            var offset = _akaoPositionsOffset + (index * 4);
            if (offset + 4 > prefix.Length)
                return;

            var value = BinaryPrimitives.ReadUInt32LittleEndian(prefix.Slice(offset, 4));
            if (value < oldTextEnd)
                continue;

            BinaryPrimitives.WriteUInt32LittleEndian(
                prefix.Slice(offset, 4),
                checked((uint)(value + delta)));
        }
    }

    private static byte[] BuildTextSection(IReadOnlyList<FF7String> texts)
    {
        if (texts.Count > MaxTextCount)
            throw new InvalidDataException($"Section 1 text count cannot exceed {MaxTextCount}.");

        var encodedTexts = texts
            .Select(static text => FF7TextCodec.Encode(text.Value))
            .ToArray();
        var tableSize = 2 + (encodedTexts.Length * 2);
        var totalSize = tableSize + encodedTexts.Sum(static text => text.Length);
        if (totalSize > ushort.MaxValue)
            throw new InvalidDataException("Section 1 text table is too large.");

        using var stream = new MemoryStream(totalSize);
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write(checked((ushort)texts.Count));
        var offset = checked((ushort)tableSize);
        foreach (var encoded in encodedTexts)
        {
            writer.Write(offset);
            offset = checked((ushort)(offset + encoded.Length));
        }

        foreach (var encoded in encodedTexts)
            writer.Write(encoded);

        return stream.ToArray();
    }
}

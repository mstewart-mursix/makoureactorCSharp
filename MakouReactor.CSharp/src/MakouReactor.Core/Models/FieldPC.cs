using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using MakouReactor.Core.IO;

namespace MakouReactor.Core.Models;

/// <summary>
/// PC FF7 field file with the nine-section table used by flevel.lgp fields.
/// </summary>
public sealed class FieldPC : Field
{
    private const int HeaderSize = 42;
    private const int SectionCount = 9;
    private const int SectionSizeHeader = 4;
    private static readonly byte[] Footer = "FINAL FANTASY7"u8.ToArray();
    private readonly byte[] _fieldData;
    private readonly byte[]? _compressedData;
    private readonly uint[] _sectionOffsets;
    private readonly List<FieldSectionInfo> _sections;
    private byte[]? _backgroundSectionOverride;
    private bool _removeUnusedTilesSection;

    private FieldPC(string name, byte[] fieldData, byte[]? compressedData) : base(name)
    {
        _fieldData = fieldData;
        _compressedData = compressedData;
        _sectionOffsets = new uint[SectionCount];

        if (fieldData.Length < HeaderSize)
            throw new InvalidDataException("PC field data is shorter than the 42-byte header.");

        var declaredSectionCount = BinaryPrimitives.ReadUInt32LittleEndian(fieldData.AsSpan(2, 4));
        if (declaredSectionCount != SectionCount)
            throw new InvalidDataException($"Unsupported PC field section count: {declaredSectionCount}.");

        for (var i = 0; i < SectionCount; i++)
            _sectionOffsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(fieldData.AsSpan(6 + (i * 4), 4));

        _sections = BuildSectionInfos(fieldData, _sectionOffsets);
        TryOpenSection1();
        TryOpenModelLoader();
        TryOpenWalkmesh();
        TryOpenEncounters();
        TryOpenBackground();
        TryOpenInf();
        IsOpen = true;
    }

    /// <summary>
    /// Gets the decompressed field bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Data => _fieldData;

    /// <summary>
    /// Gets metadata for all nine PC field sections.
    /// </summary>
    public IReadOnlyList<FieldSectionInfo> Sections => _sections;

    /// <inheritdoc />
    public override bool IsPC() => true;

    /// <summary>
    /// Opens a field from archive-stored, header-prefixed LZS bytes.
    /// </summary>
    /// <param name="name">Field name.</param>
    /// <param name="lzsData">Header-prefixed LZS data from flevel.lgp.</param>
    /// <returns>The parsed PC field.</returns>
    public static FieldPC OpenCompressed(string name, ReadOnlySpan<byte> lzsData) =>
        new(name, LzsCompression.DecompressWithHeader(lzsData), lzsData.ToArray());

    /// <summary>
    /// Opens a field from already-decompressed PC field bytes.
    /// </summary>
    /// <param name="name">Field name.</param>
    /// <param name="fieldData">Decompressed PC field bytes.</param>
    /// <returns>The parsed PC field.</returns>
    public static FieldPC OpenDecompressed(string name, ReadOnlySpan<byte> fieldData) =>
        new(name, fieldData.ToArray(), compressedData: null);

    public static FieldPC CreateEmpty(string name) =>
        new(name, BuildEmptyFieldData(name), compressedData: null);

    /// <summary>
    /// Saves decompressed PC field bytes. Unmodified fields are emitted
    /// byte-identically to the opened data.
    /// </summary>
    /// <returns>Decompressed field bytes.</returns>
    public byte[] SaveDecompressed()
    {
        if (IsModified)
            return BuildModifiedFieldData();

        return _fieldData.ToArray();
    }

    /// <summary>
    /// Saves archive-ready, header-prefixed LZS bytes. If the field was opened
    /// from compressed archive data and was not modified, the original archive
    /// bytes are returned unchanged.
    /// </summary>
    /// <returns>Header-prefixed LZS data suitable for flevel.lgp.</returns>
    public byte[] SaveCompressed()
    {
        if (!IsModified)
            return _compressedData?.ToArray() ?? LzsCompression.CompressWithHeader(_fieldData);

        return LzsCompression.CompressWithHeader(SaveDecompressed());
    }

    /// <summary>
    /// Gets raw section bytes without the section-size prefix.
    /// </summary>
    /// <param name="section">Section to read.</param>
    /// <returns>The raw section payload.</returns>
    public byte[] GetSectionData(FieldSection section)
    {
        var info = Sections.First(item => item.Section == section);
        return _fieldData.AsSpan(info.DataOffset, info.Size).ToArray();
    }

    public void SetText(int textId, FF7String text)
    {
        if (ScriptsAndTexts == null)
            throw new InvalidOperationException("Section 1 scripts/texts are not available for this field.");

        ScriptsAndTexts.SetText(textId, text);
        SetModified();
    }

    public void InsertText(int textId, FF7String text)
    {
        if (ScriptsAndTexts == null)
            throw new InvalidOperationException("Section 1 scripts/texts are not available for this field.");

        ScriptsAndTexts.InsertText(textId, text);
        SetModified();
    }

    public void DeleteText(int textId)
    {
        if (ScriptsAndTexts == null)
            throw new InvalidOperationException("Section 1 scripts/texts are not available for this field.");

        ScriptsAndTexts.DeleteText(textId);
        SetModified();
    }

    public void ApplyEncounterChanges()
    {
        if (Encounters == null)
            throw new InvalidOperationException("Encounter data is not available for this field.");

        SetModified();
    }

    public void ApplyWalkmeshChanges()
    {
        if (Walkmesh == null)
            throw new InvalidOperationException("Walkmesh data is not available for this field.");

        SetModified();
    }

    public void ApplyModelLoaderChanges()
    {
        if (ModelLoader == null)
            throw new InvalidOperationException("Model loader data is not available for this field.");

        SetModified();
    }

    public void ApplyInfChanges()
    {
        if (Inf == null)
            throw new InvalidOperationException("INF data is not available for this field.");

        SetModified();
    }

    public bool RemoveUnusedTilesSection()
    {
        if (_removeUnusedTilesSection)
            return false;

        _removeUnusedTilesSection = true;
        SetModified();
        return true;
    }

    public BackgroundResizeResult ResizeBackgroundToMinimumWidth(int minimumWidth)
    {
        if (Background == null)
            return new BackgroundResizeResult(false, 0, GetSectionData(FieldSection.Background));

        var result = Background.ResizeToMinimumWidth(minimumWidth);
        if (!result.Changed)
            return result;

        _backgroundSectionOverride = result.Data;
        Background = BackgroundFilePC.Open(result.Data);
        SetModified();
        return result;
    }

    public BackgroundRepairResult RepairBackgroundPaletteReferences()
    {
        if (Background == null)
            return new BackgroundRepairResult(false, 0, GetSectionData(FieldSection.Background));

        var result = Background.RepairInvalidPaletteReferences(GetPaletteCount());
        if (!result.Changed)
            return result;

        _backgroundSectionOverride = result.Data;
        Background = BackgroundFilePC.Open(result.Data);
        SetModified();
        return result;
    }

    public void ReplaceBackgroundSection(ReadOnlySpan<byte> data)
    {
        var parsed = BackgroundFilePC.Open(data);
        _backgroundSectionOverride = data.ToArray();
        Background = parsed;
        SetModified();
    }

    public override void SetSaved()
    {
        base.SetSaved();
        if (ScriptsAndTexts != null)
            ScriptsAndTexts.Load(ScriptsAndTexts.Save(), ScriptsAndTexts.JapaneseText);
    }

    private byte[] BuildModifiedFieldData()
    {
        var payloads = Sections
            .OrderBy(static section => section.Index)
            .Select(section => section.Section == FieldSection.Scripts && ScriptsAndTexts != null
                ? ScriptsAndTexts.Save()
                : section.Section == FieldSection.Tiles && _removeUnusedTilesSection
                    ? Array.Empty<byte>()
                    : section.Section == FieldSection.Encounter && Encounters != null
                        ? Encounters.Save()
                        : section.Section == FieldSection.Walkmesh && Walkmesh != null
                            ? Walkmesh.Save()
                            : section.Section == FieldSection.ModelLoader && ModelLoader != null
                                ? ModelLoader.Save()
                                : section.Section == FieldSection.Inf && Inf != null
                                    ? Inf.Save()
                                    : section.Section == FieldSection.Background && _backgroundSectionOverride != null
                                        ? _backgroundSectionOverride.ToArray()
                                        : _fieldData.AsSpan(section.DataOffset, section.Size).ToArray())
            .ToArray();

        var totalSize = HeaderSize + payloads.Sum(static payload => SectionSizeHeader + payload.Length) + Footer.Length;
        var output = new byte[totalSize];
        _fieldData.AsSpan(0, 6).CopyTo(output);
        var cursor = HeaderSize;
        for (var index = 0; index < payloads.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(6 + (index * 4), 4), checked((uint)cursor));
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(cursor, 4), checked((uint)payloads[index].Length));
            payloads[index].CopyTo(output.AsSpan(cursor + SectionSizeHeader));
            cursor += SectionSizeHeader + payloads[index].Length;
        }

        Footer.CopyTo(output.AsSpan(cursor));
        return output;
    }

    private static byte[] BuildEmptyFieldData(string name)
    {
        var sections = new[]
        {
            BuildEmptySection1(name),
            Array.Empty<byte>(),
            new FieldModelLoaderPC().Save(),
            BuildEmptyPaletteSection(),
            new IdFile().Save(),
            Array.Empty<byte>(),
            BuildEmptyEncounterSection(),
            BuildEmptyInfSection(name),
            Array.Empty<byte>(),
        };

        return BuildPcFieldData(sections);
    }

    private static byte[] BuildPcFieldData(IReadOnlyList<byte[]> sections)
    {
        if (sections.Count != SectionCount)
            throw new InvalidDataException($"PC field must contain {SectionCount} sections.");

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write((ushort)0);
        writer.Write((uint)SectionCount);
        for (var i = 0; i < SectionCount; i++)
            writer.Write(0u);

        var offsets = new uint[SectionCount];
        for (var i = 0; i < SectionCount; i++)
        {
            offsets[i] = checked((uint)stream.Position);
            writer.Write(checked((uint)sections[i].Length));
            writer.Write(sections[i]);
        }

        writer.Write(Footer);
        stream.Position = 6;
        foreach (var offset in offsets)
            writer.Write(offset);

        return stream.ToArray();
    }

    private static byte[] BuildEmptySection1(string name)
    {
        var script = new byte[] { 0x00 };
        var textSection = BuildTextSection(["Map name", "Hello world!"]);
        const int groupCount = 1;
        const int scriptCount = 32;
        var headerSize = 32;
        var scriptOffsetsOffset = headerSize + (groupCount * 8);
        var scriptDataOffset = scriptOffsetsOffset + (groupCount * scriptCount * 2);
        var textOffset = checked((ushort)(scriptDataOffset + script.Length));

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)0x0502);
        writer.Write((byte)groupCount);
        writer.Write((byte)0);
        writer.Write(textOffset);
        writer.Write((ushort)0);
        writer.Write((ushort)512);
        writer.Write(new byte[6]);
        WriteFixed(writer, "makou", 8);
        WriteFixed(writer, name, 8);
        WriteFixed(writer, "dic", 8);

        var scriptStart = checked((ushort)scriptDataOffset);
        for (var i = 0; i < scriptCount; i++)
            writer.Write(i == 0 ? scriptStart : textOffset);

        writer.Write(script);
        writer.Write(textSection);
        return stream.ToArray();
    }

    private static byte[] BuildTextSection(IReadOnlyList<string> texts)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)texts.Count);
        var offset = checked((ushort)(2 + (texts.Count * 2)));
        foreach (var text in texts)
        {
            writer.Write(offset);
            offset += checked((ushort)(FF7TextCodec.Encode(text).Length + 1));
        }

        foreach (var text in texts)
        {
            writer.Write(FF7TextCodec.Encode(text));
            writer.Write((byte)0xFF);
        }

        return stream.ToArray();
    }

    private static byte[] BuildEmptyPaletteSection()
    {
        var data = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 12);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8, 2), 256);
        return data;
    }

    private static byte[] BuildEmptyEncounterSection() => new byte[48];

    private static byte[] BuildEmptyInfSection(string name)
    {
        var data = new byte[740];
        var encodedName = Encoding.Latin1.GetBytes(name);
        encodedName.AsSpan(0, Math.Min(encodedName.Length, 8)).CopyTo(data);
        data[9] = 128;
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12, 2), -256);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(14, 2), -256);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(16, 2), 256);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(18, 2), 256);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(24, 2), 1024);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(26, 2), 1024);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(28, 2), 1024);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(30, 2), 1024);
        for (var i = 0; i < 12; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(74 + (i * 24), 2), 0x7FFF);
            data[440 + (i * 16)] = 0xFF;
        }

        return data;
    }

    private static void WriteFixed(BinaryWriter writer, string value, int length)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        writer.Write(bytes.AsSpan(0, Math.Min(bytes.Length, length)));
        if (bytes.Length < length)
            writer.Write(new byte[length - bytes.Length]);
    }

    private static List<FieldSectionInfo> BuildSectionInfos(byte[] fieldData, uint[] offsets)
    {
        var result = new List<FieldSectionInfo>(SectionCount);
        var footerStart = FindFooterStart(fieldData);

        for (var i = 0; i < SectionCount; i++)
        {
            var storedOffset = checked((int)offsets[i]);
            if (storedOffset < HeaderSize || storedOffset + SectionSizeHeader > fieldData.Length)
                throw new InvalidDataException($"PC field section {i + 1} has an invalid offset.");

            var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(
                fieldData.AsSpan(storedOffset, SectionSizeHeader));
            var dataOffset = storedOffset + SectionSizeHeader;
            var maxEnd = i < SectionCount - 1 ? checked((int)offsets[i + 1]) : footerStart;
            if (maxEnd < dataOffset)
                throw new InvalidDataException($"PC field section {i + 1} overlaps the following section.");

            var maxSize = maxEnd - dataOffset;
            var size = checked((int)declaredSize);
            if (size > maxSize)
                throw new InvalidDataException($"PC field section {i + 1} size exceeds its section range.");

            result.Add(new FieldSectionInfo(i + 1, FieldSectionForIndex(i), storedOffset, dataOffset, size));
        }

        return result;
    }

    private static int FindFooterStart(byte[] fieldData)
    {
        if (fieldData.Length >= Footer.Length &&
            fieldData.AsSpan(fieldData.Length - Footer.Length).SequenceEqual(Footer))
        {
            return fieldData.Length - Footer.Length;
        }

        return fieldData.Length;
    }

    private static FieldSection FieldSectionForIndex(int index) => index switch
    {
        0 => FieldSection.Scripts,
        1 => FieldSection.Camera,
        2 => FieldSection.ModelLoader,
        3 => FieldSection.PalettePC,
        4 => FieldSection.Walkmesh,
        5 => FieldSection.Tiles,
        6 => FieldSection.Encounter,
        7 => FieldSection.Inf,
        8 => FieldSection.Background,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    private void TryOpenSection1()
    {
        try
        {
            ScriptsAndTexts = Section1File.Open(GetSectionData(FieldSection.Scripts));
        }
        catch
        {
            ScriptsAndTexts = null;
        }
    }

    private void TryOpenModelLoader()
    {
        try
        {
            ModelLoader = FieldModelLoaderPC.Open(GetSectionData(FieldSection.ModelLoader));
        }
        catch
        {
            ModelLoader = null;
        }
    }

    private void TryOpenWalkmesh()
    {
        try
        {
            Walkmesh = IdFile.Open(GetSectionData(FieldSection.Walkmesh));
        }
        catch
        {
            Walkmesh = null;
        }
    }

    private void TryOpenEncounters()
    {
        try
        {
            Encounters = EncounterFile.Open(GetSectionData(FieldSection.Encounter));
        }
        catch
        {
            Encounters = null;
        }
    }

    private void TryOpenBackground()
    {
        try
        {
            Background = BackgroundFilePC.Open(GetSectionData(FieldSection.Background));
        }
        catch
        {
            Background = null;
        }
    }

    private void TryOpenInf()
    {
        try
        {
            Inf = InfFile.Open(GetSectionData(FieldSection.Inf));
        }
        catch
        {
            Inf = null;
        }
    }

    private int GetPaletteCount()
    {
        var paletteData = GetSectionData(FieldSection.PalettePC);
        return paletteData.Length >= 12
            ? BinaryPrimitives.ReadUInt16LittleEndian(paletteData.AsSpan(10, 2))
            : 0;
    }
}

/// <summary>
/// Metadata for a PC field section.
/// </summary>
/// <param name="Index">One-based PC section index.</param>
/// <param name="Section">Logical section.</param>
/// <param name="StoredOffset">Offset to the section-size prefix.</param>
/// <param name="DataOffset">Offset to the section payload.</param>
/// <param name="Size">Section payload size.</param>
public sealed record FieldSectionInfo(
    int Index,
    FieldSection Section,
    int StoredOffset,
    int DataOffset,
    int Size);

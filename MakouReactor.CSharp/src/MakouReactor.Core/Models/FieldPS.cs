using System.Buffers.Binary;
using System.IO;
using System.Linq;

using MakouReactor.Core.IO;

namespace MakouReactor.Core.Models;

/// <summary>
/// PlayStation FF7 field DAT metadata with the seven-section offset table.
/// </summary>
public sealed class FieldPS : Field
{
    private const int HeaderSize = 28;
    private const int SectionCount = 7;
    private byte[] _fieldData;
    private byte[]? _compressedData;
    private uint[] _sectionOffsets;
    private List<FieldSectionInfo> _sections;
    private byte[][] _sectionPayloads;
    private readonly int _vramDiff;

    private FieldPS(string name, byte[] fieldData, byte[]? compressedData) : base(name)
    {
        _fieldData = fieldData;
        _compressedData = compressedData;
        _sectionOffsets = new uint[SectionCount];

        if (fieldData.Length < HeaderSize)
            throw new InvalidDataException("PlayStation field data is shorter than the 28-byte header.");

        for (var index = 0; index < SectionCount; index++)
            _sectionOffsets[index] = BinaryPrimitives.ReadUInt32LittleEndian(fieldData.AsSpan(index * 4, 4));

        _vramDiff = checked((int)_sectionOffsets[0]) - HeaderSize;
        for (var index = 0; index < SectionCount; index++)
            _sectionOffsets[index] = checked((uint)(checked((int)_sectionOffsets[index]) - _vramDiff));

        _sections = BuildSectionInfos(fieldData, _sectionOffsets);
        _sectionPayloads = _sections
            .OrderBy(static section => section.Index)
            .Select(section => fieldData.AsSpan(section.DataOffset, section.Size).ToArray())
            .ToArray();
        ReopenParsedSections();
        IsOpen = true;
    }

    public ReadOnlyMemory<byte> Data => IsModified ? SaveDecompressed() : _fieldData;

    public IReadOnlyList<FieldSectionInfo> Sections => _sections;

    public FieldModelLoaderPS? ModelLoaderPS { get; private set; }

    public override bool IsPC() => false;

    public static FieldPS OpenCompressed(string name, ReadOnlySpan<byte> lzsData) =>
        new(name, LzsCompression.DecompressWithHeader(lzsData), lzsData.ToArray());

    public static FieldPS OpenDecompressed(string name, ReadOnlySpan<byte> fieldData) =>
        new(name, fieldData.ToArray(), compressedData: null);

    public byte[] GetSectionData(FieldSection section)
    {
        var index = SectionIndexForSection(section);
        return _sectionPayloads[index].ToArray();
    }

    public void SetSectionData(FieldSection section, ReadOnlySpan<byte> data)
    {
        var index = SectionIndexForSection(section);
        if (_sectionPayloads[index].AsSpan().SequenceEqual(data))
            return;

        _sectionPayloads[index] = data.ToArray();
        RebuildSectionMetadataFromPayloads();
        SetModified();
        ReopenParsedSections();
    }

    public byte[] SaveDecompressed()
    {
        if (!IsModified)
            return _fieldData.ToArray();

        return BuildModifiedFieldData();
    }

    public byte[] SaveCompressed()
    {
        if (!IsModified)
            return _compressedData?.ToArray() ?? LzsCompression.CompressWithHeader(_fieldData);

        return LzsCompression.CompressWithHeader(SaveDecompressed());
    }

    public override void SetSaved()
    {
        var saved = SaveDecompressed();
        _fieldData = saved;
        _compressedData = LzsCompression.CompressWithHeader(saved);
        _sectionOffsets = new uint[SectionCount];
        for (var index = 0; index < SectionCount; index++)
            _sectionOffsets[index] = BinaryPrimitives.ReadUInt32LittleEndian(saved.AsSpan(index * 4, 4));
        for (var index = 0; index < SectionCount; index++)
            _sectionOffsets[index] = checked((uint)(checked((int)_sectionOffsets[index]) - _vramDiff));

        _sections = BuildSectionInfos(saved, _sectionOffsets);
        _sectionPayloads = _sections
            .OrderBy(static section => section.Index)
            .Select(section => saved.AsSpan(section.DataOffset, section.Size).ToArray())
            .ToArray();
        ReopenParsedSections();
        base.SetSaved();
    }

    private byte[] BuildModifiedFieldData()
    {
        var totalSize = HeaderSize + _sectionPayloads.Sum(static payload => payload.Length);
        var output = new byte[totalSize];
        var cursor = HeaderSize;
        for (var index = 0; index < _sectionPayloads.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                output.AsSpan(index * sizeof(uint), sizeof(uint)),
                checked((uint)(cursor + _vramDiff)));
            _sectionPayloads[index].CopyTo(output.AsSpan(cursor));
            cursor += _sectionPayloads[index].Length;
        }

        return output;
    }

    private void RebuildSectionMetadataFromPayloads()
    {
        var offsets = new uint[SectionCount];
        var result = new List<FieldSectionInfo>(SectionCount);
        var cursor = HeaderSize;
        for (var index = 0; index < _sectionPayloads.Length; index++)
        {
            offsets[index] = checked((uint)cursor);
            result.Add(new FieldSectionInfo(
                index + 1,
                FieldSectionForIndex(index),
                cursor,
                cursor,
                _sectionPayloads[index].Length));
            cursor += _sectionPayloads[index].Length;
        }

        _sectionOffsets = offsets;
        _sections = result;
    }

    private static List<FieldSectionInfo> BuildSectionInfos(byte[] fieldData, uint[] offsets)
    {
        var result = new List<FieldSectionInfo>(SectionCount);
        for (var index = 0; index < SectionCount; index++)
        {
            var dataOffset = checked((int)offsets[index]);
            if (dataOffset < HeaderSize || dataOffset > fieldData.Length)
                throw new InvalidDataException($"PlayStation field section {index + 1} has an invalid offset.");

            var maxEnd = index < SectionCount - 1
                ? checked((int)offsets[index + 1])
                : fieldData.Length;
            if (maxEnd < dataOffset)
                throw new InvalidDataException($"PlayStation field section {index + 1} overlaps the following section.");

            result.Add(new FieldSectionInfo(
                index + 1,
                FieldSectionForIndex(index),
                dataOffset,
                dataOffset,
                maxEnd - dataOffset));
        }

        return result;
    }

    private static FieldSection FieldSectionForIndex(int index) => index switch
    {
        0 => FieldSection.Scripts,
        1 => FieldSection.Walkmesh,
        2 => FieldSection.Background,
        3 => FieldSection.Camera,
        4 => FieldSection.Inf,
        5 => FieldSection.Encounter,
        6 => FieldSection.ModelLoader,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    private static int SectionIndexForSection(FieldSection section) => section switch
    {
        FieldSection.Scripts => 0,
        FieldSection.Walkmesh => 1,
        FieldSection.Background => 2,
        FieldSection.Camera => 3,
        FieldSection.Inf => 4,
        FieldSection.Encounter => 5,
        FieldSection.ModelLoader => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, "Unsupported PlayStation field section."),
    };

    private void ReopenParsedSections()
    {
        TryOpenSection1();
        TryOpenModelLoader();
        TryOpenWalkmesh();
        TryOpenEncounters();
        TryOpenInf();
    }

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
            ModelLoaderPS = FieldModelLoaderPS.Open(GetSectionData(FieldSection.ModelLoader));
        }
        catch
        {
            ModelLoaderPS = null;
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
}

using System.Buffers.Binary;

using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public sealed class FieldPSTests
{
    [Fact]
    public void open_decompressed_reads_playstation_section_table()
    {
        var sections = Enumerable.Range(1, 7)
            .Select(index => Enumerable.Repeat((byte)index, index).ToArray())
            .ToArray();
        sections[6] = FieldModelLoaderPSTests.BuildModelLoader();

        var field = FieldPS.OpenDecompressed("md1stin", BuildPsField(sections));

        field.Name.Should().Be("md1stin");
        field.IsOpen.Should().BeTrue();
        field.IsPC().Should().BeFalse();
        field.Sections.Select(section => section.Section).Should().Equal(
            FieldSection.Scripts,
            FieldSection.Walkmesh,
            FieldSection.Background,
            FieldSection.Camera,
            FieldSection.Inf,
            FieldSection.Encounter,
            FieldSection.ModelLoader);
        field.GetSectionData(FieldSection.Walkmesh).Should().Equal(sections[1]);
        field.ModelLoaderPS.Should().NotBeNull();
        field.ModelLoaderPS!.ModelCount.Should().Be(2);
    }

    [Fact]
    public void open_compressed_reads_header_prefixed_lzs_dat()
    {
        var sections = Enumerable.Range(1, 7)
            .Select(index => new byte[] { (byte)(0x20 + index) })
            .ToArray();
        var compressed = LzsCompression.CompressWithHeader(BuildPsField(sections));

        var field = FieldPS.OpenCompressed("md1stin", compressed);

        field.GetSectionData(FieldSection.Camera).Should().Equal(sections[3]);
        field.GetSectionData(FieldSection.Encounter).Should().Equal(sections[5]);
    }

    [Fact]
    public void field_archive_opens_playstation_field_from_loose_directory()
    {
        using var temp = new TempDirectory();
        var sections = Enumerable.Range(1, 7)
            .Select(index => new byte[] { (byte)(0x30 + index) })
            .ToArray();
        File.WriteAllBytes(Path.Combine(temp.Path, "MD1STIN.DAT"), LzsCompression.CompressWithHeader(BuildPsField(sections)));

        var archive = FieldArchive.OpenPlayStationDirectory(temp.Path);
        var field = archive.OpenPlayStationField("MD1STIN.DAT");

        field.Name.Should().Be("MD1STIN");
        field.GetSectionData(FieldSection.Inf).Should().Equal(sections[4]);
    }

    [Fact]
    public void save_decompressed_rebuilds_modified_playstation_section_table()
    {
        var sections = Enumerable.Range(1, 7)
            .Select(index => new byte[] { (byte)(0x40 + index) })
            .ToArray();
        var replacement = new byte[] { 0xAA, 0xBB, 0xCC };
        var field = FieldPS.OpenDecompressed("md1stin", BuildPsField(sections));

        field.SetSectionData(FieldSection.Walkmesh, replacement);
        var reopened = FieldPS.OpenDecompressed("md1stin", field.SaveDecompressed());

        field.IsModified.Should().BeTrue();
        reopened.GetSectionData(FieldSection.Scripts).Should().Equal(sections[0]);
        reopened.GetSectionData(FieldSection.Walkmesh).Should().Equal(replacement);
        reopened.GetSectionData(FieldSection.ModelLoader).Should().Equal(sections[6]);
    }

    [Fact]
    public void setting_identical_playstation_section_does_not_mark_field_modified()
    {
        var sections = Enumerable.Range(1, 7)
            .Select(index => new byte[] { (byte)(0x50 + index) })
            .ToArray();
        var field = FieldPS.OpenDecompressed("md1stin", BuildPsField(sections));

        field.SetSectionData(FieldSection.Background, sections[2]);

        field.IsModified.Should().BeFalse();
        field.SaveDecompressed().Should().Equal(BuildPsField(sections));
    }

    [Fact]
    public void field_archive_saves_modified_playstation_field_to_loose_directory()
    {
        using var temp = new TempDirectory();
        var sections = Enumerable.Range(1, 7)
            .Select(index => new byte[] { (byte)(0x60 + index) })
            .ToArray();
        File.WriteAllBytes(Path.Combine(temp.Path, "MD1STIN.DAT"), LzsCompression.CompressWithHeader(BuildPsField(sections)));

        var archive = FieldArchive.OpenPlayStationDirectory(temp.Path);
        var field = archive.OpenPlayStationField("MD1STIN.DAT");
        field.SetSectionData(FieldSection.Inf, [0x10, 0x20, 0x30, 0x40]);

        archive.SaveField(field);
        var reopened = FieldArchive.OpenPlayStationDirectory(temp.Path)
            .OpenPlayStationField("MD1STIN.DAT");

        field.IsModified.Should().BeFalse();
        reopened.GetSectionData(FieldSection.Inf).Should().Equal(0x10, 0x20, 0x30, 0x40);
        reopened.GetSectionData(FieldSection.Scripts).Should().Equal(sections[0]);
    }

    internal static byte[] BuildPsField(IReadOnlyList<byte[]> sections, int vramBase = 0x1000)
    {
        sections.Should().HaveCount(7);
        var headerSize = 7 * sizeof(uint);
        var size = headerSize + sections.Sum(static section => section.Length);
        var data = new byte[size];
        var cursor = headerSize;
        for (var index = 0; index < sections.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                data.AsSpan(index * sizeof(uint), sizeof(uint)),
                checked((uint)(vramBase + cursor - headerSize)));
            sections[index].CopyTo(data.AsSpan(cursor));
            cursor += sections[index].Length;
        }

        return data;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MakouReactor.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}

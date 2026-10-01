using FluentAssertions;
using System.Buffers.Binary;
using System.Text;
using System.Threading;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public class FieldArchiveTests
{
    [Fact]
    public void open_lists_only_field_like_entries_as_fields()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "flevel.lgp");

        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("maplist", [0x01]),
            new LgpArchiveFile("md1stin", [0x10]),
            new LgpArchiveFile("blackbga", [0x20]),
            new LgpArchiveFile("md1stin.tut", [0x30]),
        ]);

        var archive = FieldArchive.Open(archivePath);

        archive.FieldEntries.Select(entry => entry.Name)
            .Should().Equal("md1stin", "blackbga");
        archive.ArchiveEntries.Should().HaveCount(4);
    }

    [Theory]
    [InlineData("md1stin", true)]
    [InlineData("blackbga", true)]
    [InlineData("EXAMPLE.DAT", true)]
    [InlineData("maplist", false)]
    [InlineData("md1stin.tut", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void field_name_filter_matches_pc_flevel_convention(string name, bool expected)
    {
        FieldArchive.IsLikelyFieldName(name).Should().Be(expected);
    }

    [Fact]
    public void read_raw_file_delegates_to_archive()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "flevel.lgp");

        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("md1stin", [0x10, 0x20]),
        ]);

        var archive = FieldArchive.Open(archivePath);

        archive.ReadRawFile("md1stin").Should().Equal([0x10, 0x20]);
    }

    [Fact]
    public void open_pc_directory_lists_loose_field_files()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(Path.Combine(temp.Path, "maplist"), [0x01]);
        File.WriteAllBytes(Path.Combine(temp.Path, "md1stin"), [0x10]);
        File.WriteAllBytes(Path.Combine(temp.Path, "blackbga"), [0x20]);
        File.WriteAllBytes(Path.Combine(temp.Path, "md1stin.tut"), [0x30]);

        var archive = FieldArchive.OpenPcDirectory(temp.Path);

        archive.IsLooseDirectory.Should().BeTrue();
        archive.ArchiveEntries.Select(entry => entry.Name)
            .Should().Equal("blackbga", "maplist", "md1stin", "md1stin.tut");
        archive.FieldEntries.Select(entry => entry.Name)
            .Should().Equal("blackbga", "md1stin");
    }

    [Fact]
    public void open_playstation_directory_lists_dat_files_as_fields()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(Path.Combine(temp.Path, "BLACKBG.DAT"), [0x10]);
        File.WriteAllBytes(Path.Combine(temp.Path, "MD1STIN.DAT"), [0x20]);
        File.WriteAllBytes(Path.Combine(temp.Path, "README.TXT"), [0x30]);

        var archive = FieldArchive.OpenPlayStationDirectory(temp.Path);

        archive.Platform.Should().Be(FieldArchivePlatform.PlayStation);
        archive.IsLooseDirectory.Should().BeTrue();
        archive.ArchiveEntries.Select(entry => entry.Name)
            .Should().Equal("BLACKBG.DAT", "MD1STIN.DAT", "README.TXT");
        archive.FieldEntries.Select(entry => entry.Name)
            .Should().Equal("BLACKBG.DAT", "MD1STIN.DAT");
        archive.ReadRawFile("MD1STIN.DAT").Should().Equal([0x20]);
    }

    [Fact]
    public void read_playstation_model_data_returns_decompressed_bsx_companion()
    {
        using var temp = new TempDirectory();
        byte[] bsxData = [0x10, 0x20, 0x30, 0x40, 0x50];
        File.WriteAllBytes(Path.Combine(temp.Path, "MD1STIN.DAT"), [0x20]);
        File.WriteAllBytes(Path.Combine(temp.Path, "MD1STIN.BSX"), LzsCompression.CompressWithHeader(bsxData));

        var archive = FieldArchive.OpenPlayStationDirectory(temp.Path);

        archive.ReadPlayStationModelData("MD1STIN.DAT").Should().Equal(bsxData);
    }

    [Fact]
    public void open_playstation_image_lists_dat_files_from_iso9660_directory()
    {
        using var temp = new TempDirectory();
        var imagePath = Path.Combine(temp.Path, "ff7.iso");
        File.WriteAllBytes(imagePath, BuildIsoImageWithFieldDat());

        var archive = FieldArchive.OpenPlayStationImage(imagePath);

        archive.Platform.Should().Be(FieldArchivePlatform.PlayStation);
        archive.IsReadOnlyImage.Should().BeTrue();
        archive.IsLooseDirectory.Should().BeFalse();
        archive.ArchiveEntries.Select(entry => entry.FullPath)
            .Should().Contain("FIELD/MD1STIN.DAT");
        archive.FieldEntries.Select(entry => entry.Name)
            .Should().Equal("MD1STIN.DAT");
        archive.ReadRawFile("MD1STIN.DAT").Should().Equal([0x20, 0x21, 0x22]);
    }

    [Fact]
    public void playstation_directory_blocks_pc_field_parsing_through_pc_api()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(Path.Combine(temp.Path, "MD1STIN.DAT"), [0x20]);
        var archive = FieldArchive.OpenPlayStationDirectory(temp.Path);

        var open = () => archive.OpenField("MD1STIN.DAT");

        open.Should().Throw<NotSupportedException>()
            .WithMessage("Use OpenPlayStationField to parse PlayStation DAT fields.");
    }

    [Fact]
    public void pc_directory_raw_file_operations_update_loose_files()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(Path.Combine(temp.Path, "md1stin"), [0x10]);

        var archive = FieldArchive.OpenPcDirectory(temp.Path);
        archive.ReadRawFile("md1stin").Should().Equal([0x10]);

        archive.ReplaceRawFile("md1stin", [0x20, 0x30]);
        archive.RenameRawFile("md1stin", "renamed");

        File.Exists(Path.Combine(temp.Path, "md1stin")).Should().BeFalse();
        File.ReadAllBytes(Path.Combine(temp.Path, "renamed")).Should().Equal([0x20, 0x30]);

        archive.RemoveRawFile("renamed");

        File.Exists(Path.Combine(temp.Path, "renamed")).Should().BeFalse();
        archive.ArchiveEntries.Should().BeEmpty();
    }

    [Fact]
    public void pc_directory_save_field_writes_compressed_field_file()
    {
        using var temp = new TempDirectory();
        var fieldPath = Path.Combine(temp.Path, "md1stin");
        File.WriteAllBytes(fieldPath, LzsCompression.CompressWithHeader(BuildMinimalPcField()));

        var archive = FieldArchive.OpenPcDirectory(temp.Path);
        var field = archive.OpenField("md1stin");
        field.IsModified.Should().BeFalse();

        archive.SaveField(field);

        var reopened = FieldPC.OpenCompressed("md1stin", File.ReadAllBytes(fieldPath));
        reopened.Sections.Should().HaveCount(9);
    }

    [Fact]
    public void create_pc_field_adds_minimal_field_to_loose_directory()
    {
        using var temp = new TempDirectory();
        var archive = FieldArchive.OpenPcDirectory(temp.Path);

        var field = archive.CreatePcField("newfield");

        field.Name.Should().Be("newfield");
        field.Sections.Should().HaveCount(9);
        field.ScriptsAndTexts.Should().NotBeNull();
        field.ScriptsAndTexts!.Texts.Select(text => text.Value)
            .Should().Equal("Map name", "Hello world!");
        field.Encounters.Should().NotBeNull();
        field.Walkmesh.Should().NotBeNull();
        field.Inf.Should().NotBeNull();

        var reopened = FieldArchive.OpenPcDirectory(temp.Path);
        reopened.FieldEntries.Select(entry => entry.Name).Should().Equal("newfield");
        reopened.OpenField("newfield").ScriptsAndTexts.Should().NotBeNull();
    }

    [Fact]
    public void create_pc_field_rejects_duplicate_names()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(Path.Combine(temp.Path, "md1stin"), [0x10]);
        var archive = FieldArchive.OpenPcDirectory(temp.Path);

        var create = () => archive.CreatePcField("md1stin");

        create.Should().Throw<InvalidOperationException>()
            .WithMessage("Archive entry already exists: md1stin");
    }

    [Fact]
    public void extract_file_writes_raw_entry_bytes()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "flevel.lgp");
        var destinationPath = Path.Combine(temp.Path, "out", "md1stin");

        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("md1stin", [0x10, 0x20]),
        ]);

        var archive = FieldArchive.Open(archivePath);
        archive.ExtractFile("md1stin", destinationPath);

        File.ReadAllBytes(destinationPath).Should().Equal([0x10, 0x20]);
    }

    [Fact]
    public void extract_all_preserves_archive_paths()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "flevel.lgp");
        var destinationDirectory = Path.Combine(temp.Path, "extract");

        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("maplist", [0x01]),
            new LgpArchiveFile("dir/shared", [0xAA]),
            new LgpArchiveFile("other/shared", [0xBB, 0xCC]),
        ]);

        var archive = FieldArchive.Open(archivePath);
        var count = archive.ExtractAll(destinationDirectory);

        count.Should().Be(3);
        File.ReadAllBytes(Path.Combine(destinationDirectory, "maplist")).Should().Equal([0x01]);
        File.ReadAllBytes(Path.Combine(destinationDirectory, "dir", "shared")).Should().Equal([0xAA]);
        File.ReadAllBytes(Path.Combine(destinationDirectory, "other", "shared")).Should().Equal([0xBB, 0xCC]);
    }

    [Fact]
    public void open_honors_pre_cancelled_token()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "flevel.lgp");
        LgpArchive.Create(archivePath, [new LgpArchiveFile("md1stin", [0x10])]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var open = () => FieldArchive.Open(archivePath, cancellation.Token);

        open.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void extract_all_honors_pre_cancelled_token()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "flevel.lgp");
        var destinationDirectory = Path.Combine(temp.Path, "extract");
        LgpArchive.Create(archivePath, [new LgpArchiveFile("md1stin", [0x10])]);
        var archive = FieldArchive.Open(archivePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var extract = () => archive.ExtractAll(destinationDirectory, cancellation.Token);

        extract.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void save_field_preserves_unmodified_compressed_field_bytes()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "flevel.lgp");
        var originalFieldBytes = LzsCompression.CompressWithHeader(BuildMinimalPcField());

        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("maplist", [0x01]),
            new LgpArchiveFile("md1stin", originalFieldBytes),
        ]);

        var archive = FieldArchive.Open(archivePath);
        var field = archive.OpenField("md1stin");
        archive.SaveField(field);

        var reopened = FieldArchive.Open(archivePath);
        reopened.ReadRawFile("md1stin").Should().Equal(originalFieldBytes);
        reopened.ReadRawFile("maplist").Should().Equal([0x01]);
    }

    [Fact]
    public void save_field_persists_modified_section1_text()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "flevel.lgp");
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[0] = BuildSection1();

        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("md1stin", LzsCompression.CompressWithHeader(BuildPcField(sections))),
        ]);

        var archive = FieldArchive.Open(archivePath);
        var field = archive.OpenField("md1stin");
        field.SetText(1, new FF7String("Saved through archive"));

        archive.SaveField(field);

        field.IsModified.Should().BeFalse();
        var reopened = FieldArchive.Open(archivePath).OpenField("md1stin");
        reopened.ScriptsAndTexts!.Texts.Select(text => text.Value)
            .Should().Equal("Map name", "Saved through archive");
    }

    [Fact]
    public void replace_raw_file_rewrites_archive_and_reopens()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "flevel.lgp");

        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("maplist", [0x01]),
            new LgpArchiveFile("md1stin", [0x10]),
        ]);

        var archive = FieldArchive.Open(archivePath);
        archive.ReplaceRawFile("md1stin", [0x44, 0x55]);

        var reopened = FieldArchive.Open(archivePath);
        reopened.ReadRawFile("md1stin").Should().Equal([0x44, 0x55]);
        reopened.ReadRawFile("maplist").Should().Equal([0x01]);
        File.Exists($"{archivePath}.bak").Should().BeTrue();
    }

    [Fact]
    public void remove_raw_file_rewrites_archive_and_reopens()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "flevel.lgp");

        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("maplist", [0x01]),
            new LgpArchiveFile("md1stin", [0x10]),
            new LgpArchiveFile("blackbga", [0x20]),
        ]);

        var archive = FieldArchive.Open(archivePath);
        archive.RemoveRawFile("md1stin");

        var reopened = FieldArchive.Open(archivePath);
        reopened.ArchiveEntries.Select(entry => entry.FullPath).Should().Equal("maplist", "blackbga");
        reopened.ReadRawFile("blackbga").Should().Equal([0x20]);
        File.Exists($"{archivePath}.bak").Should().BeTrue();
    }

    [Fact]
    public void save_field_keeps_timestamped_backups_so_the_original_survives_repeated_saves()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "flevel.lgp");
        LgpArchive.Create(archivePath, [new LgpArchiveFile("md1stin", FieldPC.CreateEmpty("md1stin").SaveCompressed())]);
        var original = File.ReadAllBytes(archivePath);
        var archive = FieldArchive.Open(archivePath);

        for (var i = 0; i < 3; i++)
        {
            var field = archive.OpenField("md1stin");
            field.InsertText(0, new FF7String($"edit {i}"));
            archive.SaveField(field);
        }

        var backups = SafeArchiveWriter.ListBackups(archivePath);
        backups.Should().HaveCount(3);
        backups.Select(b => File.ReadAllBytes(b.Path)).Should().Contain(b => b.SequenceEqual(original));
    }

    private static byte[] BuildMinimalPcField()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        writer.Write((ushort)0);
        writer.Write(9u);
        for (var i = 0; i < 9; i++)
            writer.Write(0u);

        var offsets = new uint[9];
        for (var i = 0; i < 9; i++)
        {
            offsets[i] = checked((uint)stream.Position);
            writer.Write((uint)sections[i].Length);
            writer.Write(sections[i]);
        }

        writer.Write("FINAL FANTASY7"u8);

        stream.Position = 6;
        foreach (var offset in offsets)
            writer.Write(offset);

        return stream.ToArray();
    }

    private static byte[] BuildPcField(IReadOnlyList<byte[]> sections)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write((ushort)0);
        writer.Write(9u);
        for (var i = 0; i < 9; i++)
            writer.Write(0u);

        var offsets = new uint[9];
        for (var i = 0; i < 9; i++)
        {
            offsets[i] = checked((uint)stream.Position);
            writer.Write((uint)sections[i].Length);
            writer.Write(sections[i]);
        }

        writer.Write("FINAL FANTASY7"u8);

        stream.Position = 6;
        foreach (var offset in offsets)
            writer.Write(offset);

        return stream.ToArray();
    }

    private static byte[] BuildSection1()
    {
        var texts = BuildTextSection(["Map name", "Hello"]);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)0x0502);
        writer.Write((byte)0);
        writer.Write((byte)1);
        writer.Write((ushort)32);
        writer.Write((ushort)0);
        writer.Write((ushort)512);
        writer.Write(new byte[6]);
        WriteFixed(writer, "makou", 8);
        WriteFixed(writer, "md1stin", 8);
        writer.Write(texts);

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
            offset += checked((ushort)(Encoding.ASCII.GetByteCount(text) + 1));
        }

        foreach (var text in texts)
        {
            writer.Write(Encoding.ASCII.GetBytes(text));
            writer.Write((byte)0xFF);
        }

        return stream.ToArray();
    }

    private static void WriteFixed(BinaryWriter writer, string value, int length)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        writer.Write(bytes);
        writer.Write(new byte[length - bytes.Length]);
    }

    private static byte[] BuildIsoImageWithFieldDat()
    {
        const int sectorSize = 2048;
        const int pvdSector = 16;
        const int rootSector = 20;
        const int fieldSector = 21;
        const int dataSector = 22;
        var image = new byte[24 * sectorSize];

        var pvd = image.AsSpan(pvdSector * sectorSize, sectorSize);
        pvd[0] = 1;
        Encoding.ASCII.GetBytes("CD001").CopyTo(pvd[1..]);
        pvd[6] = 1;
        WriteIsoDirectoryRecord(pvd[156..], (byte)0, rootSector, sectorSize, isDirectory: true);

        var root = image.AsSpan(rootSector * sectorSize, sectorSize);
        var cursor = 0;
        cursor += WriteIsoDirectoryRecord(root[cursor..], (byte)0, rootSector, sectorSize, isDirectory: true);
        cursor += WriteIsoDirectoryRecord(root[cursor..], (byte)1, rootSector, sectorSize, isDirectory: true);
        cursor += WriteIsoDirectoryRecord(root[cursor..], "FIELD", fieldSector, sectorSize, isDirectory: true);

        var field = image.AsSpan(fieldSector * sectorSize, sectorSize);
        cursor = 0;
        cursor += WriteIsoDirectoryRecord(field[cursor..], (byte)0, fieldSector, sectorSize, isDirectory: true);
        cursor += WriteIsoDirectoryRecord(field[cursor..], (byte)1, rootSector, sectorSize, isDirectory: true);
        _ = cursor + WriteIsoDirectoryRecord(field[cursor..], "MD1STIN.DAT;1", dataSector, 3, isDirectory: false);

        image[dataSector * sectorSize] = 0x20;
        image[(dataSector * sectorSize) + 1] = 0x21;
        image[(dataSector * sectorSize) + 2] = 0x22;
        return image;
    }

    private static int WriteIsoDirectoryRecord(
        Span<byte> destination,
        string name,
        int sector,
        int size,
        bool isDirectory)
    {
        return WriteIsoDirectoryRecord(destination, Encoding.ASCII.GetBytes(name), sector, size, isDirectory);
    }

    private static int WriteIsoDirectoryRecord(
        Span<byte> destination,
        byte specialName,
        int sector,
        int size,
        bool isDirectory)
    {
        Span<byte> name = [specialName];
        return WriteIsoDirectoryRecord(destination, name, sector, size, isDirectory);
    }

    private static int WriteIsoDirectoryRecord(
        Span<byte> destination,
        ReadOnlySpan<byte> name,
        int sector,
        int size,
        bool isDirectory)
    {
        var length = 33 + name.Length;
        if ((length & 1) != 0)
            length++;
        destination[..length].Clear();
        destination[0] = checked((byte)length);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[2..], checked((uint)sector));
        BinaryPrimitives.WriteUInt32BigEndian(destination[6..], checked((uint)sector));
        BinaryPrimitives.WriteUInt32LittleEndian(destination[10..], checked((uint)size));
        BinaryPrimitives.WriteUInt32BigEndian(destination[14..], checked((uint)size));
        destination[25] = isDirectory ? (byte)0x02 : (byte)0x00;
        destination[28] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(destination[30..], 1);
        destination[32] = checked((byte)name.Length);
        name.CopyTo(destination[33..]);
        return length;
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

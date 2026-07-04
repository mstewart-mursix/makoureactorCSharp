using System.Buffers.Binary;
using System.Text;

using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;
using MakouReactor.Core.Services;

using Xunit;

namespace MakouReactor.Tests.Services;

public sealed class ArchiveBatchProcessorTests
{
    [Fact]
    public void apply_disable_battles_turns_off_encounter_tables_and_saves_field()
    {
        using var temp = new TempDirectory();
        var fieldPath = Path.Combine(temp.Path, "md1stin");
        File.WriteAllBytes(fieldPath, LzsCompression.CompressWithHeader(BuildFieldWithEncounters()));

        var archive = FieldArchive.OpenPcDirectory(temp.Path);
        var result = new ArchiveBatchProcessor().Apply(archive, ArchiveBatchOperation.DisableBattles);

        result.FieldsVisited.Should().Be(1);
        result.FieldsChanged.Should().Be(1);
        result.EncounterTablesChanged.Should().Be(2);
        result.Errors.Should().BeEmpty();

        var reopened = FieldPC.OpenCompressed("md1stin", File.ReadAllBytes(fieldPath));
        reopened.Encounters.Should().NotBeNull();
        reopened.Encounters!.Tables.Should().OnlyContain(table => !table.Enabled && table.Rate == 0);
    }

    [Fact]
    public void apply_clean_model_loader_cleans_names_and_saves_field()
    {
        using var temp = new TempDirectory();
        var fieldPath = Path.Combine(temp.Path, "md1stin");
        File.WriteAllBytes(fieldPath, LzsCompression.CompressWithHeader(BuildFieldWithModelLoader()));

        var archive = FieldArchive.OpenPcDirectory(temp.Path);
        var result = new ArchiveBatchProcessor().Apply(archive, ArchiveBatchOperation.CleanModelLoader);

        result.FieldsVisited.Should().Be(1);
        result.FieldsChanged.Should().Be(1);
        result.ModelLoaderEntriesChanged.Should().Be(3);
        result.Errors.Should().BeEmpty();

        var reopened = FieldPC.OpenCompressed("md1stin", File.ReadAllBytes(fieldPath));
        reopened.ModelLoader.Should().NotBeNull();
        reopened.ModelLoader!.Models[0].CharacterName.Should().BeEmpty();
        reopened.ModelLoader.Models[0].Animations.Select(animation => animation.Name)
            .Should().Equal("idle", "walk", "run");
    }

    [Fact]
    public void apply_remove_unused_background_sections_empties_tiles_section_and_saves_field()
    {
        using var temp = new TempDirectory();
        var fieldPath = Path.Combine(temp.Path, "md1stin");
        File.WriteAllBytes(fieldPath, LzsCompression.CompressWithHeader(BuildFieldWithTilesSection()));

        var archive = FieldArchive.OpenPcDirectory(temp.Path);
        var result = new ArchiveBatchProcessor().Apply(
            archive,
            ArchiveBatchOperation.RemoveUnusedBackgroundSections);

        result.FieldsVisited.Should().Be(1);
        result.FieldsChanged.Should().Be(1);
        result.BackgroundSectionsRemoved.Should().Be(1);
        result.Errors.Should().BeEmpty();

        var reopened = FieldPC.OpenCompressed("md1stin", File.ReadAllBytes(fieldPath));
        reopened.GetSectionData(FieldSection.Tiles).Should().BeEmpty();
        reopened.GetSectionData(FieldSection.Background).Should().Equal([0x09]);
    }

    [Fact]
    public void apply_autosize_text_windows_updates_window_opcodes_and_saves_field()
    {
        using var temp = new TempDirectory();
        var fieldPath = Path.Combine(temp.Path, "md1stin");
        File.WriteAllBytes(fieldPath, LzsCompression.CompressWithHeader(BuildFieldWithWindowScript()));

        var archive = FieldArchive.OpenPcDirectory(temp.Path);
        var result = new ArchiveBatchProcessor().Apply(archive, ArchiveBatchOperation.AutosizeTextWindows);

        result.FieldsVisited.Should().Be(1);
        result.FieldsChanged.Should().Be(1);
        result.TextWindowsAutosized.Should().Be(1);
        result.Errors.Should().BeEmpty();

        var reopened = FieldPC.OpenCompressed("md1stin", File.ReadAllBytes(fieldPath));
        var scriptBytes = reopened.ScriptsAndTexts!.GrpScripts[0].Scripts[0].RawBytes.ToArray();
        BinaryPrimitives.ReadUInt16LittleEndian(scriptBytes.AsSpan(6, 2)).Should().BeGreaterThan(10);
        BinaryPrimitives.ReadUInt16LittleEndian(scriptBytes.AsSpan(8, 2)).Should().BeGreaterThan(10);
    }

    [Fact]
    public void apply_resize_backgrounds_expands_pc_background_width_and_saves_field()
    {
        using var temp = new TempDirectory();
        var fieldPath = Path.Combine(temp.Path, "md1stin");
        File.WriteAllBytes(fieldPath, LzsCompression.CompressWithHeader(BuildFieldWithBackground()));

        var archive = FieldArchive.OpenPcDirectory(temp.Path);
        var result = new ArchiveBatchProcessor().Apply(archive, ArchiveBatchOperation.ResizeBackgrounds);

        result.FieldsVisited.Should().Be(1);
        result.FieldsChanged.Should().Be(1);
        result.BackgroundResizeTilesAdded.Should().BeGreaterThan(0);
        result.Errors.Should().BeEmpty();

        var reopened = FieldPC.OpenCompressed("md1stin", File.ReadAllBytes(fieldPath));
        reopened.Background.Should().NotBeNull();
        reopened.Background!.Layers[0].DeclaredTileCount.Should().Be(1 + result.BackgroundResizeTilesAdded);
    }

    [Fact]
    public void apply_repair_backgrounds_fixes_invalid_pc_background_palette_reference_and_saves_field()
    {
        using var temp = new TempDirectory();
        var fieldPath = Path.Combine(temp.Path, "md1stin");
        File.WriteAllBytes(fieldPath, LzsCompression.CompressWithHeader(BuildFieldWithBrokenBackground()));

        var archive = FieldArchive.OpenPcDirectory(temp.Path);
        var result = new ArchiveBatchProcessor().Apply(archive, ArchiveBatchOperation.RepairBackgrounds);

        result.FieldsVisited.Should().Be(1);
        result.FieldsChanged.Should().Be(1);
        result.BackgroundTilesRepaired.Should().Be(1);
        result.Errors.Should().BeEmpty();

        var reopened = FieldPC.OpenCompressed("md1stin", File.ReadAllBytes(fieldPath));
        reopened.Background.Should().NotBeNull();
        var repairedTile = reopened.Background!.Layers[1].Tiles[0];
        repairedTile.PaletteId.Should().Be(1);
        repairedTile.TypeTrans.Should().Be(2);
    }

    private static byte[] BuildFieldWithEncounters()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(static index => new[] { (byte)index })
            .ToArray();
        sections[6] = BuildEncounters(enabled1: true, rate1: 32, enabled2: true, rate2: 64);
        return BuildPcField(sections);
    }

    private static byte[] BuildFieldWithModelLoader()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(static index => new[] { (byte)index })
            .ToArray();
        sections[2] = MakouReactor.Tests.IO.FieldModelLoaderPCTests.BuildModelLoader(
            characterName: "Cloud",
            animations: [("idle.a", (ushort)1), ("walk", (ushort)2), ("run.anim", (ushort)3)]);
        return BuildPcField(sections);
    }

    private static byte[] BuildFieldWithTilesSection()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(static index => new[] { (byte)index })
            .ToArray();
        sections[5] = [0xAA, 0xBB, 0xCC];
        return BuildPcField(sections);
    }

    private static byte[] BuildFieldWithWindowScript()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(static index => new[] { (byte)index })
            .ToArray();
        sections[0] = BuildSection1WithScriptAndTexts(
        [
            0x50, 0x00, 0x2C, 0x01, 0xDC, 0x00, 0x0A, 0x00, 0x0A, 0x00,
            0x40, 0x00, 0x01,
        ],
        ["Map name", "Hello world"]);
        return BuildPcField(sections);
    }

    private static byte[] BuildFieldWithBackground()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(static index => new[] { (byte)index })
            .ToArray();
        sections[8] = MakouReactor.Tests.IO.BackgroundFilePCTests.BuildBackground();
        return BuildPcField(sections);
    }

    private static byte[] BuildFieldWithBrokenBackground()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(static index => new[] { (byte)index })
            .ToArray();
        sections[3] = BuildPaletteSection(2);
        sections[8] = MakouReactor.Tests.IO.BackgroundFilePCTests.BuildBackgroundWithBrokenPaletteReference();
        return BuildPcField(sections);
    }

    private static byte[] BuildSection1WithScriptAndTexts(byte[] script, IReadOnlyList<string> texts)
    {
        const int groupCount = 1;
        const int scriptCount = 32;
        var headerSize = 32;
        var scriptOffsetsOffset = headerSize + (groupCount * 8);
        var scriptDataOffset = scriptOffsetsOffset + (groupCount * scriptCount * 2);
        var posText = scriptDataOffset + script.Length;
        var textSection = BuildTextSection(texts);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)0x0502);
        writer.Write((byte)groupCount);
        writer.Write((byte)1);
        writer.Write((ushort)posText);
        writer.Write((ushort)0);
        writer.Write((ushort)512);
        writer.Write(new byte[6]);
        WriteFixed(writer, "makou", 8);
        WriteFixed(writer, "md1stin", 8);
        WriteFixed(writer, "cloud", 8);

        var first = checked((ushort)scriptDataOffset);
        var text = checked((ushort)posText);
        writer.Write(first);
        for (var i = 1; i < scriptCount; i++)
            writer.Write(text);

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
        writer.Write(bytes.AsSpan(0, Math.Min(bytes.Length, length)));
        if (bytes.Length < length)
            writer.Write(new byte[length - bytes.Length]);
    }

    private static byte[] BuildPcField(IReadOnlyList<byte[]> sections)
    {
        sections.Should().HaveCount(9);

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

    private static byte[] BuildPaletteSection(ushort paletteCount)
    {
        var data = new byte[12 + (paletteCount * 512)];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), checked((uint)data.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8, 2), 256);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10, 2), paletteCount);
        return data;
    }

    private static byte[] BuildEncounters(bool enabled1, byte rate1, bool enabled2, byte rate2)
    {
        var data = new byte[48];
        WriteTable(data.AsSpan(0, 24), enabled1, rate1, 0x100);
        WriteTable(data.AsSpan(24, 24), enabled2, rate2, 0x200);
        return data;
    }

    private static void WriteTable(Span<byte> data, bool enabled, byte rate, int battleBase)
    {
        data[0] = enabled ? (byte)1 : (byte)0;
        data[1] = rate;
        var offset = 2;
        for (var i = 0; i < 10; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.Slice(offset, 2),
                checked((ushort)(((4 + i) << 10) | ((battleBase + i) & 0x03FF))));
            offset += 2;
        }
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

using System.Buffers.Binary;
using System.Text;

using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public class FieldPCTests
{
    [Fact]
    public void open_decompressed_reads_pc_section_table()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => Enumerable.Repeat((byte)index, index).ToArray())
            .ToArray();
        var data = BuildPcField(sections);

        var field = FieldPC.OpenDecompressed("md1stin", data);

        field.Name.Should().Be("md1stin");
        field.IsOpen.Should().BeTrue();
        field.IsPC().Should().BeTrue();
        field.Sections.Should().HaveCount(9);
        field.Sections[0].Section.Should().Be(FieldSection.Scripts);
        field.Sections[4].Section.Should().Be(FieldSection.Walkmesh);
        field.GetSectionData(FieldSection.Scripts).Should().Equal(sections[0]);
        field.GetSectionData(FieldSection.Background).Should().Equal(sections[8]);
    }

    [Fact]
    public void open_compressed_reads_header_prefixed_lzs_field()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => Encoding.ASCII.GetBytes(new string((char)('A' + index), index + 2)))
            .ToArray();
        var data = BuildPcField(sections);
        var compressed = LzsCompression.CompressWithHeader(data);

        var field = FieldPC.OpenCompressed("blackbga", compressed);

        field.GetSectionData(FieldSection.Camera).Should().Equal(sections[1]);
        field.GetSectionData(FieldSection.Inf).Should().Equal(sections[7]);
    }

    [Fact]
    public void save_decompressed_returns_original_field_bytes_when_unmodified()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => Encoding.ASCII.GetBytes(new string((char)('A' + index), index + 2)))
            .ToArray();
        var data = BuildPcField(sections);

        var field = FieldPC.OpenDecompressed("md1stin", data);

        field.SaveDecompressed().Should().Equal(data);
    }

    [Fact]
    public void save_compressed_returns_original_archive_bytes_when_unmodified()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => Encoding.ASCII.GetBytes(new string((char)('A' + index), index + 2)))
            .ToArray();
        var compressed = LzsCompression.CompressWithHeader(BuildPcField(sections));

        var field = FieldPC.OpenCompressed("md1stin", compressed);

        field.SaveCompressed().Should().Equal(compressed);
    }

    [Fact]
    public void save_decompressed_rebuilds_modified_section1_texts()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[0] = BuildSection1();
        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField(sections));

        field.SetText(1, new FF7String("Changed line"));
        var saved = FieldPC.OpenDecompressed("md1stin", field.SaveDecompressed());

        saved.ScriptsAndTexts.Should().NotBeNull();
        saved.ScriptsAndTexts!.Texts.Select(text => text.Value)
            .Should().Equal("Map name", "Changed line");
        saved.GetSectionData(FieldSection.Camera).Should().Equal(sections[1]);
    }

    [Fact]
    public void open_decompressed_parses_walkmesh_section_when_available()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[4] = IdFileTests.BuildWalkmesh();

        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField(sections));

        field.Walkmesh.Should().NotBeNull();
        field.Walkmesh!.TriangleCount.Should().Be(1);
        field.Walkmesh.Access[0].A.Should().Equal([-1, 2, 3]);
    }

    [Fact]
    public void save_decompressed_persists_edited_walkmesh_section()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[4] = IdFileTests.BuildWalkmesh();
        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField(sections));

        field.Walkmesh!.SetAccess(0, new Access(9, 8, -1));
        field.ApplyWalkmeshChanges();
        var reopened = FieldPC.OpenDecompressed("md1stin", field.SaveDecompressed());

        reopened.Walkmesh.Should().NotBeNull();
        reopened.Walkmesh!.Access[0].A.Should().Equal([9, 8, -1]);
    }

    [Fact]
    public void open_decompressed_parses_model_loader_section_when_available()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[2] = FieldModelLoaderPCTests.BuildModelLoader();

        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField(sections));

        field.ModelLoader.Should().NotBeNull();
        field.ModelLoader!.Models[0].HrcName.Should().Be("AAAA");
        field.ModelLoader.Models[0].Animations.Should().HaveCount(2);
    }

    [Fact]
    public void save_decompressed_persists_edited_model_loader_section()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[2] = FieldModelLoaderPCTests.BuildModelLoader();
        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField(sections));

        field.ModelLoader!.ReplaceModels(
        [
            field.ModelLoader.Models[0] with
            {
                CharacterName = "Tifa",
                Scale = 384,
                Animations =
                [
                    field.ModelLoader.Models[0].Animations[0] with { Name = "stand" },
                    field.ModelLoader.Models[0].Animations[1],
                ],
            },
        ]);
        field.ApplyModelLoaderChanges();
        var reopened = FieldPC.OpenDecompressed("md1stin", field.SaveDecompressed());

        reopened.ModelLoader.Should().NotBeNull();
        reopened.ModelLoader!.Models[0].CharacterName.Should().Be("Tifa");
        reopened.ModelLoader.Models[0].Scale.Should().Be(384);
        reopened.ModelLoader.Models[0].Animations[0].Name.Should().Be("stand");
    }

    [Fact]
    public void open_decompressed_parses_encounter_section_when_available()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[6] = EncounterFileTests.BuildEncounters();

        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField(sections));

        field.Encounters.Should().NotBeNull();
        field.Encounters!.Table1.Enabled.Should().BeTrue();
        field.Encounters.Table2.Rate.Should().Be(64);
    }

    [Fact]
    public void save_decompressed_persists_edited_encounter_section()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[6] = EncounterFileTests.BuildEncounters();
        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField(sections));

        field.Encounters!.SetTable(1, field.Encounters.Table2 with
        {
            Rate = 77,
            SpecialBattles = field.Encounters.Table2.SpecialBattles
                .Select((battle, index) => index == 0
                    ? battle with { BattleId = 0x155, Probability = 17 }
                    : battle)
                .ToArray(),
        });
        field.ApplyEncounterChanges();
        var reopened = FieldPC.OpenDecompressed("md1stin", field.SaveDecompressed());

        reopened.Encounters.Should().NotBeNull();
        reopened.Encounters!.Table2.Rate.Should().Be(77);
        reopened.Encounters.Table2.SpecialBattles[0].BattleId.Should().Be(0x155);
        reopened.Encounters.Table2.SpecialBattles[0].Probability.Should().Be(17);
    }

    [Fact]
    public void open_decompressed_parses_background_section_when_available()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[8] = BackgroundFilePCTests.BuildBackground();

        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField(sections));

        field.Background.Should().NotBeNull();
        field.Background!.TileCount.Should().Be(2);
        field.Background.ExistingTextureCount.Should().Be(1);
    }

    [Fact]
    public void save_decompressed_persists_replaced_background_section()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[8] = BackgroundFilePCTests.BuildBackground();
        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField(sections));
        var replacement = BackgroundFilePCTests.BuildBackgroundWithBrokenPaletteReference();

        field.ReplaceBackgroundSection(replacement);
        var reopened = FieldPC.OpenDecompressed("md1stin", field.SaveDecompressed());

        reopened.Background.Should().NotBeNull();
        reopened.Background!.RawData.ToArray().Should().Equal(replacement);
    }

    [Fact]
    public void open_decompressed_parses_inf_section_when_available()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[7] = InfFileTests.BuildInf();

        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField(sections));

        field.Inf.Should().NotBeNull();
        field.Inf!.MapName.Should().Be("md1stin");
        field.Inf.ExitLines[0].FieldId.Should().Be(0x1234);
    }

    [Fact]
    public void save_decompressed_persists_edited_inf_section()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[7] = InfFileTests.BuildInf();
        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField(sections));

        field.Inf!.SetGeneralMetadata("newmap", 0x44, 321, new InfRange(-1, -2, 3, 4));
        field.ApplyInfChanges();
        var reopened = FieldPC.OpenDecompressed("md1stin", field.SaveDecompressed());

        reopened.Inf.Should().NotBeNull();
        reopened.Inf!.MapName.Should().Be("newmap");
        reopened.Inf.Control.Should().Be(0x44);
        reopened.Inf.CameraFocusHeight.Should().Be(321);
        reopened.Inf.CameraRange.Should().Be(new InfRange(-1, -2, 3, 4));
    }

    [Fact]
    public void field_archive_open_field_decompresses_and_parses_section_table()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "flevel.lgp");
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)(0x20 + index) })
            .ToArray();

        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("md1stin", LzsCompression.CompressWithHeader(BuildPcField(sections))),
        ]);

        var archive = FieldArchive.Open(archivePath);
        var field = archive.OpenField("md1stin");

        field.Sections.Select(section => section.Size).Should().Equal(Enumerable.Repeat(1, 9));
        field.GetSectionData(FieldSection.ModelLoader).Should().Equal(sections[2]);
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

    private static byte[] BuildSection1()
    {
        const int groupCount = 0;
        var posText = 32;
        var texts = BuildTextSection(["Map name", "Hello"]);

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

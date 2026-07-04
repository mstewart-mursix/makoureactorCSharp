using System.Buffers.Binary;
using System.Text;

using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public class Section1FileTests
{
    [Fact]
    public void open_reads_header_groups_scripts_and_texts()
    {
        var data = BuildSection1();

        var section = Section1File.Open(data);

        section.Version.Should().Be(0x0502);
        section.Scale.Should().Be(512);
        section.Author.Should().Be("makou");
        section.MapName.Should().Be("md1stin");
        section.GrpScriptCount.Should().Be(2);
        section.GrpScripts[0].Name.Should().Be("dic");
        section.GrpScripts[1].Name.Should().Be("cloud");
        section.GrpScripts[0].Scripts[0].RawBytes.ToArray().Should().Equal([0x00]);
        section.GrpScripts[1].Scripts[0].RawBytes.ToArray().Should().Equal([0x01, 0x00]);
        section.Texts.Select(text => text.Value).Should().Equal("Map name", "Hello");
    }

    [Fact]
    public void open_rejects_invalid_text_offset()
    {
        var data = BuildSection1();
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4, 2), 4);

        var open = () => Section1File.Open(data);

        open.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void save_rebuilds_text_table_and_shifts_akao_offsets()
    {
        var data = BuildSection1WithAkao();
        var section = Section1File.Open(data);
        var oldAkaoOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(32, 4));

        section.SetText(1, new FF7String("Longer replacement line"));
        var saved = section.Save();
        var reopened = Section1File.Open(saved);

        reopened.Texts.Select(text => text.Value).Should().Equal("Map name", "Longer replacement line");
        var newAkaoOffset = BinaryPrimitives.ReadUInt32LittleEndian(saved.AsSpan(32, 4));
        newAkaoOffset.Should().BeGreaterThan(oldAkaoOffset);
        saved.AsSpan((int)newAkaoOffset, 4).ToArray().Should().Equal([0x41, 0x4B, 0x41, 0x4F]);
    }

    [Fact]
    public void save_preserves_unknown_opcode_script_bytes_when_text_changes()
    {
        var data = BuildSection1WithScript([0x0C, 0xAA, 0x00]);
        var section = Section1File.Open(data);

        section.GrpScripts[0].Scripts[0].RawOpcodes[0].Name.Should().Be("Unknown1");
        section.SetText(0, new FF7String("Changed"));

        var saved = section.Save();
        var reopened = Section1File.Open(saved);

        reopened.GrpScripts[0].Scripts[0].RawBytes.ToArray()
            .Should().Equal([0x0C, 0xAA, 0x00]);
        reopened.Texts.Select(text => text.Value).Should().Equal("Changed");
    }

    [Fact]
    public void japanese_toggle_redecodes_loaded_raw_text_without_marking_section_modified()
    {
        var data = BuildSection1WithRawText([0xA6, 0xA7, 0xFF]);
        var section = Section1File.Open(data);

        section.Texts[0].Value.Should().Be("{xA6}{xA7}");

        section.SetJapaneseText(true);

        section.JapaneseText.Should().BeTrue();
        section.Texts[0].Value.Should().Be("ｦｧ");
        section.IsModified.Should().BeFalse();

        section.SetJapaneseText(false);

        section.Texts[0].Value.Should().Be("{xA6}{xA7}");
        section.IsModified.Should().BeFalse();
    }

    [Fact]
    public void insert_and_delete_text_shift_script_text_references()
    {
        var data = BuildSection1WithScript(
        [
            0x40, 0x00, 0x01,
            0x43, 0x02,
            0x48, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
        ]);
        var section = Section1File.Open(data);

        section.InsertText(1, new FF7String("Inserted"));

        section.GrpScripts[0].Scripts[0].RawBytes.ToArray().Should().Equal(
        [
            0x40, 0x00, 0x02,
            0x43, 0x03,
            0x48, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00,
        ]);

        section.DeleteText(1);

        section.GrpScripts[0].Scripts[0].RawBytes.ToArray().Should().Equal(
        [
            0x40, 0x00, 0x01,
            0x43, 0x02,
            0x48, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
        ]);
    }

    [Fact]
    public void clean_unused_texts_blanks_only_unreferenced_entries()
    {
        var data = BuildSection1WithScriptAndTexts(
            [0x40, 0x00, 0x01],
            ["Unused zero", "Used one", "Unused two"]);
        var section = Section1File.Open(data);

        section.ListUsedTexts().Should().Equal(1);
        var changed = section.CleanUnusedTexts();

        changed.Should().Be(2);
        section.Texts.Select(static text => text.Value)
            .Should().Equal(string.Empty, "Used one", string.Empty);
        section.IsModified.Should().BeTrue();
    }

    [Fact]
    public void empty_texts_blanks_all_entries_without_removing_ids()
    {
        var section = Section1File.Open(BuildSection1());

        var changed = section.EmptyTexts();

        changed.Should().Be(2);
        section.Texts.Should().HaveCount(2);
        section.Texts.Select(static text => text.Value).Should().Equal(string.Empty, string.Empty);
        section.IsModified.Should().BeTrue();
    }

    [Fact]
    public void autosize_text_windows_updates_window_opcode_and_saves_script_bytes()
    {
        var data = BuildSection1WithScriptAndTexts(
        [
            0x50, 0x00, 0x2C, 0x01, 0xDC, 0x00, 0x0A, 0x00, 0x0A, 0x00,
            0x40, 0x00, 0x01,
        ],
        ["Map name", "Hello world"]);
        var section = Section1File.Open(data);

        var changed = section.AutosizeTextWindows();
        var saved = section.Save();
        var reopened = Section1File.Open(saved);

        changed.Should().Be(1);
        reopened.GrpScripts[0].Scripts[0].RawOpcodes[0].Name.Should().Be("WINDOW");
        var bytes = reopened.GrpScripts[0].Scripts[0].RawBytes.ToArray();
        BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(2, 2)).Should().BeLessThan(300);
        BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(4, 2)).Should().BeLessThan(220);
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2)).Should().BeGreaterThan(10);
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2)).Should().BeGreaterThan(10);
    }

    [Fact]
    public void script_replaces_fixed_size_raw_opcode_bytes()
    {
        var script = new Script([0x40, 0x00, 0x01, 0x00]);

        var changed = script.ReplaceRawOpcodeBytes(0, [0x40, 0x00, 0x02]);

        changed.Should().BeTrue();
        script.RawBytes.ToArray().Should().Equal([0x40, 0x00, 0x02, 0x00]);
        script.RawOpcodes[0].Arguments.Should().Be("window=0, text=2");
    }

    private static byte[] BuildSection1()
    {
        const int groupCount = 2;
        const int scriptCount = 32;
        var headerSize = 32;
        var scriptOffsetsOffset = headerSize + (groupCount * 8);
        var scriptDataOffset = scriptOffsetsOffset + (groupCount * scriptCount * 2);
        var script0 = new byte[] { 0x00 };
        var script1 = new byte[] { 0x01, 0x00 };
        var posText = scriptDataOffset + script0.Length + script1.Length;
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
        WriteFixed(writer, "dic", 8);
        WriteFixed(writer, "cloud", 8);

        WriteScriptOffsets(writer, scriptDataOffset, script0.Length, script1.Length, posText);
        writer.Write(script0);
        writer.Write(script1);
        writer.Write(texts);

        return stream.ToArray();
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

    private static byte[] BuildSection1WithRawText(byte[] rawText)
    {
        const int groupCount = 0;
        var headerSize = 32;
        var textOffset = headerSize;
        var texts = BuildRawTextSection([rawText]);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)0x0502);
        writer.Write((byte)groupCount);
        writer.Write((byte)1);
        writer.Write((ushort)textOffset);
        writer.Write((ushort)0);
        writer.Write((ushort)512);
        writer.Write(new byte[6]);
        WriteFixed(writer, "makou", 8);
        WriteFixed(writer, "md1stin", 8);
        writer.Write(texts);

        return stream.ToArray();
    }

    private static byte[] BuildSection1WithScript(byte[] script)
    {
        const int groupCount = 1;
        const int scriptCount = 32;
        var headerSize = 32;
        var scriptOffsetsOffset = headerSize + (groupCount * 8);
        var scriptDataOffset = scriptOffsetsOffset + (groupCount * scriptCount * 2);
        var posText = scriptDataOffset + script.Length;
        var texts = BuildTextSection(["Hello"]);

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
        writer.Write(texts);

        return stream.ToArray();
    }

    private static byte[] BuildSection1WithAkao()
    {
        const int groupCount = 0;
        var headerSize = 32;
        var akaoPositionsOffset = headerSize;
        var textOffset = akaoPositionsOffset + 4;
        var texts = BuildTextSection(["Map name", "Hello"]);
        var akaoOffset = textOffset + texts.Length;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)0x0502);
        writer.Write((byte)groupCount);
        writer.Write((byte)1);
        writer.Write((ushort)textOffset);
        writer.Write((ushort)1);
        writer.Write((ushort)512);
        writer.Write(new byte[6]);
        WriteFixed(writer, "makou", 8);
        WriteFixed(writer, "md1stin", 8);
        writer.Write((uint)akaoOffset);
        writer.Write(texts);
        writer.Write([0x41, 0x4B, 0x41, 0x4F]);

        return stream.ToArray();
    }

    private static void WriteScriptOffsets(BinaryWriter writer, int scriptDataOffset, int firstLength, int secondLength, int posText)
    {
        var first = checked((ushort)scriptDataOffset);
        var second = checked((ushort)(scriptDataOffset + firstLength));
        var text = checked((ushort)posText);

        writer.Write(first);
        for (var i = 1; i < 32; i++)
            writer.Write(second);

        writer.Write(second);
        for (var i = 1; i < 32; i++)
            writer.Write(text);
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

    private static byte[] BuildRawTextSection(IReadOnlyList<byte[]> texts)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)texts.Count);
        var offset = checked((ushort)(2 + (texts.Count * 2)));
        foreach (var text in texts)
        {
            writer.Write(offset);
            offset += checked((ushort)text.Length);
        }

        foreach (var text in texts)
            writer.Write(text);

        return stream.ToArray();
    }

    private static void WriteFixed(BinaryWriter writer, string value, int length)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        writer.Write(bytes);
        writer.Write(new byte[length - bytes.Length]);
    }
}

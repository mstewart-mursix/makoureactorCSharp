using System.Buffers.Binary;

using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Fields;

/// <summary>
/// Audit of <see cref="FieldPC"/> section-table handling against <c>Field::save</c>,
/// <c>Field::sectionData</c>, <c>Field::setSaved</c> and <c>FieldPC::saveHeader</c> in
/// <c>src/core/field/</c>.
/// </summary>
public class FieldPCAuditTests
{
    [Fact]
    public void set_saved_makes_saved_bytes_the_new_baseline()
    {
        var field = FieldPC.CreateEmpty("testmap");
        field.InsertText(field.ScriptsAndTexts!.TextCount, new FF7String("Hello"));
        var before = field.SaveDecompressed();

        field.SetSaved();

        field.IsModified.Should().BeFalse();
        field.SaveDecompressed().Should().Equal(before);
        LzsCompression.DecompressWithHeader(field.SaveCompressed()).Should().Equal(before);
        field.Data.ToArray().Should().Equal(before);
        field.ScriptsAndTexts!.TextCount.Should().Be(3);
        field.ScriptsAndTexts.Save().Should().Equal(field.GetSectionData(FieldSection.Scripts));
    }

    [Fact]
    public void set_saved_then_further_edit_keeps_previous_edits()
    {
        var field = FieldPC.CreateEmpty("testmap");
        field.InsertText(field.ScriptsAndTexts!.TextCount, new FF7String("First"));
        field.SetSaved();

        field.InsertText(field.ScriptsAndTexts!.TextCount, new FF7String("Second"));
        var reopened = FieldPC.OpenDecompressed("testmap", field.SaveDecompressed());

        reopened.ScriptsAndTexts!.TextCount.Should().Be(4);
        reopened.ScriptsAndTexts.Texts[2].Value.Should().Be("First");
        reopened.ScriptsAndTexts.Texts[3].Value.Should().Be("Second");
    }

    [Fact]
    public void set_saved_bakes_in_removed_tiles_section()
    {
        var data = BuildField(index => index == 5 ? [1, 2, 3, 4] : Section(index));
        var field = FieldPC.OpenDecompressed("tiles", data);
        field.RemoveUnusedTilesSection().Should().BeTrue();
        var saved = field.SaveDecompressed();

        field.SetSaved();

        field.GetSectionData(FieldSection.Tiles).Should().BeEmpty();
        field.SaveDecompressed().Should().Equal(saved);
        field.RemoveUnusedTilesSection().Should().BeTrue();
    }

    [Fact]
    public void modified_save_writes_cpp_header_padding_and_section_count()
    {
        var data = BuildField(Section);
        data[0] = 0xAB;
        data[1] = 0xCD;
        var field = FieldPC.OpenDecompressed("header", data);
        field.SetModified();

        var saved = field.SaveDecompressed();

        saved.AsSpan(0, 6).ToArray().Should().Equal(0x00, 0x00, 0x09, 0x00, 0x00, 0x00);
    }

    [Fact]
    public void section_payload_spans_to_next_section_offset_not_declared_size()
    {
        // Field::sectionSize = sectionPosition(id + 1) - 4 - sectionPosition(id); the stored size
        // prefix is ignored, so bytes after the declared size stay part of the section.
        var data = BuildField(index => index == 1 ? [0x11, 0x22, 0x33, 0x44] : Section(index),
            declaredSize: index => index == 1 ? 2 : null);

        var field = FieldPC.OpenDecompressed("ranges", data);

        field.GetSectionData(FieldSection.Camera).Should().Equal(0x11, 0x22, 0x33, 0x44);
        field.SetModified();
        var reopened = FieldPC.OpenDecompressed("ranges", field.SaveDecompressed());
        reopened.GetSectionData(FieldSection.Camera).Should().Equal(0x11, 0x22, 0x33, 0x44);
    }

    [Fact]
    public void declared_size_larger_than_section_range_is_ignored()
    {
        var data = BuildField(index => index == 1 ? [0x11, 0x22] : Section(index),
            declaredSize: index => index == 1 ? 1000 : null);

        var field = FieldPC.OpenDecompressed("ranges", data);

        field.GetSectionData(FieldSection.Camera).Should().Equal(0x11, 0x22);
    }

    [Fact]
    public void last_section_ends_at_footer()
    {
        var data = BuildField(index => index == 8 ? [0x01, 0x02, 0x03] : Section(index));

        var field = FieldPC.OpenDecompressed("footer", data);

        field.GetSectionData(FieldSection.Background).Should().Equal(0x01, 0x02, 0x03);
    }

    [Fact]
    public void empty_field_texts_have_a_single_terminator()
    {
        var field = FieldPC.CreateEmpty("testmap");
        var section = field.GetSectionData(FieldSection.Scripts);
        var posTexts = BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(4));
        var text = section.AsSpan(posTexts);

        var first = BinaryPrimitives.ReadUInt16LittleEndian(text[2..]);
        var second = BinaryPrimitives.ReadUInt16LittleEndian(text[4..]);
        var encodedLength = FF7TextCodec.Encode("Map name").Length; // includes the 0xFF

        (second - first).Should().Be(encodedLength);
        text[(second - 1)].Should().Be(0xFF);
        text[(second - 2)].Should().NotBe(0xFF);
    }

    private static byte[] Section(int index) => [(byte)index, 0xEE];

    private static byte[] BuildField(Func<int, byte[]> payload, Func<int, int?>? declaredSize = null)
    {
        var output = new List<byte> { 0x00, 0x00 };
        output.AddRange(BitConverter.GetBytes(9u));
        output.AddRange(new byte[36]);
        for (var i = 0; i < 9; i++)
        {
            var bytes = payload(i);
            BinaryPrimitives.WriteUInt32LittleEndian(
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(output).Slice(6 + (i * 4), 4),
                (uint)output.Count);
            output.AddRange(BitConverter.GetBytes((uint)(declaredSize?.Invoke(i) ?? bytes.Length)));
            output.AddRange(bytes);
        }

        output.AddRange("FINAL FANTASY7"u8.ToArray());
        return output.ToArray();
    }
}

using System.Buffers.Binary;
using System.Text;

using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public sealed class TutorialFileTests
{
    [Fact]
    public void open_reads_tutorial_and_music_entries_from_section1()
    {
        var tutorials = TutorialFile.Open(BuildSectionWithTutorials());

        tutorials.Count.Should().Be(2);
        tutorials.HasTutorials.Should().BeTrue();
        tutorials.HasMusic.Should().BeTrue();
        tutorials.Entries[0].Kind.Should().Be(TutorialEntryKind.Tutorial);
        tutorials.Entries[0].Description.Should().Contain("[UP]");
        tutorials.Entries[0].Description.Should().Contain("{FINISH}");
        tutorials.Entries[1].Kind.Should().Be(TutorialEntryKind.Music);
        tutorials.Entries[1].MusicId.Should().Be(77);
        tutorials.Entries[1].Description.Should().Contain("id=77");
    }

    [Fact]
    public void section1_open_populates_tutorials_and_sounds()
    {
        var section = Section1File.Open(BuildSectionWithTutorials());

        section.TutorialsAndSounds.Should().NotBeNull();
        section.TutorialsAndSounds!.Entries.Should().HaveCount(2);
    }

    [Fact]
    public void open_returns_empty_when_section_has_no_akao_table()
    {
        TutorialFile.Open(new byte[32]).Entries.Should().BeEmpty();
    }

    private static byte[] BuildSectionWithTutorials()
    {
        var tutorial = new byte[] { 0x02, 0x11 };
        var akao = new byte[24];
        Encoding.ASCII.GetBytes("AKAO").CopyTo(akao, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(akao.AsSpan(4, 2), 77);
        BinaryPrimitives.WriteUInt16LittleEndian(akao.AsSpan(6, 2), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(akao.AsSpan(20, 2), 4);

        const int textOffset = 40;
        const int textSize = 6;
        var firstAkaoOffset = textOffset + textSize;
        var secondAkaoOffset = firstAkaoOffset + tutorial.Length;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)0x0502);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)textOffset);
        writer.Write((ushort)2);
        writer.Write((ushort)512);
        writer.Write(new byte[6]);
        WriteFixed(writer, "makou", 8);
        WriteFixed(writer, "md1stin", 8);
        writer.Write((uint)firstAkaoOffset);
        writer.Write((uint)secondAkaoOffset);
        writer.Write(new byte[textSize]);
        writer.Write(tutorial);
        writer.Write(akao);

        stream.Position.Should().Be(checked(secondAkaoOffset + akao.Length));
        return stream.ToArray();
    }

    private static void WriteFixed(BinaryWriter writer, string value, int length)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        writer.Write(bytes);
        writer.Write(new byte[length - bytes.Length]);
    }
}

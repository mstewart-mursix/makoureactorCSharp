using System.Buffers.Binary;
using System.IO;
using System.Text;

using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.ViewModels;

public sealed class TutorialsViewModelTests
{
    [Fact]
    public void view_model_exposes_entry_details()
    {
        var viewModel = new TutorialsViewModel("md1stin", TutorialFile.Open(BuildSectionWithTutorials()));

        viewModel.FieldName.Should().Be("md1stin");
        viewModel.Entries.Should().HaveCount(2);
        viewModel.SelectedEntry.Should().Be(viewModel.Entries[0]);
        viewModel.KindText.Should().Be("Type: Tutorial");
        viewModel.SizeText.Should().Be("Size: 2 bytes");
        viewModel.BrokenText.Should().Be("Broken: no");
        viewModel.DescriptionText.Should().Contain("[UP]");
        viewModel.StatusText.Should().Be("2 of 2 entries shown. 1 music, 1 tutorial.");
    }

    [Fact]
    public void filters_music_and_tutorial_entries()
    {
        var viewModel = new TutorialsViewModel("md1stin", TutorialFile.Open(BuildSectionWithTutorials()));

        viewModel.ShowTutorials = false;

        viewModel.Entries.Should().ContainSingle();
        viewModel.Entries[0].Kind.Should().Be(TutorialEntryKind.Music);
        viewModel.MusicIdText.Should().Be("Music ID: 77");
        viewModel.StatusText.Should().Be("1 of 2 entries shown. 1 music, 1 tutorial.");

        viewModel.ShowMusic = false;
        viewModel.Entries.Should().BeEmpty();
        viewModel.HasSelectedEntry.Should().BeFalse();
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

        return stream.ToArray();
    }

    private static void WriteFixed(BinaryWriter writer, string value, int length)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        writer.Write(bytes);
        writer.Write(new byte[length - bytes.Length]);
    }
}

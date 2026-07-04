using System.IO;

using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.UI.WPF.Archive;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Archive;

public sealed class ArchiveEntryPreviewBuilderTests
{
    [Fact]
    public void builds_text_preview_for_printable_archive_entry()
    {
        var entry = Entry("readme.txt", 12);

        var preview = ArchiveEntryPreviewBuilder.Build(entry, "hello world!"u8.ToArray());

        preview.Should().Contain("readme.txt");
        preview.Should().Contain("12 bytes");
        preview.Should().Contain("hello world!");
    }

    [Fact]
    public void builds_hex_preview_for_binary_archive_entry()
    {
        var entry = Entry("kernel.bin", 4);

        var preview = ArchiveEntryPreviewBuilder.Build(entry, [0, 1, 2, 255]);

        preview.Should().Contain("kernel.bin");
        preview.Should().Contain("0000: 00 01 02 FF");
    }

    [Fact]
    public void truncates_large_previews()
    {
        var entry = Entry("large.txt", 600);
        var data = Enumerable.Repeat((byte)'A', 600).ToArray();

        var preview = ArchiveEntryPreviewBuilder.Build(entry, data);

        preview.Should().EndWith("...");
        preview.Should().Contain(new string('A', 128));
    }

    [Fact]
    public void reports_preview_reader_failures()
    {
        var entry = Entry("bad.bin", 4);

        var preview = ArchiveEntryPreviewBuilder.Build(
            entry,
            _ => throw new InvalidOperationException("read failed"));

        preview.Should().Be("bad.bin\nPreview failed: read failed");
    }

    [Fact]
    public void detects_text_and_binary_samples()
    {
        ArchiveEntryPreviewBuilder.LooksLikeText("plain text"u8).Should().BeTrue();
        ArchiveEntryPreviewBuilder.LooksLikeText([0, 1, 2, 3, 4, 5]).Should().BeFalse();
    }

    private static LgpArchiveEntry Entry(string path, int size) =>
        new(Path.GetFileName(path), path, string.Empty, size, 0);
}

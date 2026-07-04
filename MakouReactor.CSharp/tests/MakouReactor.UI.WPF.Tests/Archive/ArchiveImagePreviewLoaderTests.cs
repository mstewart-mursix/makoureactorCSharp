using System.IO;
using System.Threading;

using FluentAssertions;

using MakouReactor.UI.WPF.Archive;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Archive;

public sealed class ArchiveImagePreviewLoaderTests
{
    [Fact]
    public void looks_like_bitmap_accepts_image_extensions_and_magic_bytes()
    {
        ArchiveImagePreviewLoader.LooksLikeBitmap("preview.png", [0x00])
            .Should().BeTrue();
        ArchiveImagePreviewLoader.LooksLikeBitmap("preview.bin", [0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0])
            .Should().BeTrue();
        ArchiveImagePreviewLoader.LooksLikeBitmap("preview.bin", [0xFF, 0xD8, 0, 0, 0, 0, 0, 0])
            .Should().BeTrue();
        ArchiveImagePreviewLoader.LooksLikeBitmap("preview.dat", [0x01, 0x02])
            .Should().BeFalse();
    }

    [Fact]
    public void try_load_decodes_valid_bitmap_and_freezes_image()
    {
        RunSta(() =>
        {
            var loaded = ArchiveImagePreviewLoader.TryLoad("preview.bmp", BuildBmp(), out var image);

            loaded.Should().BeTrue();
            image.Should().NotBeNull();
            image!.PixelWidth.Should().Be(1);
            image.PixelHeight.Should().Be(1);
            image.IsFrozen.Should().BeTrue();
        });
    }

    [Fact]
    public void try_load_returns_false_for_invalid_image_data()
    {
        RunSta(() =>
        {
            var loaded = ArchiveImagePreviewLoader.TryLoad("preview.png", [0x89, 0x50, 0x4E, 0x47], out var image);

            loaded.Should().BeFalse();
            image.Should().BeNull();
        });
    }

    private static byte[] BuildBmp()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(58);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(1);
        writer.Write(1);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write(0);
        writer.Write(4);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0xFF);
        writer.Write((byte)0x00);

        return stream.ToArray();
    }

    private static void RunSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }
}

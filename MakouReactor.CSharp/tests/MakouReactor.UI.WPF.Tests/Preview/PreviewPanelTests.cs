using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Media;

using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.Preview;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Preview;

public sealed class PreviewPanelTests
{
    [Fact]
    public void preview_panel_wraps_background_preview_and_accepts_empty_states()
    {
        RunSta(() =>
        {
            var panel = new PreviewPanel();

            panel.Should().NotBeNull();
            panel.ShowField(null, "Background");
            panel.ShowField(null, "Model");
            panel.ShowLoadFailed("md1stin");
        });
    }

    [Fact]
    public void background_preview_renders_decoded_background_tile_layout()
    {
        RunSta(() =>
        {
            var preview = new BackgroundPreviewControl();
            var field = FieldPC.OpenDecompressed("md1stin", BuildPcFieldWithBackground());

            preview.ShowField(field, "Background");

            preview.IsTilePreviewVisible.Should().BeTrue();
            preview.RenderedTileCount.Should().Be(2);
        });
    }

    [Fact]
    public void background_preview_clears_tile_layout_for_empty_state()
    {
        RunSta(() =>
        {
            var preview = new BackgroundPreviewControl();
            var field = FieldPC.OpenDecompressed("md1stin", BuildPcFieldWithBackground());
            preview.ShowField(field, "Background");

            preview.ShowField(null, "Background");

            preview.IsTilePreviewVisible.Should().BeFalse();
            preview.RenderedTileCount.Should().Be(0);
        });
    }

    [Fact]
    public void background_preview_uses_dark_empty_panel_matching_original_shell()
    {
        RunSta(() =>
        {
            var preview = new BackgroundPreviewControl();

            preview.PreviewBackground.Should().BeOfType<SolidColorBrush>()
                .Which.Color.Should().Be(Color.FromRgb(0x30, 0x30, 0x30));
            preview.IsTilePreviewVisible.Should().BeFalse();
        });
    }

    [Fact]
    public void model_preview_renders_selected_model_loader_metadata()
    {
        RunSta(() =>
        {
            var preview = new ModelPreviewControl();
            var model = new FieldModelInfoPC(
                0,
                "Cloud",
                "AAAA",
                7,
                512,
                new RgbColor(0x10, 0x20, 0x30),
                [
                    new FieldModelAnimationInfoPC(0, "idle", 1),
                    new FieldModelAnimationInfoPC(1, "walk", 2),
                ]);

            preview.ShowModel("md1stin", model);

            preview.IsModelVisible.Should().BeTrue();
            preview.CurrentModelName.Should().Be("Cloud");
            preview.CurrentAnimationCount.Should().Be(2);
        });
    }

    [Fact]
    public void preview_panel_model_mode_shows_first_loaded_field_model()
    {
        RunSta(() =>
        {
            var panel = new PreviewPanel();
            var field = FieldPC.OpenDecompressed("md1stin", BuildPcFieldWithModelLoader());

            panel.ShowField(field, "Model");

            panel.IsModelPreviewVisible.Should().BeTrue();
            panel.CurrentModelPreviewName.Should().Be("Cloud");
        });
    }

    private static byte[] BuildPcFieldWithBackground()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[8] = BuildBackground();

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

    private static byte[] BuildPcFieldWithModelLoader()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[2] = BuildModelLoader();

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

    private static byte[] BuildModelLoader()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)0);

        WriteModel(writer, "Cloud", 7, "AAAA", "512", [("idle", (ushort)1), ("walk", (ushort)2)]);

        return stream.ToArray();
    }

    private static void WriteModel(
        BinaryWriter writer,
        string characterName,
        ushort unknown,
        string hrcName,
        string scale,
        IReadOnlyList<(string Name, ushort Unknown)> animations)
    {
        var nameBytes = Encoding.Latin1.GetBytes(characterName);
        writer.Write((ushort)nameBytes.Length);
        writer.Write(nameBytes);
        writer.Write(unknown);
        WriteFixed(writer, hrcName, 8);
        WriteFixed(writer, scale, 4);
        writer.Write((ushort)animations.Count);

        for (var light = 0; light < 3; light++)
        {
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)0);
            writer.Write((short)0);
            writer.Write((short)0);
        }

        writer.Write((byte)0x10);
        writer.Write((byte)0x20);
        writer.Write((byte)0x30);

        foreach (var animation in animations)
        {
            var animationBytes = Encoding.Latin1.GetBytes(animation.Name);
            writer.Write((ushort)animationBytes.Length);
            writer.Write(animationBytes);
            writer.Write(animation.Unknown);
        }
    }

    private static void WriteFixed(BinaryWriter writer, string value, int length)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        writer.Write(bytes.AsSpan(0, Math.Min(bytes.Length, length)));
        if (bytes.Length < length)
            writer.Write(new byte[length - bytes.Length]);
    }

    private static byte[] BuildBackground()
    {
        var textureMarkerOffset = 185;
        var textureTableOffset = textureMarkerOffset + "TEXTURE".Length;
        var data = new byte[textureTableOffset + 2 + 4 + 65_536 + (41 * 2)];

        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(44, 2), 1);
        WriteTile(data, 52, dstX: 10, dstY: 20, srcX: 3, srcY: 4, textureId: 5, depth: 1);

        data[104] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(109, 2), 1);
        WriteTile(data, 131, dstX: -30, dstY: 40, srcX: 6, srcY: 7, textureId: 6, textureId2: 7, depth: 1);

        Encoding.ASCII.GetBytes("TEXTURE").CopyTo(data, textureMarkerOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(textureTableOffset, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(textureTableOffset + 2, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(textureTableOffset + 4, 2), 1);

        return data;
    }

    private static void WriteTile(
        byte[] data,
        int offset,
        short dstX,
        short dstY,
        byte srcX,
        byte srcY,
        byte textureId = 0,
        byte textureId2 = 0,
        byte depth = 0)
    {
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset, 2), dstX);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset + 2, 2), dstY);
        data[offset + 8] = srcX;
        data[offset + 10] = srcY;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 16, 2), 16);
        data[offset + 30] = textureId;
        data[offset + 32] = textureId2;
        data[offset + 34] = depth;
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

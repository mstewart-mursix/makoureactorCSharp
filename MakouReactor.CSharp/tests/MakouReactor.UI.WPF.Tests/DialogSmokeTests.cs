using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.Background;
using MakouReactor.UI.WPF.Preview;
using MakouReactor.UI.WPF.Texts;
using MakouReactor.UI.WPF.Walkmesh;

using Xunit;

namespace MakouReactor.UI.WPF.Tests;

public sealed class DialogSmokeTests
{
    [Fact]
    public void text_manager_opens_with_text_entries()
    {
        RunSta(() =>
        {
            var section = Section1File.Open(BuildSection1());
            var dialog = new TextManagerDialog("md1stin", section)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10_000,
                Top = -10_000,
                ShowActivated = false,
            };

            try
            {
                dialog.Show();
                dialog.UpdateLayout();

                Find<TextBlock>(dialog, "FieldLabel").Text.Should().Be("md1stin");
                Find<ListView>(dialog, "TextListView").Items.Count.Should().Be(2);
                Find<TextBox>(dialog, "TextEditor").Text.Should().Be("Map name");
                Find<TextBlock>(dialog, "StatusLabel").Text.Should().Contain("Selected text 0");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void walkmesh_manager_opens_with_triangles()
    {
        RunSta(() =>
        {
            var field = FieldPC.OpenDecompressed("md1stin", BuildFieldWithWalkmesh());
            var dialog = new WalkmeshManagerDialog("md1stin", field)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10_000,
                Top = -10_000,
                ShowActivated = false,
            };

            try
            {
                dialog.Show();
                dialog.UpdateLayout();

                Find<TextBlock>(dialog, "FieldLabel").Text.Should().Be("md1stin");
                Find<ListView>(dialog, "TriangleListView").Items.Count.Should().Be(1);
                Find<ItemsControl>(dialog, "WalkmeshPreviewItems").Items.Count.Should().Be(1);
                Find<TextBlock>(dialog, "StatusLabel").Text.Should().Be("1 triangle loaded.");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void background_manager_opens_with_layers_tiles_and_textures()
    {
        RunSta(() =>
        {
            var background = BackgroundFilePC.Open(BuildBackground());
            var dialog = new BackgroundDialog("md1stin", background)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10_000,
                Top = -10_000,
                ShowActivated = false,
            };

            try
            {
                dialog.Show();
                dialog.UpdateLayout();

                Find<TextBlock>(dialog, "FieldLabel").Text.Should().Be("md1stin");
                Find<TextBlock>(dialog, "TileCountLabel").Text.Should().Be("Tiles: 2");
                Find<ListView>(dialog, "LayerListView").Items.Count.Should().BeGreaterThan(0);
                Find<ListView>(dialog, "TileListView").Items.Count.Should().BeGreaterThan(0);
                Find<ListView>(dialog, "TextureListView").Items.Count.Should().Be(1);
                Find<BackgroundPreviewControl>(dialog, "BackgroundPreview").RenderedTileCount.Should().Be(2);
                Find<Button>(dialog, "ExportBackgroundButton").IsEnabled.Should().BeTrue();
                Find<Button>(dialog, "ImportBackgroundButton").IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void background_manager_enables_import_when_import_callback_is_available()
    {
        RunSta(() =>
        {
            var background = BackgroundFilePC.Open(BuildBackground());
            var dialog = new BackgroundDialog("md1stin", background, _ => { })
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10_000,
                Top = -10_000,
                ShowActivated = false,
            };

            try
            {
                dialog.Show();
                dialog.UpdateLayout();

                Find<Button>(dialog, "ImportBackgroundButton").IsEnabled.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static T Find<T>(FrameworkElement root, string name)
        where T : class
    {
        root.FindName(name).Should().BeAssignableTo<T>();
        return (T)root.FindName(name);
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

    private static byte[] BuildSection1()
    {
        const int groupCount = 1;
        const int scriptCount = 32;
        var headerSize = 32;
        var scriptOffsetsOffset = headerSize + (groupCount * 8);
        var scriptDataOffset = scriptOffsetsOffset + (groupCount * scriptCount * 2);
        var script = new byte[] { 0x00 };
        var posText = scriptDataOffset + script.Length;
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
        WriteFixed(writer, "cloud", 8);

        var first = checked((ushort)scriptDataOffset);
        var text = checked((ushort)posText);
        writer.Write(first);
        for (var index = 1; index < scriptCount; index++)
            writer.Write(text);

        writer.Write(script);
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

    private static byte[] BuildWalkmesh()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(1u);
        WriteVertex(writer, -10, 0, 5, 5);
        WriteVertex(writer, 0, 20, 5, 5);
        WriteVertex(writer, 10, 0, 30, 30);
        writer.Write((short)-1);
        writer.Write((short)2);
        writer.Write((short)3);

        return stream.ToArray();
    }

    private static byte[] BuildFieldWithWalkmesh()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[4] = BuildWalkmesh();

        var headerSize = 42;
        var footer = "FINAL FANTASY7"u8.ToArray();
        var size = headerSize + sections.Sum(static section => 4 + section.Length) + footer.Length;
        var data = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(2, 4), 9);
        var cursor = headerSize;
        for (var index = 0; index < sections.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(6 + (index * 4), 4), (uint)cursor);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(cursor, 4), (uint)sections[index].Length);
            sections[index].CopyTo(data.AsSpan(cursor + 4));
            cursor += 4 + sections[index].Length;
        }

        footer.CopyTo(data.AsSpan(cursor));
        return data;
    }

    private static void WriteVertex(BinaryWriter writer, short x, short y, short z, short res)
    {
        writer.Write(x);
        writer.Write(y);
        writer.Write(z);
        writer.Write(res);
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
        WriteTile(
            data,
            131,
            dstX: -30,
            dstY: 40,
            srcX: 6,
            srcY: 7,
            srcX2: 8,
            srcY2: 9,
            paletteId: 1,
            id: 0x1234,
            param: 2,
            state: 3,
            blending: true,
            typeTrans: 1,
            textureId: 6,
            textureId2: 7,
            depth: 1,
            idBig: 0xAABBCCDD);

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
        byte srcX2 = 0,
        byte srcY2 = 0,
        byte paletteId = 0,
        ushort id = 0,
        byte param = 0,
        byte state = 0,
        bool blending = false,
        byte typeTrans = 0,
        byte textureId = 0,
        byte textureId2 = 0,
        byte depth = 0,
        uint idBig = 0)
    {
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset, 2), dstX);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset + 2, 2), dstY);
        data[offset + 8] = srcX;
        data[offset + 10] = srcY;
        data[offset + 12] = srcX2;
        data[offset + 14] = srcY2;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 16, 2), 16);
        data[offset + 18] = paletteId;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 20, 2), id);
        data[offset + 22] = param;
        data[offset + 23] = state;
        data[offset + 24] = blending ? (byte)1 : (byte)0;
        data[offset + 26] = typeTrans;
        data[offset + 30] = textureId;
        data[offset + 32] = textureId2;
        data[offset + 34] = depth;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 36, 4), idBig);
    }
}

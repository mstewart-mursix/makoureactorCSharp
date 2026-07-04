using System.Buffers.Binary;
using System.Text;

using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public sealed class BackgroundFilePCTests
{
    [Fact]
    public void open_reads_layer_tiles_and_texture_metadata()
    {
        var background = BackgroundFilePC.Open(BuildBackground());

        background.Size.Should().BeGreaterThan(65_000);
        background.TileCount.Should().Be(2);
        background.Layers.Should().HaveCount(4);
        background.Layers[0].DeclaredTileCount.Should().Be(1);
        background.Layers[1].Exists.Should().BeTrue();
        background.Layers[1].DeclaredTileCount.Should().Be(1);
        background.Layers[2].Exists.Should().BeFalse();
        background.ExistingTextureCount.Should().Be(1);

        var layer1Tile = background.Layers[0].Tiles[0];
        layer1Tile.LayerId.Should().Be(0);
        layer1Tile.Id.Should().Be(4095);
        layer1Tile.Destination.Should().Be("10, 20");
        layer1Tile.Source.Should().Be("3, 4");
        layer1Tile.Size.Should().Be(16);

        var layer2Tile = background.Layers[1].Tiles[0];
        layer2Tile.LayerId.Should().Be(1);
        layer2Tile.Id.Should().Be(0x1234);
        layer2Tile.Blending.Should().BeTrue();
        layer2Tile.Source.Should().Be("8, 9");
        layer2Tile.TextureId.Should().Be(7);
        layer2Tile.Param.Should().Be(2);
        layer2Tile.State.Should().Be(3);

        var texture = background.Textures[0];
        texture.Exists.Should().BeTrue();
        texture.Depth.Should().Be(1);
        texture.DataSize.Should().Be(65_536);
        texture.TileSize.Should().Be("16x16");
    }

    [Fact]
    public void open_rejects_missing_texture_marker()
    {
        var data = BuildBackground();
        Encoding.ASCII.GetBytes("BROKEN").CopyTo(data, 185);

        var open = () => BackgroundFilePC.Open(data);

        open.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void resize_to_minimum_width_adds_layer_one_tiles_before_texture_table()
    {
        var background = BackgroundFilePC.Open(BuildBackground());

        var result = background.ResizeToMinimumWidth(448);

        result.Changed.Should().BeTrue();
        result.TilesAdded.Should().BeGreaterThan(0);

        var resized = BackgroundFilePC.Open(result.Data);
        resized.Layers[0].DeclaredTileCount.Should().Be(1 + result.TilesAdded);
        resized.Layers[1].Exists.Should().BeTrue();
        resized.ExistingTextureCount.Should().Be(1);

        var sourceTiles = resized.Layers
            .Where(static layer => layer.LayerId is 0 or 1)
            .SelectMany(static layer => layer.Tiles)
            .ToArray();
        var left = sourceTiles.Min(static tile => tile.DestinationX);
        var right = sourceTiles.Max(static tile => tile.DestinationX + tile.Size);
        (right - left).Should().BeGreaterThanOrEqualTo(440);
    }

    [Fact]
    public void repair_invalid_palette_references_assigns_unused_palette_to_broken_blended_tiles()
    {
        var background = BackgroundFilePC.Open(BuildBackgroundWithBrokenPaletteReference());

        var result = background.RepairInvalidPaletteReferences(paletteCount: 2);

        result.Changed.Should().BeTrue();
        result.TilesRepaired.Should().Be(1);

        var repaired = BackgroundFilePC.Open(result.Data);
        var tile = repaired.Layers[1].Tiles[0];
        tile.PaletteId.Should().Be(1);
        tile.TypeTrans.Should().Be(2);
    }

    internal static byte[] BuildBackground()
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

    internal static byte[] BuildBackgroundWithBrokenPaletteReference()
    {
        var data = BuildBackground();
        data[52 + 18] = 0;
        data[131 + 18] = 7;
        data[131 + 26] = 1;
        data[131 + 34] = 1;
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

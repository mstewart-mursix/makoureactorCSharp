using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MakouReactor.Core.Models;

/// <summary>
/// Read-only metadata for a PC field background section.
/// </summary>
public sealed class BackgroundFilePC
{
    private const int FirstLayerCountOffset = 44;
    private const int FirstLayerTileOffset = 52;
    private const int TileRecordSize = 52;
    private const int TilePcSize = 40;
    private const int MaxTileDestination = 1024;
    private const int TextureCount = 42;

    private readonly byte[] _data;

    private BackgroundFilePC(
        byte[] data,
        IReadOnlyList<BackgroundLayerPC> layers,
        IReadOnlyList<BackgroundTexturePC> textures,
        int textureTableOffset)
    {
        _data = data;
        Layers = layers;
        Textures = textures;
        TextureTableOffset = textureTableOffset;
    }

    public int Size => _data.Length;
    public int TextureTableOffset { get; }
    public IReadOnlyList<BackgroundLayerPC> Layers { get; }
    public IReadOnlyList<BackgroundTexturePC> Textures { get; }
    public int TileCount => Layers.Sum(static layer => layer.Tiles.Count);
    public int ExistingTextureCount => Textures.Count(static texture => texture.Exists);
    public ReadOnlyMemory<byte> RawData => _data;

    public BackgroundResizeResult ResizeToMinimumWidth(int minimumWidth)
    {
        if (minimumWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumWidth));

        var sourceTiles = Layers
            .Where(static layer => layer.LayerId is 0 or 1)
            .SelectMany(static layer => layer.Tiles)
            .ToArray();
        if (sourceTiles.Length == 0)
            return new BackgroundResizeResult(false, 0, _data.ToArray());

        var left = sourceTiles.Min(static tile => tile.DestinationX);
        var top = sourceTiles.Min(static tile => tile.DestinationY);
        var right = sourceTiles.Max(static tile => tile.DestinationX + tile.Size);
        var bottom = sourceTiles.Max(static tile => tile.DestinationY + tile.Size);
        var width = right - left;
        if (width >= minimumWidth)
            return new BackgroundResizeResult(false, 0, _data.ToArray());

        var layer0 = Layers.First(static layer => layer.LayerId == 0);
        if (layer0.Tiles.Count == 0)
            return new BackgroundResizeResult(false, 0, _data.ToArray());

        var template = layer0.Tiles.FirstOrDefault(static tile => tile.Depth == 2) ?? layer0.Tiles[0];
        var tileSize = Math.Max(1, (int)template.Size);
        var sideWidth = ((minimumWidth - width) / tileSize / 2) * tileSize;
        if (sideWidth <= 0)
            return new BackgroundResizeResult(false, 0, _data.ToArray());

        var newTiles = new List<byte[]>();
        AddTileRectangle(newTiles, template, -left - sideWidth, -top, sideWidth, bottom - top, tileSize);
        AddTileRectangle(newTiles, template, -left + width, -top, sideWidth, bottom - top, tileSize);
        if (newTiles.Count == 0)
            return new BackgroundResizeResult(false, 0, _data.ToArray());

        var output = new byte[checked(_data.Length + (newTiles.Count * TileRecordSize))];
        _data.AsSpan(0, layer0.EndOffset).CopyTo(output);
        var cursor = layer0.EndOffset;
        foreach (var tile in newTiles)
        {
            tile.CopyTo(output.AsSpan(cursor, TileRecordSize));
            cursor += TileRecordSize;
        }

        _data.AsSpan(layer0.EndOffset).CopyTo(output.AsSpan(cursor));
        BinaryPrimitives.WriteUInt16LittleEndian(
            output.AsSpan(layer0.HeaderOffset, 2),
            checked((ushort)(layer0.DeclaredTileCount + newTiles.Count)));

        return new BackgroundResizeResult(true, newTiles.Count, output);
    }

    public BackgroundRepairResult RepairInvalidPaletteReferences(int paletteCount)
    {
        if (paletteCount <= 0)
            return new BackgroundRepairResult(false, 0, _data.ToArray());

        var usedPalettes = Layers
            .SelectMany(static layer => layer.Tiles)
            .Where(tile => tile.PaletteId < paletteCount)
            .Select(static tile => tile.PaletteId)
            .ToHashSet();
        var unusedPalettes = Enumerable.Range(0, Math.Min(paletteCount, byte.MaxValue + 1))
            .Select(static value => (byte)value)
            .Where(palette => !usedPalettes.Contains(palette))
            .ToList();
        if (unusedPalettes.Count == 0)
            return new BackgroundRepairResult(false, 0, _data.ToArray());

        var output = _data.ToArray();
        var textureToPalette = new Dictionary<byte, byte>();
        var tilesRepaired = 0;
        foreach (var tile in Layers.SelectMany(static layer => layer.Tiles))
        {
            if (tile.Depth >= 2 ||
                !tile.Blending ||
                tile.TypeTrans == 2 ||
                tile.PaletteId < paletteCount)
            {
                continue;
            }

            if (!textureToPalette.TryGetValue(tile.TextureId, out var palette))
            {
                if (unusedPalettes.Count == 0)
                    continue;

                palette = unusedPalettes[0];
                unusedPalettes.RemoveAt(0);
                textureToPalette[tile.TextureId] = palette;
            }

            output[tile.Offset + 18] = palette;
            output[tile.Offset + 26] = 2;
            tilesRepaired++;
        }

        return tilesRepaired == 0
            ? new BackgroundRepairResult(false, 0, _data.ToArray())
            : new BackgroundRepairResult(true, tilesRepaired, output);
    }

    public static BackgroundFilePC Open(ReadOnlySpan<byte> data)
    {
        if (data.Length < FirstLayerTileOffset)
            throw new InvalidDataException("Background section is too short.");

        var source = data.ToArray();
        var layers = new List<BackgroundLayerPC>(4);

        var layer1 = ReadRequiredLayer(source, 0, FirstLayerCountOffset, FirstLayerTileOffset);
        layers.Add(layer1);

        var cursor = FirstLayerTileOffset + (layer1.DeclaredTileCount * TileRecordSize);
        layers.Add(ReadOptionalLayer(source, 1, cursor, 27));

        cursor = layers[^1].EndOffset;
        layers.Add(ReadOptionalLayer(source, 2, cursor, 21));

        cursor = layers[^1].EndOffset;
        layers.Add(ReadOptionalLayer(source, 3, cursor, 21));

        cursor = layers[^1].EndOffset;
        var textureTableOffset = FindTextureTableOffset(source, cursor);
        var textures = ReadTextures(source, textureTableOffset);

        return new BackgroundFilePC(source, layers, textures, textureTableOffset);
    }

    private static BackgroundLayerPC ReadRequiredLayer(byte[] data, int layerId, int countOffset, int tileOffset)
    {
        var count = ReadUInt16(data, countOffset, "background layer 1 tile count");
        var tiles = ReadTiles(data, layerId, count, tileOffset);
        return new BackgroundLayerPC(layerId, true, count, tiles, countOffset, tileOffset, tileOffset + (count * TileRecordSize));
    }

    private static BackgroundLayerPC ReadOptionalLayer(byte[] data, int layerId, int existsOffset, int tileOffsetDelta)
    {
        if (existsOffset >= data.Length)
            throw new InvalidDataException($"Background layer {layerId + 1} exists flag is out of range.");

        var exists = data[existsOffset] != 0;
        if (!exists)
            return new BackgroundLayerPC(layerId, false, 0, Array.Empty<BackgroundTilePC>(), existsOffset, existsOffset, existsOffset + 1);

        var countOffset = checked(existsOffset + 5);
        var count = ReadUInt16(data, countOffset, $"background layer {layerId + 1} tile count");
        var tileOffset = checked(existsOffset + tileOffsetDelta);
        var tiles = ReadTiles(data, layerId, count, tileOffset);
        return new BackgroundLayerPC(layerId, true, count, tiles, existsOffset, tileOffset, tileOffset + (count * TileRecordSize));
    }

    private static IReadOnlyList<BackgroundTilePC> ReadTiles(byte[] data, int layerId, ushort count, int tileOffset)
    {
        var result = new List<BackgroundTilePC>(count);
        for (var i = 0; i < count; i++)
        {
            var offset = checked(tileOffset + (i * TileRecordSize));
            if (offset + TilePcSize > data.Length)
                throw new InvalidDataException($"Background layer {layerId + 1} tile {i} is out of range.");

            var tile = ReadTile(data.AsSpan(offset, TilePcSize), layerId, i, offset);
            if (Math.Abs(tile.DestinationX) < MaxTileDestination &&
                Math.Abs(tile.DestinationY) < MaxTileDestination)
            {
                result.Add(tile);
            }
        }

        return result;
    }

    private static BackgroundTilePC ReadTile(ReadOnlySpan<byte> data, int layerId, int tileId, int offset)
    {
        var blending = data[24] != 0;
        var srcX = layerId > 0 && blending ? data[12] : data[8];
        var srcY = layerId > 0 && blending ? data[14] : data[10];
        var textureId = layerId > 0 && blending ? data[32] : data[30];
        var id = layerId switch
        {
            0 => 4095,
            2 => 4096,
            3 => 0,
            _ => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(20, 2)),
        };

        return new BackgroundTilePC(
            layerId,
            tileId,
            offset,
            BinaryPrimitives.ReadInt16LittleEndian(data.Slice(0, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(data.Slice(2, 2)),
            srcX,
            srcY,
            data[18],
            id,
            layerId > 0 ? data[22] : (byte)0,
            layerId > 0 ? data[23] : (byte)0,
            layerId > 0 && blending,
            layerId > 0 ? data[26] : (byte)0,
            textureId,
            data[32],
            data[34],
            (byte)(layerId > 1 ? 32 : 16),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(36, 4)));
    }

    private static int FindTextureTableOffset(byte[] data, int cursor)
    {
        const string textureMarker = "TEXTURE";
        if (cursor + textureMarker.Length > data.Length)
            throw new InvalidDataException("Background texture marker is out of range.");

        var marker = System.Text.Encoding.ASCII.GetString(data, cursor, textureMarker.Length);
        if (marker != textureMarker)
            throw new InvalidDataException("Background texture marker was not found after tile layers.");

        return cursor + textureMarker.Length;
    }

    private static IReadOnlyList<BackgroundTexturePC> ReadTextures(byte[] data, int textureTableOffset)
    {
        var result = new List<BackgroundTexturePC>(TextureCount);
        var cursor = textureTableOffset;
        for (var textureId = 0; textureId < TextureCount; textureId++)
        {
            if (cursor + 2 > data.Length)
                throw new InvalidDataException($"Background texture {textureId} exists flag is out of range.");

            var exists = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(cursor, 2)) != 0;
            cursor += 2;
            if (!exists)
            {
                result.Add(new BackgroundTexturePC(textureId, false, false, 0, cursor, 0));
                continue;
            }

            if (cursor + 4 > data.Length)
                throw new InvalidDataException($"Background texture {textureId} metadata is out of range.");

            var isBigTile = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(cursor, 2)) != 0;
            var depth = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(cursor + 2, 2));
            cursor += 4;
            var dataSize = depth == 0 ? 32768 : checked(depth * 65536);
            if (cursor + dataSize > data.Length)
                throw new InvalidDataException($"Background texture {textureId} data is out of range.");

            result.Add(new BackgroundTexturePC(textureId, true, isBigTile, depth, cursor - textureTableOffset, dataSize));
            cursor += dataSize;
        }

        return result;
    }

    private static ushort ReadUInt16(byte[] data, int offset, string label)
    {
        if (offset + 2 > data.Length)
            throw new InvalidDataException($"{label} is out of range.");

        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    }

    private void AddTileRectangle(
        List<byte[]> tiles,
        BackgroundTilePC template,
        int x,
        int y,
        int width,
        int height,
        int tileSize)
    {
        for (var tileY = y; tileY < y + height; tileY += tileSize)
        {
            for (var tileX = x; tileX < x + width; tileX += tileSize)
                tiles.Add(CloneLayer0Tile(template, checked((short)tileX), checked((short)tileY)));
        }
    }

    private byte[] CloneLayer0Tile(BackgroundTilePC tile, short destinationX, short destinationY)
    {
        var raw = new byte[TileRecordSize];
        _data.AsSpan(tile.Offset, TileRecordSize).CopyTo(raw);
        BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(0, 2), destinationX);
        BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(2, 2), destinationY);
        return raw;
    }
}

public sealed record BackgroundResizeResult(bool Changed, int TilesAdded, byte[] Data);

public sealed record BackgroundRepairResult(bool Changed, int TilesRepaired, byte[] Data);

public sealed record BackgroundLayerPC(
    int LayerId,
    bool Exists,
    int DeclaredTileCount,
    IReadOnlyList<BackgroundTilePC> Tiles,
    int HeaderOffset,
    int TileOffset,
    int EndOffset)
{
    public string Name => $"Layer {LayerId + 1}";
    public int VisibleTileCount => Tiles.Count;
}

public sealed record BackgroundTilePC(
    int LayerId,
    int TileId,
    int Offset,
    short DestinationX,
    short DestinationY,
    byte SourceX,
    byte SourceY,
    byte PaletteId,
    int Id,
    byte Param,
    byte State,
    bool Blending,
    byte TypeTrans,
    byte TextureId,
    byte TextureId2,
    byte Depth,
    byte Size,
    uint IdBig)
{
    public string Destination => $"{DestinationX}, {DestinationY}";
    public string Source => $"{SourceX}, {SourceY}";
}

public sealed record BackgroundTexturePC(
    int TextureId,
    bool Exists,
    bool IsBigTile,
    int Depth,
    int DataOffset,
    int DataSize)
{
    public string TileSize => IsBigTile ? "32x32" : "16x16";
}

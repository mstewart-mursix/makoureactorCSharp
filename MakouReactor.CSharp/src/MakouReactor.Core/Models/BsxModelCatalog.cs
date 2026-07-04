using System.Buffers.Binary;

using MakouReactor.Core.IO;

namespace MakouReactor.Core.Models;

public sealed class BsxModelCatalog
{
    private const int ModelsHeaderSize = 16;
    private const int ModelHeaderSize = 48;
    private const int BoneHeaderSize = 4;
    private const int PartHeaderSize = 32;
    private const int AnimationHeaderSize = 16;
    private readonly byte[] _data;
    private readonly uint _offsetModels;

    private BsxModelCatalog(byte[] data, uint offsetModels, IReadOnlyList<BsxModelInfo> models)
    {
        _data = data;
        _offsetModels = offsetModels;
        Models = models;
    }

    public IReadOnlyList<BsxModelInfo> Models { get; }

    public static BsxModelCatalog Open(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            throw new InvalidDataException("BSX data is too short to contain a model table pointer.");

        var offsetModels = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4));
        if (offsetModels > data.Length || offsetModels + ModelsHeaderSize > data.Length)
            throw new InvalidDataException("BSX model table offset is out of range.");

        var modelCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice((int)offsetModels + 4, 4)));
        var modelsOffset = checked((int)offsetModels + ModelsHeaderSize);
        if (modelCount < 0 || modelsOffset + (modelCount * ModelHeaderSize) > data.Length)
            throw new InvalidDataException("BSX model headers exceed the file size.");

        var models = new List<BsxModelInfo>(modelCount);
        for (var index = 0; index < modelCount; index++)
        {
            var offset = modelsOffset + (index * ModelHeaderSize);
            var header = ReadModelHeader(data.Slice(offset, ModelHeaderSize));
            models.Add(new BsxModelInfo(
                index,
                header.ModelId,
                header.NumBones,
                header.NumParts,
                header.NumAnimations));
        }

        return new BsxModelCatalog(data.ToArray(), offsetModels, models);
    }

    public byte[] ExportPcAnimation(int modelIndex, int animationIndex)
    {
        var animation = ReadPlayStationAnimation(modelIndex, animationIndex);
        return AFile.Write(BsxAnimationFile.ToPcAnimation(animation));
    }

    public BsxAnimationHeader ReadAnimationHeader(int modelIndex, int animationIndex)
    {
        if ((uint)modelIndex >= (uint)Models.Count)
            throw new ArgumentOutOfRangeException(nameof(modelIndex));

        var modelHeaderOffset = checked((int)_offsetModels + ModelsHeaderSize + (modelIndex * ModelHeaderSize));
        var modelHeader = ReadModelHeader(_data.AsSpan(modelHeaderOffset, ModelHeaderSize));
        if ((uint)animationIndex >= modelHeader.NumAnimations)
            throw new ArgumentOutOfRangeException(nameof(animationIndex));

        var skeletonOffset = checked(modelHeaderOffset + (int)modelHeader.OffsetSkeleton);
        var animationHeadersOffset = checked(
            skeletonOffset +
            (modelHeader.NumBones * BoneHeaderSize) +
            (modelHeader.NumParts * PartHeaderSize));
        var animationHeaderOffset = checked(animationHeadersOffset + (animationIndex * AnimationHeaderSize));
        if (animationHeaderOffset + AnimationHeaderSize > _data.Length)
            throw new InvalidDataException("BSX animation header offset is out of range.");

        return ReadAnimationHeader(_data.AsSpan(animationHeaderOffset, AnimationHeaderSize));
    }

    private FieldModelAnimation ReadPlayStationAnimation(int modelIndex, int animationIndex) =>
        BsxAnimationFile.ReadAnimation(_data, ReadAnimationHeader(modelIndex, animationIndex));

    private static BsxModelHeader ReadModelHeader(ReadOnlySpan<byte> data) =>
        new(
            BinaryPrimitives.ReadUInt16LittleEndian(data[..2]),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4)),
            data[27],
            data[39],
            data[47]);

    private static BsxAnimationHeader ReadAnimationHeader(ReadOnlySpan<byte> data) =>
        new(
            BinaryPrimitives.ReadUInt16LittleEndian(data[..2]),
            data[2],
            data[3],
            data[4],
            data[5],
            BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10, 2)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4)));

    private readonly record struct BsxModelHeader(
        ushort ModelId,
        uint OffsetSkeleton,
        byte NumBones,
        byte NumParts,
        byte NumAnimations);
}

public sealed record BsxModelInfo(
    int Index,
    ushort ModelId,
    byte BoneCount,
    byte PartCount,
    byte AnimationCount);

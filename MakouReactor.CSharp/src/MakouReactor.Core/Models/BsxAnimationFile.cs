using System.Buffers.Binary;

namespace MakouReactor.Core.Models;

public static class BsxAnimationFile
{
    private const float ModelScalePs = 4096.0f;
    private const int FrameTranslationSize = 8;

    public static FieldModelAnimation ReadAnimation(
        ReadOnlySpan<byte> data,
        BsxAnimationHeader header)
    {
        var offsetToAnimation = checked((int)(header.OffsetData & 0x7FFF_FFFF));
        if (offsetToAnimation < 0 || offsetToAnimation + 4 > data.Length)
            throw new InvalidDataException("BSX animation data offset is out of range.");

        var rotationTableOffset = checked(offsetToAnimation + header.OffsetFramesRotation);
        var staticTranslationOffset = checked(offsetToAnimation + header.OffsetStaticTranslation);
        var frameTranslationOffset = checked(offsetToAnimation + header.OffsetFramesTranslation);
        var descriptorOffset = offsetToAnimation + 4;
        if (descriptorOffset + (header.NumBones * FrameTranslationSize) > data.Length)
            throw new InvalidDataException("BSX animation frame translation table is out of range.");

        var animation = new FieldModelAnimation(new ModelCoordinate());
        for (var frame = 0; frame < header.NumFrames; frame++)
        {
            var rotations = new List<ModelCoordinate>(header.NumBones);
            var translations = new List<ModelCoordinate>(header.NumBones);
            for (var bone = 0; bone < header.NumBones; bone++)
            {
                var descriptor = ReadFrameTranslation(
                    data.Slice(descriptorOffset + (bone * FrameTranslationSize), FrameTranslationSize));
                var rotation = new ModelCoordinate(
                    ReadRotation(data, rotationTableOffset, descriptor.Rx, descriptor.Flag, 0x01, header.NumFrames, frame),
                    ReadRotation(data, rotationTableOffset, descriptor.Ry, descriptor.Flag, 0x02, header.NumFrames, frame),
                    ReadRotation(data, rotationTableOffset, descriptor.Rz, descriptor.Flag, 0x04, header.NumFrames, frame));
                var translation = new ModelCoordinate(
                    ReadTranslation(data, frameTranslationOffset, staticTranslationOffset, descriptor.Tx, descriptor.Flag, 0x10, header.NumFrames, frame),
                    ReadTranslation(data, frameTranslationOffset, staticTranslationOffset, descriptor.Ty, descriptor.Flag, 0x20, header.NumFrames, frame),
                    ReadTranslation(data, frameTranslationOffset, staticTranslationOffset, descriptor.Tz, descriptor.Flag, 0x40, header.NumFrames, frame));
                rotations.Add(rotation);
                translations.Add(translation);
            }

            animation.AddFrame(rotations, translations.Count > 0 ? translations[0] : new ModelCoordinate());
        }

        return animation;
    }

    public static FieldModelAnimation ToPcAnimation(FieldModelAnimation playStationAnimation)
    {
        ArgumentNullException.ThrowIfNull(playStationAnimation);
        if (playStationAnimation.FrameCount == 0)
            return new FieldModelAnimation(new ModelCoordinate());
        if (playStationAnimation.BoneCount <= 1)
            throw new InvalidDataException("PlayStation animation must contain at least one root bone and one child bone.");

        var firstFrame = playStationAnimation.Frames[0];
        var pc = new FieldModelAnimation(firstFrame.Rotations[0]);
        foreach (var frame in playStationAnimation.Frames)
            pc.AddFrame(frame.Rotations.Skip(1).ToArray(), frame.Translation);

        return pc;
    }

    private static BsxFrameTranslation ReadFrameTranslation(ReadOnlySpan<byte> data) =>
        new(data[0], data[1], data[2], data[3], data[4], data[5], data[6]);

    private static float ReadRotation(
        ReadOnlySpan<byte> data,
        int rotationTableOffset,
        byte value,
        byte flags,
        byte flag,
        ushort frameCount,
        int frame)
    {
        var rotationByte = (flags & flag) != 0
            ? ReadByte(data, checked(rotationTableOffset + (value * frameCount) + frame))
            : value;
        return 360.0f * rotationByte / 256.0f;
    }

    private static float ReadTranslation(
        ReadOnlySpan<byte> data,
        int frameTranslationOffset,
        int staticTranslationOffset,
        byte value,
        byte flags,
        byte flag,
        ushort frameCount,
        int frame)
    {
        if ((flags & flag) != 0)
        {
            var offset = checked(frameTranslationOffset + (value * frameCount * 2) + (frame * 2));
            return -ReadInt16(data, offset) / ModelScalePs;
        }

        if (value == 0xFF)
            return 0;

        return -ReadInt16(data, checked(staticTranslationOffset + (value * 2))) / ModelScalePs;
    }

    private static byte ReadByte(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset >= data.Length)
            throw new InvalidDataException("BSX animation rotation offset is out of range.");

        return data[offset];
    }

    private static short ReadInt16(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset + 2 > data.Length)
            throw new InvalidDataException("BSX animation translation offset is out of range.");

        return BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));
    }
}

public readonly record struct BsxAnimationHeader(
    ushort NumFrames,
    byte NumBones,
    byte NumFramesTranslation,
    byte NumStaticTranslation,
    byte NumFramesRotation,
    ushort OffsetFramesTranslation,
    ushort OffsetStaticTranslation,
    ushort OffsetFramesRotation,
    uint OffsetData);

internal readonly record struct BsxFrameTranslation(
    byte Flag,
    byte Rx,
    byte Ry,
    byte Rz,
    byte Tx,
    byte Ty,
    byte Tz);

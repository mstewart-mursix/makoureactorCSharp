using System.Buffers.Binary;

using MakouReactor.Core.Models;

namespace MakouReactor.Core.IO;

public static class AFile
{
    private const float ModelScalePc = 132.0f;
    private const int HeaderSize = 36;
    private const int CoordinateSize = 12;

    public static byte[] Write(FieldModelAnimation animation)
    {
        ArgumentNullException.ThrowIfNull(animation);

        using var stream = new MemoryStream();
        Span<byte> header = stackalloc byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header[..4], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4, 4), checked((uint)animation.FrameCount));
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(8, 4), checked((uint)animation.BoneCount));
        header[12] = 1;
        header[13] = 0;
        header[14] = 2;
        stream.Write(header);

        foreach (var frame in animation.Frames)
        {
            WriteCoordinate(stream, animation.InitialRotation);
            WriteCoordinate(stream, new ModelCoordinate(
                frame.Translation.X * ModelScalePc,
                frame.Translation.Y * ModelScalePc,
                frame.Translation.Z * ModelScalePc));
            foreach (var rotation in frame.Rotations)
                WriteCoordinate(stream, rotation);
        }

        return stream.ToArray();
    }

    private static void WriteCoordinate(Stream stream, ModelCoordinate coordinate)
    {
        Span<byte> data = stackalloc byte[CoordinateSize];
        BinaryPrimitives.WriteSingleLittleEndian(data[..4], coordinate.X);
        BinaryPrimitives.WriteSingleLittleEndian(data.Slice(4, 4), coordinate.Y);
        BinaryPrimitives.WriteSingleLittleEndian(data.Slice(8, 4), coordinate.Z);
        stream.Write(data);
    }
}

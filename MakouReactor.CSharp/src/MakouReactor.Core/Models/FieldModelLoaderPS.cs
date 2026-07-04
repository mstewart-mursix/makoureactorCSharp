using System.Buffers.Binary;
using System.IO;

namespace MakouReactor.Core.Models;

public sealed class FieldModelLoaderPS
{
    public List<FieldModelInfoPS> Models { get; } = [];

    public int ModelCount => Models.Count;

    public static FieldModelLoaderPS Open(ReadOnlySpan<byte> data)
    {
        var loader = new FieldModelLoaderPS();
        loader.Load(data);
        return loader;
    }

    public void Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            throw new InvalidDataException("PlayStation model loader section is too short.");

        var declaredSize = BinaryPrimitives.ReadUInt16LittleEndian(data[..2]);
        var modelCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(2, 2));
        if (declaredSize != data.Length || declaredSize != 4 + (modelCount * 8))
            throw new InvalidDataException("PlayStation model loader section has an invalid size.");

        Models.Clear();
        for (var index = 0; index < modelCount; index++)
        {
            var offset = 4 + (index * 8);
            Models.Add(new FieldModelInfoPS(
                index,
                data[offset],
                data[offset + 1],
                data[offset + 2],
                data[offset + 3],
                data[offset + 4],
                data[offset + 5],
                data[offset + 6],
                data[offset + 7]));
        }
    }

    public byte[] Save()
    {
        var output = new byte[4 + (Models.Count * 8)];
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(0, 2), checked((ushort)output.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(2, 2), checked((ushort)Models.Count));
        for (var index = 0; index < Models.Count; index++)
        {
            var offset = 4 + (index * 8);
            var model = Models[index];
            output[offset] = model.FaceId;
            output[offset + 1] = model.BonesCount;
            output[offset + 2] = model.PartsCount;
            output[offset + 3] = model.AnimationCount;
            output[offset + 4] = model.Unknown1;
            output[offset + 5] = model.Unknown2;
            output[offset + 6] = model.Unknown3;
            output[offset + 7] = model.ModelId;
        }

        return output;
    }

    public void ReplaceModels(IEnumerable<FieldModelInfoPS> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        Models.Clear();
        Models.AddRange(models.Select((model, index) => model with { Id = index }));
    }
}

public sealed record FieldModelInfoPS(
    int Id,
    byte FaceId,
    byte BonesCount,
    byte PartsCount,
    byte AnimationCount,
    byte Unknown1,
    byte Unknown2,
    byte Unknown3,
    byte ModelId);

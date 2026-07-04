using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace MakouReactor.Core.Models;

public sealed class FieldModelLoaderPC
{
    public List<FieldModelInfoPC> Models { get; } = [];
    public int ModelCount => Models.Count;

    public static FieldModelLoaderPC Open(ReadOnlySpan<byte> data)
    {
        var loader = new FieldModelLoaderPC();
        loader.Load(data);
        return loader;
    }

    public void Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
            throw new InvalidDataException("Model loader section is too short.");

        Models.Clear();
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(2, 2));
        var cursor = 6;

        for (var modelIndex = 0; modelIndex < count; modelIndex++)
        {
            if (cursor + 2 > data.Length)
                throw new InvalidDataException("Model loader record is truncated.");

            var charNameLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(cursor, 2));
            if (cursor + 48 + charNameLength > data.Length)
                throw new InvalidDataException("Model loader model record exceeds section size.");

            var charName = ReadLatin1(data.Slice(cursor + 2, charNameLength));
            var unknown = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(cursor + 2 + charNameLength, 2));
            var hrcName = ReadLatin1(data.Slice(cursor + 4 + charNameLength, 8));
            var typeText = ReadLatin1(data.Slice(cursor + 12 + charNameLength, 4));
            var scale = ushort.TryParse(typeText, out var parsedScale) ? parsedScale : (ushort)0;
            var animationCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(cursor + 16 + charNameLength, 2));

            cursor += 18 + charNameLength;
            var lightingData = data.Slice(cursor, 30).ToArray();
            var globalColor = new RgbColor(data[cursor + 27], data[cursor + 28], data[cursor + 29]);
            cursor += 30;

            var animations = new List<FieldModelAnimationInfoPC>(animationCount);
            for (var animationIndex = 0; animationIndex < animationCount; animationIndex++)
            {
                if (cursor + 2 > data.Length)
                    throw new InvalidDataException("Model loader animation record is truncated.");

                var animationNameLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(cursor, 2));
                if (cursor + 4 + animationNameLength > data.Length)
                    throw new InvalidDataException("Model loader animation record exceeds section size.");

                var animationName = ReadLatin1(data.Slice(cursor + 2, animationNameLength));
                var animationUnknown = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(cursor + 2 + animationNameLength, 2));
                animations.Add(new FieldModelAnimationInfoPC(animationIndex, animationName, animationUnknown));
                cursor += 4 + animationNameLength;
            }

            Models.Add(new FieldModelInfoPC(modelIndex, charName, hrcName, unknown, scale, globalColor, animations, lightingData));
        }
    }

    public byte[] Save()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)0);
        writer.Write(checked((ushort)Models.Count));
        writer.Write((ushort)0);
        foreach (var model in Models)
            WriteModel(writer, model);

        return stream.ToArray();
    }

    public void ReplaceModels(IEnumerable<FieldModelInfoPC> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        Models.Clear();
        Models.AddRange(models.Select((model, index) => model with { Id = index }));
    }

    public int CleanUnusedData()
    {
        var changed = 0;
        for (var modelIndex = 0; modelIndex < Models.Count; modelIndex++)
        {
            var model = Models[modelIndex];
            if (!string.IsNullOrEmpty(model.CharacterName))
            {
                model = model with { CharacterName = string.Empty };
                changed++;
            }

            var animations = model.Animations
                .Select(animation =>
                {
                    var extensionIndex = animation.Name.LastIndexOf('.');
                    if (extensionIndex < 0)
                        return animation;

                    changed++;
                    return animation with { Name = animation.Name[..extensionIndex] };
                })
                .ToArray();

            Models[modelIndex] = model with { Animations = animations };
        }

        return changed;
    }

    private static string ReadLatin1(ReadOnlySpan<byte> data)
    {
        var length = data.IndexOf((byte)0);
        if (length < 0)
            length = data.Length;
        return Encoding.Latin1.GetString(data[..length]);
    }

    private static void WriteModel(BinaryWriter writer, FieldModelInfoPC model)
    {
        var characterName = Encoding.Latin1.GetBytes(model.CharacterName);
        writer.Write(checked((ushort)characterName.Length));
        writer.Write(characterName);
        writer.Write(model.Unknown);
        WriteFixed(writer, model.HrcName, 8);
        WriteFixed(writer, model.Scale.ToString("D3"), 4);
        writer.Write(checked((ushort)model.Animations.Count));

        var lightingData = model.LightingData?.ToArray() ?? new byte[30];
        if (lightingData.Length != 30)
            throw new InvalidDataException("PC model loader lighting block must be 30 bytes.");
        lightingData[27] = model.GlobalColor.R;
        lightingData[28] = model.GlobalColor.G;
        lightingData[29] = model.GlobalColor.B;
        writer.Write(lightingData);

        foreach (var animation in model.Animations)
        {
            var animationName = Encoding.Latin1.GetBytes(animation.Name);
            writer.Write(checked((ushort)animationName.Length));
            writer.Write(animationName);
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
}

public sealed record FieldModelInfoPC(
    int Id,
    string CharacterName,
    string HrcName,
    ushort Unknown,
    ushort Scale,
    RgbColor GlobalColor,
    IReadOnlyList<FieldModelAnimationInfoPC> Animations,
    byte[]? LightingData = null);

public sealed record FieldModelAnimationInfoPC(int Id, string Name, ushort Unknown);

public sealed record RgbColor(byte R, byte G, byte B)
{
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

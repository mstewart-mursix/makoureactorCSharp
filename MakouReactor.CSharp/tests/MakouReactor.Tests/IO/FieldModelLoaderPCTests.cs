using System.Text;

using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public sealed class FieldModelLoaderPCTests
{
    [Fact]
    public void open_reads_model_metadata_and_animation_names()
    {
        var loader = FieldModelLoaderPC.Open(BuildModelLoader());

        loader.ModelCount.Should().Be(1);
        var model = loader.Models[0];
        model.CharacterName.Should().Be("Cloud");
        model.HrcName.Should().Be("AAAA");
        model.Unknown.Should().Be(7);
        model.Scale.Should().Be(512);
        model.GlobalColor.ToString().Should().Be("#102030");
        model.Animations.Select(animation => animation.Name).Should().Equal("idle", "walk");
        model.Animations[1].Unknown.Should().Be(2);
    }

    [Fact]
    public void open_rejects_truncated_record()
    {
        var open = () => FieldModelLoaderPC.Open([0, 0, 1, 0, 0, 0, 20, 0]);

        open.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void save_round_trips_edited_model_loader_metadata()
    {
        var loader = FieldModelLoaderPC.Open(BuildModelLoader());
        loader.ReplaceModels(
        [
            loader.Models[0] with
            {
                CharacterName = "Tifa",
                HrcName = "BBAA",
                Unknown = 9,
                Scale = 384,
                GlobalColor = new RgbColor(0x40, 0x50, 0x60),
                Animations =
                [
                    loader.Models[0].Animations[0] with { Name = "stand", Unknown = 3 },
                    loader.Models[0].Animations[1],
                ],
            },
        ]);

        var reopened = FieldModelLoaderPC.Open(loader.Save());

        reopened.ModelCount.Should().Be(1);
        reopened.Models[0].CharacterName.Should().Be("Tifa");
        reopened.Models[0].HrcName.Should().Be("BBAA");
        reopened.Models[0].Unknown.Should().Be(9);
        reopened.Models[0].Scale.Should().Be(384);
        reopened.Models[0].GlobalColor.Should().Be(new RgbColor(0x40, 0x50, 0x60));
        reopened.Models[0].Animations[0].Name.Should().Be("stand");
        reopened.Models[0].Animations[0].Unknown.Should().Be(3);
    }

    [Fact]
    public void clean_unused_data_clears_character_names_and_strips_animation_extensions()
    {
        var loader = FieldModelLoaderPC.Open(BuildModelLoader(
            characterName: "Cloud",
            animations: [("idle.a", (ushort)1), ("walk", (ushort)2), ("run.anim", (ushort)3)]));

        var changed = loader.CleanUnusedData();
        var reopened = FieldModelLoaderPC.Open(loader.Save());

        changed.Should().Be(3);
        reopened.Models[0].CharacterName.Should().BeEmpty();
        reopened.Models[0].Animations.Select(animation => animation.Name)
            .Should().Equal("idle", "walk", "run");
    }

    internal static byte[] BuildModelLoader(
        string characterName = "Cloud",
        IReadOnlyList<(string Name, ushort Unknown)>? animations = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)0);

        WriteModel(
            writer,
            characterName,
            7,
            "AAAA",
            "512",
            animations ?? [("idle", (ushort)1), ("walk", (ushort)2)]);

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
}

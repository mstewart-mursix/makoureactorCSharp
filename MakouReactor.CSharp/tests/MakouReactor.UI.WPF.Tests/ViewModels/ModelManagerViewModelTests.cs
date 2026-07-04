using System.Buffers.Binary;
using System.IO;

using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.ViewModels;

public sealed class ModelManagerViewModelTests
{
    [Fact]
    public void view_model_exposes_model_loader_rows()
    {
        var field = FieldPC.OpenDecompressed("md1stin", BuildField());
        var viewModel = new ModelManagerViewModel("md1stin", field);

        viewModel.FieldName.Should().Be("md1stin");
        viewModel.Models.Should().HaveCount(1);
        viewModel.SelectedModel.Should().Be(viewModel.Models[0]);
        viewModel.SelectedModel!.CharacterName.Should().Be("Cloud");
        viewModel.SelectedAnimation!.Name.Should().Be("idle");
        viewModel.StatusText.Should().Be("1 model loaded.");
    }

    [Fact]
    public void apply_changes_writes_model_loader_section_to_field()
    {
        var field = FieldPC.OpenDecompressed("md1stin", BuildField());
        var viewModel = new ModelManagerViewModel("md1stin", field);

        viewModel.SelectedModel!.CharacterName = "Tifa";
        viewModel.SelectedModel.Scale = 384;
        viewModel.SelectedModel.GlobalR = 0x40;
        viewModel.SelectedAnimation!.Name = "stand";
        viewModel.ApplyChanges();
        var reopened = FieldPC.OpenDecompressed("md1stin", field.SaveDecompressed());

        field.IsModified.Should().BeTrue();
        viewModel.IsModified.Should().BeFalse();
        reopened.ModelLoader!.Models[0].CharacterName.Should().Be("Tifa");
        reopened.ModelLoader.Models[0].Scale.Should().Be(384);
        reopened.ModelLoader.Models[0].GlobalColor.R.Should().Be(0x40);
        reopened.ModelLoader.Models[0].Animations[0].Name.Should().Be("stand");
    }

    private static byte[] BuildField()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[2] = BuildModelLoader();

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

    private static byte[] BuildModelLoader()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1);
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
        var nameBytes = System.Text.Encoding.Latin1.GetBytes(characterName);
        writer.Write((ushort)nameBytes.Length);
        writer.Write(nameBytes);
        writer.Write(unknown);
        WriteFixed(writer, hrcName, 8);
        WriteFixed(writer, scale, 4);
        writer.Write((ushort)animations.Count);
        writer.Write(new byte[27]);
        writer.Write((byte)0x10);
        writer.Write((byte)0x20);
        writer.Write((byte)0x30);

        foreach (var animation in animations)
        {
            var animationBytes = System.Text.Encoding.Latin1.GetBytes(animation.Name);
            writer.Write((ushort)animationBytes.Length);
            writer.Write(animationBytes);
            writer.Write(animation.Unknown);
        }
    }

    private static void WriteFixed(BinaryWriter writer, string value, int length)
    {
        var bytes = System.Text.Encoding.Latin1.GetBytes(value);
        writer.Write(bytes.AsSpan(0, Math.Min(bytes.Length, length)));
        if (bytes.Length < length)
            writer.Write(new byte[length - bytes.Length]);
    }
}

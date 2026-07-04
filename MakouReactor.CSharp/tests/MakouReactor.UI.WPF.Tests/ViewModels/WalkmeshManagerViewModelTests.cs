using System.Buffers.Binary;

using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.ViewModels;

public sealed class WalkmeshManagerViewModelTests
{
    [Fact]
    public void view_model_exposes_walkmesh_triangles()
    {
        var field = FieldPC.OpenDecompressed("md1stin", BuildField());
        var viewModel = new WalkmeshManagerViewModel("md1stin", field);

        viewModel.FieldName.Should().Be("md1stin");
        viewModel.Triangles.Should().HaveCount(1);
        viewModel.PreviewTriangles.Should().ContainSingle();
        viewModel.PreviewTriangles[0].Points.Should().Contain(" ");
        viewModel.PreviewTriangles[0].IsSelected.Should().BeTrue();
        viewModel.SelectedTriangle.Should().Be(viewModel.Triangles[0]);
        viewModel.StatusText.Should().Be("1 triangle loaded.");
    }

    [Fact]
    public void add_remove_and_apply_updates_field_walkmesh()
    {
        var field = FieldPC.OpenDecompressed("md1stin", BuildField());
        var viewModel = new WalkmeshManagerViewModel("md1stin", field);

        viewModel.AddTriangle();
        viewModel.Triangles.Should().HaveCount(2);
        viewModel.PreviewTriangles.Should().HaveCount(2);
        viewModel.RemoveSelectedTriangle();
        viewModel.ApplyChanges();

        field.IsModified.Should().BeTrue();
        viewModel.IsModified.Should().BeFalse();
        var reopened = FieldPC.OpenDecompressed("md1stin", field.SaveDecompressed());
        reopened.Walkmesh!.TriangleCount.Should().Be(1);
    }

    private static byte[] BuildField()
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

    private static byte[] BuildWalkmesh()
    {
        var data = new byte[34];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 1);
        WriteVertex(data.AsSpan(4, 8), -10, 0, 5, 5);
        WriteVertex(data.AsSpan(12, 8), 0, 20, 5, 5);
        WriteVertex(data.AsSpan(20, 8), 10, 0, 30, 30);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(28, 2), -1);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(30, 2), 2);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(32, 2), 3);
        return data;
    }

    private static void WriteVertex(Span<byte> data, short x, short y, short z, short res)
    {
        BinaryPrimitives.WriteInt16LittleEndian(data[..2], x);
        BinaryPrimitives.WriteInt16LittleEndian(data.Slice(2, 2), y);
        BinaryPrimitives.WriteInt16LittleEndian(data.Slice(4, 2), z);
        BinaryPrimitives.WriteInt16LittleEndian(data.Slice(6, 2), res);
    }
}

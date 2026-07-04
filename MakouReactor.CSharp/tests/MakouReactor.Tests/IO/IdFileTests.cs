using System.Buffers.Binary;

using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public sealed class IdFileTests
{
    [Fact]
    public void open_reads_triangles_and_access_table()
    {
        var data = BuildWalkmesh();

        var id = IdFile.Open(data);

        id.TriangleCount.Should().Be(1);
        id.Triangles[0].Vertices[0].X.Should().Be(-10);
        id.Triangles[0].Vertices[1].Y.Should().Be(20);
        id.Triangles[0].Vertices[2].Z.Should().Be(30);
        id.Access[0].A.Should().Equal([-1, 2, 3]);
    }

    [Fact]
    public void open_accepts_two_byte_padding()
    {
        var source = BuildWalkmesh();
        var data = new byte[source.Length + 2];
        source.CopyTo(data, 0);

        IdFile.Open(data).TriangleCount.Should().Be(1);
    }

    [Fact]
    public void open_rejects_invalid_size()
    {
        var open = () => IdFile.Open([0x01, 0x00, 0x00, 0x00, 0x00]);

        open.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void save_round_trips_walkmesh_triangles_and_access()
    {
        var id = IdFile.Open(BuildWalkmesh());
        id.SetTriangle(0, new Triangle(
            new VertexSR(1, 2, 3, 4),
            new VertexSR(5, 6, 7, 8),
            new VertexSR(9, 10, 11, 12)));
        id.SetAccess(0, new Access(3, 2, -1));

        var reopened = IdFile.Open(id.Save());

        reopened.TriangleCount.Should().Be(1);
        reopened.Triangles[0].Vertices[2].Z.Should().Be(11);
        reopened.Access[0].A.Should().Equal([3, 2, -1]);
    }

    [Fact]
    public void insert_and_remove_triangle_keeps_access_table_aligned()
    {
        var id = IdFile.Open(BuildWalkmesh());
        id.InsertTriangle(1, new Triangle(
            new VertexSR(1, 1, 1, 0),
            new VertexSR(2, 2, 2, 0),
            new VertexSR(3, 3, 3, 0)));

        id.TriangleCount.Should().Be(2);
        id.Access.Should().HaveCount(2);
        id.RemoveTriangle(0);

        id.TriangleCount.Should().Be(1);
        id.Access.Should().HaveCount(1);
        IdFile.Open(id.Save()).TriangleCount.Should().Be(1);
    }

    internal static byte[] BuildWalkmesh()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(1u);
        WriteVertex(writer, -10, 0, 5, 5);
        WriteVertex(writer, 0, 20, 5, 5);
        WriteVertex(writer, 10, 0, 30, 30);
        writer.Write((short)-1);
        writer.Write((short)2);
        writer.Write((short)3);

        return stream.ToArray();
    }

    private static void WriteVertex(BinaryWriter writer, short x, short y, short z, short res)
    {
        writer.Write(x);
        writer.Write(y);
        writer.Write(z);
        writer.Write(res);
    }
}

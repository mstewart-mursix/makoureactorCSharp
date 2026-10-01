using FluentAssertions;

using MakouReactor.AI.Layout;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Context;

public class WalkmeshGeometryTests
{
    [Fact]
    public void from_returns_null_without_walkmesh()
    {
        WalkmeshGeometry.From((Field?)null).Should().BeNull();
        WalkmeshGeometry.From(new MeshField("x")).Should().BeNull();
        WalkmeshGeometry.From(new IdFile()).Should().BeNull();
    }

    [Fact]
    public void contains_handles_inside_edge_and_outside()
    {
        var mesh = WalkmeshGeometry.From(MeshField.Square(0, 100))!;

        mesh.Contains(new Point(50, 50)).Should().BeTrue();
        mesh.Contains(new Point(0, 0)).Should().BeTrue("corners are on the edge");
        mesh.Contains(new Point(100, 50)).Should().BeTrue("edges are inclusive");
        mesh.Contains(new Point(101, 50)).Should().BeFalse();
        mesh.Contains(new Point(-1, -1)).Should().BeFalse();
    }

    [Fact]
    public void contains_respects_concave_shape()
    {
        var mesh = WalkmeshGeometry.From(MeshField.LShape())!;

        mesh.Contains(new Point(50, 50)).Should().BeTrue("inside the bar");
        mesh.Contains(new Point(150, 250)).Should().BeTrue("inside the foot");
        mesh.Contains(new Point(150, 50)).Should().BeFalse("in the notch of the L, inside the bounding box");
    }

    [Fact]
    public void degenerate_triangles_are_ignored()
    {
        var id = new IdFile();
        id.Triangles.Add(new Triangle(new VertexSR(0, 0, 0, 0), new VertexSR(10, 10, 0, 0), new VertexSR(20, 20, 0, 0)));
        id.Access.Add(new Access(-1, -1, -1));

        WalkmeshGeometry.From(id).Should().BeNull();
    }

    [Fact]
    public void nearest_returns_same_point_when_walkable_and_closest_edge_point_otherwise()
    {
        var mesh = WalkmeshGeometry.From(MeshField.Square(0, 100))!;

        mesh.Nearest(new Point(40, 60)).Should().Be(new Point(40, 60));
        mesh.Nearest(new Point(150, 50)).Should().Be(new Point(100, 50));
        mesh.Nearest(new Point(-30, -30)).Should().Be(new Point(0, 0));
    }

    [Fact]
    public void nearest_result_is_always_walkable()
    {
        var mesh = WalkmeshGeometry.From(MeshField.LShape())!;

        foreach (var p in new[] { new Point(150, 50), new Point(180, 10), new Point(-50, 400), new Point(120, 190) })
            mesh.Contains(mesh.Nearest(p)).Should().BeTrue($"{p} should snap onto the mesh");
    }

    [Fact]
    public void bounds_cover_all_vertices()
    {
        var mesh = WalkmeshGeometry.From(MeshField.LShape())!;

        mesh.Bounds.X.Should().Be(0);
        mesh.Bounds.Y.Should().Be(0);
        mesh.Bounds.Width.Should().Be(201);
        mesh.Bounds.Height.Should().Be(301);
    }

    [Fact]
    public void ascii_render_shows_walkable_shape_and_is_stable()
    {
        var mesh = WalkmeshGeometry.From(MeshField.LShape())!;

        var picture = mesh.RenderAscii(4, 3);

        picture.Should().Be("##..\n##..\n####");
        mesh.RenderAscii(4, 3).Should().Be(picture);
    }
}

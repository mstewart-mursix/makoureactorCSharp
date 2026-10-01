using FluentAssertions;

using MakouReactor.AI.Layout;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Layout;

public class WalkmeshBuilderTests
{
    private static Point[] Square(int min = 0, int max = 100) =>
        [new(min, min), new(max, min), new(max, max), new(min, max)];

    private static double Area((Point A, Point B, Point C) t) =>
        Math.Abs(((long)(t.B.X - t.A.X) * (t.C.Y - t.A.Y)) - ((long)(t.B.Y - t.A.Y) * (t.C.X - t.A.X))) / 2.0;

    private static double PolygonArea(IReadOnlyList<Point> p)
    {
        long sum = 0;
        for (var i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            sum += ((long)a.X * b.Y) - ((long)b.X * a.Y);
        }

        return Math.Abs(sum) / 2.0;
    }

    [Fact]
    public void triangulates_convex_square_into_two_triangles_covering_the_area()
    {
        var triangles = WalkmeshBuilder.Triangulate(Square());

        triangles.Should().HaveCount(2);
        triangles.Sum(Area).Should().Be(PolygonArea(Square()));
    }

    [Fact]
    public void triangulates_concave_l_shape_preserving_area()
    {
        Point[] l = [new(0, 0), new(200, 0), new(200, 100), new(100, 100), new(100, 300), new(0, 300)];

        var triangles = WalkmeshBuilder.Triangulate(l);

        triangles.Should().HaveCount(4);
        triangles.Sum(Area).Should().Be(PolygonArea(l));
        // The notch (150, 200) is outside the L and must not be covered.
        var mesh = WalkmeshGeometry.FromTriangles(triangles.Select(t => (((double)t.A.X, (double)t.A.Y), ((double)t.B.X, (double)t.B.Y), ((double)t.C.X, (double)t.C.Y))))!;
        mesh.Contains(new Point(150, 200)).Should().BeFalse();
        mesh.Contains(new Point(50, 250)).Should().BeTrue();
        mesh.Contains(new Point(150, 50)).Should().BeTrue();
    }

    [Fact]
    public void output_triangles_are_counter_clockwise_for_both_input_windings()
    {
        var cw = Square().Reverse().ToArray();

        foreach (var polygon in new[] { Square(), cw })
        {
            foreach (var t in WalkmeshBuilder.Triangulate(polygon))
            {
                var cross = ((long)(t.B.X - t.A.X) * (t.C.Y - t.A.Y)) - ((long)(t.B.Y - t.A.Y) * (t.C.X - t.A.X));
                cross.Should().BePositive();
            }
        }
    }

    [Fact]
    public void ignores_repeated_closing_point_and_duplicate_neighbours()
    {
        Point[] polygon = [new(0, 0), new(100, 0), new(100, 0), new(100, 100), new(0, 100), new(0, 0)];

        WalkmeshBuilder.Triangulate(polygon).Should().HaveCount(2);
    }

    [Fact]
    public void collinear_vertex_on_an_edge_still_triangulates()
    {
        Point[] polygon = [new(0, 0), new(50, 0), new(100, 0), new(100, 100), new(0, 100)];

        var triangles = WalkmeshBuilder.Triangulate(polygon);

        triangles.Sum(Area).Should().Be(10000);
    }

    [Theory]
    [InlineData("two points", new[] { 0, 0, 10, 10 }, "at least 3")]
    [InlineData("zero area", new[] { 0, 0, 10, 10, 20, 20 }, "zero area")]
    [InlineData("bow tie", new[] { 0, 0, 100, 100, 100, 0, 0, 100 }, "self-intersecting")]
    [InlineData("too large", new[] { 0, 0, 40000, 0, 40000, 10 }, "+/-")]
    public void validate_polygon_reports_problems(string _, int[] coordinates, string expected)
    {
        var polygon = coordinates.Chunk(2).Select(c => new Point(c[0], c[1])).ToArray();

        WalkmeshBuilder.ValidatePolygon(polygon).Should().Contain(expected);
        var act = () => WalkmeshBuilder.Triangulate(polygon);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void validate_polygon_rejects_spike_that_folds_back_on_itself()
    {
        Point[] spike = [new(0, 0), new(100, 0), new(50, 0), new(100, 100), new(0, 100)];

        WalkmeshBuilder.ValidatePolygon(spike).Should().NotBeNull();
    }

    [Fact]
    public void validate_polygon_accepts_valid_polygon()
    {
        WalkmeshBuilder.ValidatePolygon(Square()).Should().BeNull();
    }

    [Fact]
    public void build_creates_idfile_with_adjacency_across_shared_edges()
    {
        var plan = new WalkmeshPlan { Regions = { new WalkmeshRegion { Id = "r", Polygon = Square().ToList() } } };

        var id = WalkmeshBuilder.Build(plan);

        id.Triangles.Should().HaveCount(2);
        id.Access.Should().HaveCount(2);
        // The two triangles share exactly one edge, so each lists the other once and has two open sides.
        id.Access[0].A.Count(a => a == 1).Should().Be(1);
        id.Access[1].A.Count(a => a == 0).Should().Be(1);
        id.Access[0].A.Count(a => a == -1).Should().Be(2);
    }

    [Fact]
    public void build_output_round_trips_through_idfile_save_and_open()
    {
        Point[] l = [new(0, 0), new(200, 0), new(200, 100), new(100, 100), new(100, 300), new(0, 300)];
        var plan = new WalkmeshPlan { Regions = { new WalkmeshRegion { Id = "l", Polygon = l.ToList() } } };

        var id = WalkmeshBuilder.Build(plan, groundZ: 7);
        var reopened = IdFile.Open(id.Save());

        reopened.Triangles.Should().HaveCount(4);
        reopened.Triangles.SelectMany(t => t.Vertices).Should().OnlyContain(v => v.Z == 7 && v.Res == 7);
        reopened.Access.Select(a => a.A).Should().BeEquivalentTo(id.Access.Select(a => a.A));
    }

    [Fact]
    public void apply_ignore_does_nothing_and_replace_swaps_the_mesh()
    {
        var existing = MeshField.Square(0, 50);
        var plan = new WalkmeshPlan { Regions = { new WalkmeshRegion { Id = "r", Polygon = Square(500, 600).ToList() } } };

        WalkmeshBuilder.Apply(existing, plan, WalkmeshMode.Ignore).Should().Be(0);
        existing.Triangles.Should().HaveCount(2);
        existing.Triangles[0].Vertices[0].X.Should().Be(0);

        WalkmeshBuilder.Apply(existing, plan, WalkmeshMode.Replace).Should().Be(2);
        existing.Triangles.Should().HaveCount(2);
        existing.Triangles.SelectMany(t => t.Vertices).Should().OnlyContain(v => v.X >= 500);
    }

    [Fact]
    public void apply_merge_appends_and_links_touching_triangles_without_touching_existing_links()
    {
        var existing = MeshField.Square(0, 100); // triangles 0 and 1 already linked to each other
        var neighbour = new WalkmeshPlan
        {
            Regions = { new WalkmeshRegion { Id = "east", Polygon = Square(0, 100).Select(p => new Point(p.X + 100, p.Y)).ToList() } },
        };

        var added = WalkmeshBuilder.Apply(existing, neighbour, WalkmeshMode.Merge);

        added.Should().Be(2);
        existing.Triangles.Should().HaveCount(4);
        existing.Access.Should().HaveCount(4);
        existing.Access[0].A.Should().Contain((short)1, "the original link is preserved");
        // The squares share the x=100 edge, so at least one new triangle links to an old one and vice versa.
        existing.Access.Take(2).SelectMany(a => a.A).Should().Contain(a => a >= 2);
        existing.Access.Skip(2).SelectMany(a => a.A).Should().Contain(a => a >= 0 && a < 2);
    }

    [Fact]
    public void apply_with_an_invalid_region_changes_nothing()
    {
        var existing = MeshField.Square(0, 50);
        var plan = new WalkmeshPlan
        {
            Regions =
            {
                new WalkmeshRegion { Id = "good", Polygon = Square(500, 600).ToList() },
                new WalkmeshRegion { Id = "bad", Polygon = [new(0, 0), new(1, 1)] },
            },
        };

        var act = () => WalkmeshBuilder.Apply(existing, plan, WalkmeshMode.Merge);

        act.Should().Throw<ArgumentException>().WithMessage("*bad*");
        existing.Triangles.Should().HaveCount(2);
    }
}

using System.Collections.Generic;

using FluentAssertions;

using MakouReactor.AI.Layout;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Layout;

// Concrete subclass of abstract Field for testing
public class TestField : Field
{
    public TestField(string name) : base(name) { }
}

public class LayoutGeneratorTests
{
    [Fact]
    public void field_bounds_returns_default_320x240()
    {
        var bounds = LayoutGenerator.FieldBounds(null);

        bounds.X.Should().Be(0);
        bounds.Y.Should().Be(0);
        bounds.Width.Should().Be(320);
        bounds.Height.Should().Be(240);
    }

    [Fact]
    public void field_bounds_ignores_field_instance()
    {
        var field = new TestField("test");
        var bounds = LayoutGenerator.FieldBounds(field);

        bounds.Width.Should().Be(320);
        bounds.Height.Should().Be(240);
    }

    [Fact]
    public void adjust_clamps_out_of_bounds_spawn()
    {
        var plan = CreatePlan(spawnX: 500, spawnY: 500);
        var field = new TestField("test");

        var result = LayoutGenerator.Adjust(plan, field);

        result.Ok.Should().BeTrue();
        plan.Layout.SpawnPoint.X.Should().BeLessThanOrEqualTo(320);
        plan.Layout.SpawnPoint.Y.Should().BeLessThanOrEqualTo(240);
    }

    [Fact]
    public void adjust_clamps_out_of_bounds_actors()
    {
        var plan = CreatePlan(actorX: 1000, actorY: 1000);
        var field = new TestField("test");

        var result = LayoutGenerator.Adjust(plan, field);

        result.Ok.Should().BeTrue();
        plan.Actors[0].Position.X.Should().BeLessThanOrEqualTo(320);
        plan.Actors[0].Position.Y.Should().BeLessThanOrEqualTo(240);
    }

    [Fact]
    public void adjust_clamps_out_of_bounds_props()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Layout = new LayoutDef
            {
                Props = new List<LayoutProp> { new("rock", 500, 500) }
            }
        };
        var field = new TestField("test");

        var result = LayoutGenerator.Adjust(plan, field);

        result.Ok.Should().BeTrue();
        plan.Layout.Props[0].Position.X.Should().BeLessThanOrEqualTo(320);
        plan.Layout.Props[0].Position.Y.Should().BeLessThanOrEqualTo(240);
    }

    [Fact]
    public void adjust_nudges_overlapping_actors()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Actors = new List<Actor>
            {
                new() { Id = "a1", Position = new Point(100, 100) },
                new() { Id = "a2", Position = new Point(100, 100) }
            },
            Layout = new LayoutDef { SpawnPoint = new Point(50, 50) }
        };
        var field = new TestField("test");

        var result = LayoutGenerator.Adjust(plan, field);

        result.Ok.Should().BeTrue();
        plan.Actors[0].Position.Should().NotBe(plan.Actors[1].Position);
    }

    [Fact]
    public void adjust_nudges_spawn_away_from_props()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Layout = new LayoutDef
            {
                SpawnPoint = new Point(100, 100),
                Props = new List<LayoutProp> { new("barrel", 100, 100) }
            }
        };
        var field = new TestField("test");

        var result = LayoutGenerator.Adjust(plan, field);

        result.Ok.Should().BeTrue();
        plan.Layout.SpawnPoint.Should().NotBe(new Point(100, 100));
    }

    [Fact]
    public void adjust_leaves_valid_plan_unchanged()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Actors = new List<Actor>
            {
                new() { Id = "a1", Position = new Point(50, 50) },
                new() { Id = "a2", Position = new Point(150, 50) }
            },
            Layout = new LayoutDef
            {
                SpawnPoint = new Point(100, 100),
                Props = new List<LayoutProp> { new("rock", 250, 200) }
            }
        };
        var field = new TestField("test");

        var result = LayoutGenerator.Adjust(plan, field);

        result.Ok.Should().BeTrue();
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public void adjust_reports_clamping_notes()
    {
        var plan = CreatePlan(actorX: 1000, actorY: 1000);
        var field = new TestField("test");

        var result = LayoutGenerator.Adjust(plan, field);

        result.Ok.Should().BeTrue();
        result.Notes.Should().NotBeEmpty();
        result.Notes.Should().Contain(n => n.Contains("Clamped"));
    }

    // --- Rect tests ---

    [Fact]
    public void rect_contains_point()
    {
        var rect = new Rect(0, 0, 320, 240);

        rect.Contains(new Point(100, 100)).Should().BeTrue();
        rect.Contains(new Point(0, 0)).Should().BeTrue();
        rect.Contains(new Point(319, 239)).Should().BeTrue();
        rect.Contains(new Point(320, 240)).Should().BeFalse();
        rect.Contains(new Point(-1, -1)).Should().BeFalse();
    }

    [Fact]
    public void rect_empty()
    {
        Rect.Empty.Width.Should().Be(0);
        Rect.Empty.Height.Should().Be(0);
    }

    [Fact]
    public void rect_equality()
    {
        var a = new Rect(10, 20, 30, 40);
        var b = new Rect(10, 20, 30, 40);
        var c = new Rect(10, 20, 30, 50);

        (a == b).Should().BeTrue();
        (a == c).Should().BeFalse();
        (a != c).Should().BeTrue();
    }

    // --- Point tests ---

    [Fact]
    public void point_equality()
    {
        var a = new Point(10, 20);
        var b = new Point(10, 20);
        var c = new Point(10, 30);

        (a == b).Should().BeTrue();
        (a == c).Should().BeFalse();
    }

    [Fact]
    public void point_to_string()
    {
        var p = new Point(10, 20);

        p.ToString().Should().Be("Point(10,20)");
    }

    // --- Helpers ---

    private static ScenePlan CreatePlan(int spawnX = 100, int spawnY = 100,
                                        int actorX = 50, int actorY = 50)
    {
        return new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Actors = new List<Actor>
            {
                new() { Id = "a1", Position = new Point(actorX, actorY) }
            },
            Layout = new LayoutDef { SpawnPoint = new Point(spawnX, spawnY) }
        };
    }
}

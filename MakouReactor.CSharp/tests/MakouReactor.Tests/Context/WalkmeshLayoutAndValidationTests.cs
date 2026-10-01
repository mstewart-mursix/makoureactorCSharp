using FluentAssertions;

using MakouReactor.AI.Layout;
using MakouReactor.AI.Validation;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Context;

public class WalkmeshLayoutAndValidationTests
{
    private static ScenePlan PlanWithActorAt(int x, int y)
    {
        var plan = new ScenePlan();
        plan.Actors.Add(new Actor("cloud", "Cloud", "idle", x, y));
        plan.Layout.SpawnPoint = new Point(50, 50);
        return plan;
    }

    [Fact]
    public void field_bounds_follow_walkmesh()
    {
        var field = new MeshField("f", MeshField.Square(-200, 200));

        var bounds = LayoutGenerator.FieldBounds(field);

        bounds.X.Should().Be(-200);
        bounds.Width.Should().Be(401);
    }

    [Fact]
    public void adjust_moves_off_mesh_actor_onto_walkmesh()
    {
        var field = new MeshField("f", MeshField.LShape());
        var plan = PlanWithActorAt(150, 50); // in the notch of the L

        var result = LayoutGenerator.Adjust(plan, field);

        field.Walkmesh!.Triangles.Should().NotBeEmpty();
        WalkmeshGeometry.From(field)!.Contains(plan.Actors[0].Position).Should().BeTrue();
        result.Notes.Should().Contain(n => n.Contains("onto walkmesh"));
    }

    [Fact]
    public void adjust_keeps_overlap_nudges_on_walkable_ground()
    {
        var field = new MeshField("f", MeshField.LShape());
        var plan = new ScenePlan();
        plan.Layout.SpawnPoint = new Point(50, 50);
        for (var i = 0; i < 6; i++)
            plan.Actors.Add(new Actor($"npc{i}", $"Npc{i}", "idle", 50, 50));

        LayoutGenerator.Adjust(plan, field);

        var mesh = WalkmeshGeometry.From(field)!;
        plan.Actors.Should().OnlyContain(a => mesh.Contains(a.Position));
        plan.Actors.Select(a => a.Position).Distinct().Should().HaveCount(6);
    }

    [Fact]
    public void adjust_can_leave_positions_alone_when_constraint_disabled()
    {
        var field = new MeshField("f", MeshField.LShape());
        var plan = PlanWithActorAt(150, 50);

        LayoutGenerator.Adjust(plan, field, new LayoutOptions { ConstrainToWalkmesh = false });

        plan.Actors[0].Position.Should().NotBe(new Point(0, 0));
        WalkmeshGeometry.From(field)!.Contains(plan.Actors[0].Position).Should().BeFalse();
    }

    [Fact]
    public void validator_warns_for_actor_off_walkmesh_but_inside_bounds()
    {
        var field = new MeshField("f", MeshField.LShape());
        var plan = PlanWithActorAt(150, 50);

        var result = ScenePlanValidator.Validate(plan, field);

        result.HasErrors.Should().BeFalse();
        result.Issues.Should().Contain(i =>
            i.Level == Severity.Warn && i.Path == "actors[0].position" && i.Message.Contains("walkmesh"));
    }

    [Fact]
    public void validator_has_no_walkmesh_warning_for_on_mesh_actor()
    {
        var field = new MeshField("f", MeshField.LShape());

        var result = ScenePlanValidator.Validate(PlanWithActorAt(50, 100), field);

        result.Issues.Should().NotContain(i => i.Message.Contains("walkmesh"));
    }

    [Fact]
    public void validator_warns_for_move_target_off_walkmesh_and_prop_and_spawn()
    {
        var field = new MeshField("f", MeshField.LShape());
        var plan = PlanWithActorAt(50, 100);
        plan.Layout.SpawnPoint = new Point(150, 50);
        plan.Layout.Props.Add(new LayoutProp("crate", 150, 60));
        var ev = new EventDef("e1", "on_enter");
        ev.Steps.Add(new EventStep { Type = EventStepType.Move, ActorId = "cloud", To = new Point(150, 40) });
        plan.Events.Add(ev);

        var result = ScenePlanValidator.Validate(plan, field);

        result.Issues.Should().Contain(i => i.Path == "layout.spawnPoint" && i.Message.Contains("walkmesh"));
        result.Issues.Should().Contain(i => i.Path == "layout.props[0].position" && i.Message.Contains("walkmesh"));
        result.Issues.Should().Contain(i => i.Path == "events[0].steps[0].to" && i.Message.Contains("walkmesh"));
    }

    [Fact]
    public void validator_without_field_keeps_default_rectangle_bounds()
    {
        var result = ScenePlanValidator.Validate(PlanWithActorAt(5000, 5000), field: null);

        result.Issues.Should().Contain(i => i.Message.Contains("out of bounds"));
    }
}

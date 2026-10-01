using FluentAssertions;

using MakouReactor.AI.Layout;
using MakouReactor.AI.Mapping;
using MakouReactor.AI.Validation;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Mapping;

public class ScenePlanMapperWalkmeshTests
{
    private static ScenePlan PlanWithRegion(params Point[] polygon)
    {
        var plan = new ScenePlan();
        plan.Layout.Walkmesh = new WalkmeshPlan
        {
            Regions = { new WalkmeshRegion { Id = "area", Polygon = polygon.ToList() } },
        };
        return plan;
    }

    private static readonly Point[] Square = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)];

    [Fact]
    public void default_mode_ignores_the_proposed_walkmesh_and_preview_says_so()
    {
        var field = FieldPC.CreateEmpty("md1stin");
        var plan = PlanWithRegion(Square);

        var preview = ScenePlanMapper.ApplyToField(plan, field, new ApplyOptions { PreviewOnly = true });
        var apply = ScenePlanMapper.ApplyToField(plan, field, new ApplyOptions { PreviewOnly = false });

        preview.Summary.Should().Contain("walkmesh mode is Ignore");
        apply.Ok.Should().BeTrue(apply.Error);
        field.Walkmesh!.TriangleCount.Should().Be(0);
    }

    [Fact]
    public void preview_with_replace_describes_the_triangle_count_without_changing_the_field()
    {
        var field = FieldPC.CreateEmpty("md1stin");

        var preview = ScenePlanMapper.ApplyToField(PlanWithRegion(Square), field,
            new ApplyOptions { PreviewOnly = true, WalkmeshMode = WalkmeshMode.Replace });

        preview.Summary.Should().Contain("Replace the walkmesh (0 triangle(s)) with 2 triangle(s)");
        field.Walkmesh!.TriangleCount.Should().Be(0);
        field.IsModified.Should().BeFalse();
    }

    [Fact]
    public void replace_writes_triangles_that_survive_save_and_reopen()
    {
        var field = FieldPC.CreateEmpty("md1stin");

        var result = ScenePlanMapper.ApplyToField(PlanWithRegion(Square), field,
            new ApplyOptions { PreviewOnly = false, WalkmeshMode = WalkmeshMode.Replace });

        result.Ok.Should().BeTrue(result.Error);
        result.Summary.Should().Contain("Replaced the walkmesh with 2 walkmesh triangle(s) from 1 region(s).");
        var reopened = FieldPC.OpenCompressed("md1stin", field.SaveCompressed());
        reopened.Walkmesh!.TriangleCount.Should().Be(2);
        WalkmeshGeometry.From(reopened)!.Contains(new Point(50, 50)).Should().BeTrue();
    }

    [Fact]
    public void merge_adds_to_the_existing_walkmesh()
    {
        var field = FieldPC.CreateEmpty("md1stin");
        ScenePlanMapper.ApplyToField(PlanWithRegion(Square), field,
            new ApplyOptions { PreviewOnly = false, WalkmeshMode = WalkmeshMode.Replace });

        var east = Square.Select(p => new Point(p.X + 100, p.Y)).ToArray();
        var result = ScenePlanMapper.ApplyToField(PlanWithRegion(east), field,
            new ApplyOptions { PreviewOnly = false, WalkmeshMode = WalkmeshMode.Merge });

        result.Ok.Should().BeTrue(result.Error);
        field.Walkmesh!.TriangleCount.Should().Be(4);
    }

    [Fact]
    public void invalid_region_fails_before_anything_is_changed()
    {
        var field = FieldPC.CreateEmpty("md1stin");
        var plan = PlanWithRegion(new Point(0, 0), new Point(10, 10));
        plan.Dialog.Add(new DialogLine("narrator", "This must not be added"));
        var textsBefore = field.ScriptsAndTexts!.TextCount;

        var result = ScenePlanMapper.ApplyToField(plan, field,
            new ApplyOptions { PreviewOnly = false, WalkmeshMode = WalkmeshMode.Replace });

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("area");
        field.ScriptsAndTexts!.TextCount.Should().Be(textsBefore);
        field.IsModified.Should().BeFalse();
    }

    [Fact]
    public void describe_changes_lists_what_is_not_written()
    {
        var plan = new ScenePlan();
        plan.Actors.Add(new Actor("cloud", "Cloud"));
        var ev = new EventDef("e", "on_enter");
        ev.Steps.Add(new EventStep { Type = EventStepType.Wait, Ms = 10 });
        ev.Steps.Add(new EventStep { Type = EventStepType.Wait, Ms = 20 });
        plan.Events.Add(ev);
        plan.Layout.Props.Add(new LayoutProp("crate", 1, 1));

        var text = ScenePlanMapper.DescribeChanges(plan, FieldPC.CreateEmpty("md1stin"));

        text.Should().Contain("No dialogue text will be added")
            .And.Contain("Not written to the field (plan JSON only)")
            .And.Contain("1 actor(s)").And.Contain("1 event(s) with 2 step(s)").And.Contain("1 prop(s)");
    }

    [Fact]
    public void validator_reports_bad_regions_and_duplicate_ids()
    {
        var plan = PlanWithRegion(new Point(0, 0), new Point(100, 100), new Point(100, 0), new Point(0, 100));
        plan.Layout.Walkmesh!.Regions.Add(new WalkmeshRegion { Id = "area", Polygon = Square.ToList() });

        var result = ScenePlanValidator.Validate(plan, field: null);

        result.Issues.Should().Contain(i => i.Level == Severity.Error && i.Path == "layout.walkmesh.regions[0].polygon"
                                            && i.Message.Contains("self-intersecting"));
        result.Issues.Should().Contain(i => i.Level == Severity.Error && i.Path == "layout.walkmesh.regions[1].id"
                                            && i.Message.Contains("Duplicate"));
    }

    [Fact]
    public void layout_and_validation_use_the_proposed_walkmesh_for_a_new_area()
    {
        // A new area far outside the default 320x240 box.
        var plan = PlanWithRegion(new Point(1000, 1000), new Point(1400, 1000), new Point(1400, 1300), new Point(1000, 1300));
        plan.Actors.Add(new Actor("cloud", "Cloud", "idle", 1100, 1100));
        plan.Layout.SpawnPoint = new Point(1200, 1200);

        var before = ScenePlanValidator.Validate(plan, field: null);
        var layout = LayoutGenerator.Adjust(plan, field: null);

        before.Issues.Should().NotContain(i => i.Message.Contains("out of bounds"));
        plan.Actors[0].Position.Should().Be(new Point(1100, 1100));
        layout.Notes.Should().NotContain(n => n.Contains("Clamped"));
    }

    [Fact]
    public void layout_moves_actor_outside_the_proposed_walkmesh_onto_it()
    {
        var plan = PlanWithRegion(new Point(0, 0), new Point(100, 0), new Point(100, 100), new Point(0, 100));
        plan.Actors.Add(new Actor("cloud", "Cloud", "idle", 150, 50));

        LayoutGenerator.Adjust(plan, field: null);

        BoundsContain(plan.Actors[0].Position).Should().BeTrue();
        plan.Actors[0].Position.X.Should().BeLessThanOrEqualTo(100);

        static bool BoundsContain(Point p) => p.X >= 0 && p.X <= 100 && p.Y >= 0 && p.Y <= 100;
    }
}

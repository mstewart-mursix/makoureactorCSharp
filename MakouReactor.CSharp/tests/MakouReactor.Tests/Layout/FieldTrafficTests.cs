using FluentAssertions;

using MakouReactor.AI.Layout;
using MakouReactor.AI.Mapping;
using MakouReactor.AI.Prompt;
using MakouReactor.Core.Models;
using MakouReactor.Tests.IO;

using Xunit;

namespace MakouReactor.Tests.Layout;

/// <summary>
/// New actors are solid, so they must not stand where the field's own characters walk: found in game
/// on md1stin, where two generated guards on the platform stopped the opening scene.
/// </summary>
public sealed class FieldTrafficTests
{
    /// <summary>
    /// A corridor <paramref name="width"/> wide and 1000 long (x 0..width, y 0..1000) with an NPC
    /// "walker" that starts at (width/2, <paramref name="from"/>) and walks to (width/2,
    /// <paramref name="to"/>), plus its model entry.
    /// </summary>
    private static FieldPC Corridor(short width, short from = 900, short to = 100)
    {
        var field = FieldPC.CreateEmpty("corridor");
        var mesh = new IdFile();
        mesh.Triangles.Add(new Triangle(V(0, 0), V(0, 1000), V(width, 1000)));
        mesh.Triangles.Add(new Triangle(V(0, 0), V(width, 1000), V(width, 0)));
        mesh.Access.Add(new Access(-1, 1, -1));
        mesh.Access.Add(new Access(0, -1, -1));
        field.ReplaceWalkmesh(mesh);

        var mid = (short)(width / 2);
        var walker = new GrpScript("walker");
        walker.SetScript(0, new Script(new ScriptBuilder()
            .Char(0).Xyzi(mid, from, 0, 0).Ret().Compile()));
        walker.SetScript(1, new Script(new ScriptBuilder().Ret().Compile()));
        walker.SetScript(2, new Script(new ScriptBuilder().Move(mid, to).Ret().Compile()));
        walker.DetectType();
        field.ScriptsAndTexts!.InsertGrpScript(field.ScriptsAndTexts.GrpScriptCount, walker);

        var model = FieldModelLoaderPC.Open(FieldModelLoaderPCTests.BuildModelLoader()).Models[0];
        field.ModelLoader!.Models.Add(model with { Id = 0 });
        field.ApplyModelLoaderChanges();
        return FieldPC.OpenDecompressed("corridor", field.SaveDecompressed());
    }

    private static VertexSR V(short x, short y) => new(x, y, 0, 0);

    private static ScenePlan PlanWithActorAt(int x, int y)
    {
        var plan = new ScenePlan();
        plan.Actors.Add(new Actor("guard", "Guard", x: x, y: y));
        plan.Layout.SpawnPoint = new Point(x, 990);
        return plan;
    }

    [Fact]
    public void traffic_follows_an_entity_from_its_start_to_its_move_target()
    {
        var traffic = FieldTraffic.Of(Corridor(400), Corridor(400).Walkmesh);

        traffic.Segments.Should().Contain((new Point(200, 900), new Point(200, 100)));
        traffic.DistanceToTraffic(new Point(200, 500)).Should().Be(0);
        traffic.DistanceToTraffic(new Point(300, 500)).Should().Be(100);
    }

    [Fact]
    public void layout_moves_an_actor_off_a_scripted_path_and_keeps_the_way_open()
    {
        var field = Corridor(400);
        var plan = PlanWithActorAt(200, 500); // right on the walker's path

        var layout = LayoutGenerator.Adjust(plan, field);

        var traffic = FieldTraffic.Of(field, field.Walkmesh);
        var at = plan.Actors[0].Position;
        traffic.DistanceToTraffic(at).Should().BeGreaterThanOrEqualTo(FieldTraffic.DefaultClearance);
        traffic.IsClear(at, []).Should().BeTrue();
        WalkmeshGeometry.Contains(field.Walkmesh!, at).Should().BeTrue();
        layout.Notes.Should().Contain(n => n.Contains("'guard'") && n.Contains("scripted paths"));

        var result = ScenePlanMapper.ApplyToField(plan, field, new ApplyOptions { PreviewOnly = true });
        result.Warnings.Should().NotContain(w => w.Contains("non-solid"));
        result.Groups.Single(g => g.Name == "guard").Scripts[0].Opcodes.Should().NotContain("SOLID");
    }

    [Fact]
    public void a_move_target_on_a_scripted_path_is_moved_too()
    {
        // In game an actor that "runs over" into a walkway blocks it as surely as one placed there.
        var field = Corridor(400);
        var plan = PlanWithActorAt(20, 500);
        var evt = new EventDef("e", "on_enter");
        evt.Steps.Add(new EventStep { Type = EventStepType.Move, ActorId = "guard", To = new Point(200, 400) });
        plan.Events.Add(evt);

        var layout = LayoutGenerator.Adjust(plan, field);

        var to = plan.Events[0].Steps[0].To;
        FieldTraffic.Of(field, field.Walkmesh).DistanceToTraffic(to).Should().BeGreaterThanOrEqualTo(FieldTraffic.DefaultClearance);
        layout.Notes.Should().Contain(n => n.Contains("move target of 'guard'"));
        var result = ScenePlanMapper.ApplyToField(plan, field, new ApplyOptions { PreviewOnly = true });
        result.Warnings.Should().NotContain(w => w.Contains("non-solid"));
    }

    [Fact]
    public void an_actor_walking_into_traffic_without_layout_is_made_non_solid()
    {
        var field = Corridor(400);
        var plan = PlanWithActorAt(20, 500);
        var evt = new EventDef("e", "on_enter");
        evt.Steps.Add(new EventStep { Type = EventStepType.Move, ActorId = "guard", To = new Point(200, 400) });
        plan.Events.Add(evt);

        var result = ScenePlanMapper.ApplyToField(plan, field, new ApplyOptions { PreviewOnly = true });

        result.Warnings.Should().Contain(w => w.Contains("walks to 200,400") && w.Contains("non-solid"));
        result.Groups.Single(g => g.Name == "guard").Scripts[0].Opcodes.Should().Contain("SOLID");
    }

    [Fact]
    public void an_actor_away_from_traffic_is_left_alone()
    {
        var field = Corridor(400);
        var plan = PlanWithActorAt(20, 500);

        LayoutGenerator.Adjust(plan, field);

        plan.Actors[0].Position.Should().Be(new Point(20, 500));
    }

    [Fact]
    public void in_a_corridor_too_narrow_to_pass_the_actor_is_made_non_solid()
    {
        // The walker crosses the whole corridor, so nowhere is 64 units from its path.
        var field = Corridor(100, from: 1000, to: 0);
        var plan = PlanWithActorAt(50, 500);

        var layout = LayoutGenerator.Adjust(plan, field);
        var result = ScenePlanMapper.ApplyToField(plan, field, new ApplyOptions { PreviewOnly = false });

        layout.Notes.Should().Contain(n => n.Contains("non-solid"));
        result.Ok.Should().BeTrue(result.Error);
        result.Warnings.Should().Contain(w => w.Contains("'guard'") && w.Contains("non-solid"));
        var init = field.ScriptsAndTexts!.GrpScripts.Single(g => g.Name == "guard").Scripts[0];
        init.RawOpcodes.Should().Contain(op => op.Name == "SOLID" && op.Bytes.SequenceEqual(new byte[] { 0xC7, 0x01 }));
    }

    [Fact]
    public void an_actor_near_the_path_is_moved_to_a_spot_that_keeps_both_clearance_and_a_way_through()
    {
        // 220 wide, path at x = 110: x = 150 is only 40 from it. Clear spots exist beside the
        // 64-unit band (x >= 174) and still leave the left half of the corridor open.
        var field = Corridor(220);
        var plan = PlanWithActorAt(150, 500);

        LayoutGenerator.Adjust(plan, field);

        var traffic = FieldTraffic.Of(field, field.Walkmesh);
        traffic.IsClear(plan.Actors[0].Position, []).Should().BeTrue();
    }

    [Fact]
    public void layout_option_can_turn_the_check_off()
    {
        var field = Corridor(400);
        var plan = PlanWithActorAt(200, 500);

        LayoutGenerator.Adjust(plan, field, new LayoutOptions { KeepTrafficClear = false });

        plan.Actors[0].Position.Should().Be(new Point(200, 500));
    }

    [Fact]
    public void field_context_lists_the_paths_to_keep_clear()
    {
        FieldContextBuilder.Describe(Corridor(400)).Should()
            .Contain("Paths existing characters walk")
            .And.Contain("(200,900) -> (200,100)");
    }
}

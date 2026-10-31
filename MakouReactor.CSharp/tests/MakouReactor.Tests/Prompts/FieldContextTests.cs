using System.Buffers.Binary;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using FluentAssertions;

using MakouReactor.AI;
using MakouReactor.AI.Backends;
using MakouReactor.AI.Layout;
using MakouReactor.AI.Parsing;
using MakouReactor.AI.Prompt;
using MakouReactor.AI.Validation;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Prompts;

/// <summary>Synthetic field with an L-shaped walkmesh, groups, dialog and one exit.</summary>
internal sealed class ContextField : Field
{
    public ContextField(string name = "testmap", bool withWalkmesh = true, Section1File? scripts = null, InfFile? inf = null)
        : base(name)
    {
        if (withWalkmesh)
            Walkmesh = LShapedWalkmesh();
        ScriptsAndTexts = scripts;
        Inf = inf;
    }

    /// <summary>
    /// Walkable: the strip X 0..1000, Y 0..400 plus the column X 0..400, Y 400..1000.
    /// The square X 400..1000, Y 400..1000 is blocked.
    /// </summary>
    public static IdFile LShapedWalkmesh()
    {
        static VertexSR V(short x, short y) => new(x, y, 0, 0);
        var id = new IdFile();
        id.Triangles.Add(new Triangle(V(0, 0), V(1000, 0), V(1000, 400)));
        id.Triangles.Add(new Triangle(V(0, 0), V(1000, 400), V(0, 400)));
        id.Triangles.Add(new Triangle(V(0, 400), V(400, 400), V(400, 1000)));
        id.Triangles.Add(new Triangle(V(0, 400), V(400, 1000), V(0, 1000)));
        return id;
    }
}

public class FieldContextTests
{
    private static readonly Point Blocked = new(800, 800);

    private static Section1File Scripts(int textCount = 2)
    {
        var s = new Section1File();
        s.GrpScripts.Add(new GrpScript("director") { Type = GrpScriptType.Director });
        s.GrpScripts.Add(new GrpScript("barret") { Type = GrpScriptType.Model });
        s.GrpScripts.Add(new GrpScript("door1") { Type = GrpScriptType.Location });
        for (var i = 0; i < textCount; i++)
            s.Texts.Add(new FF7String(i == 0 ? "Barret\n\"Move it,\nspiky!\"" : $"Line number {i}"));
        return s;
    }

    /// <summary>International-size INF with one real exit (to field 123) and the rest unused.</summary>
    private static InfFile InfWithExit()
    {
        var data = new byte[740];
        void W16(int offset, int value) => BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset, 2), (short)value);
        for (var i = 0; i < 12; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(56 + i * 24 + 18, 2), 0x7FFF);

        var door = 56;
        W16(door + 0, 100); W16(door + 2, 0);    // line A
        W16(door + 6, 300); W16(door + 8, 0);    // line B
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(door + 18, 2), 123);
        return InfFile.Open(data);
    }

    private static ScenePlan Parse(string json)
    {
        var parsed = ScenePlanParser.Parse(Encoding.UTF8.GetBytes(json));
        parsed.Ok.Should().BeTrue(parsed.Error);
        return parsed.Plan;
    }

    private static string PlanJson(int actorX, int actorY, int moveX = 100, int moveY = 100) =>
        "{\"meta\":{\"title\":\"T\",\"model\":\"m\",\"version\":\"1.0\"}," +
        $"\"actors\":[{{\"id\":\"cloud\",\"position\":{{\"x\":{actorX},\"y\":{actorY}}}}}]," +
        "\"events\":[{\"id\":\"e1\",\"trigger\":\"on_enter\",\"steps\":[" +
        $"{{\"type\":\"move\",\"actorId\":\"cloud\",\"to\":{{\"x\":{moveX},\"y\":{moveY}}}}}]}}]," +
        "\"layout\":{\"spawnPoint\":{\"x\":200,\"y\":200}}}";

    // -----------------------------------------------------------------------
    // WalkmeshGeometry
    // -----------------------------------------------------------------------

    [Fact]
    public void geometry_point_in_walkmesh()
    {
        var mesh = ContextField.LShapedWalkmesh();

        WalkmeshGeometry.Contains(mesh, new Point(500, 200)).Should().BeTrue();
        WalkmeshGeometry.Contains(mesh, new Point(200, 900)).Should().BeTrue();
        WalkmeshGeometry.Contains(mesh, new Point(1000, 0)).Should().BeTrue("vertices and edges count as walkable");
        WalkmeshGeometry.Contains(mesh, Blocked).Should().BeFalse();
        WalkmeshGeometry.Contains(mesh, new Point(-1, 200)).Should().BeFalse();
    }

    [Fact]
    public void geometry_bounds_and_nearest_walkable()
    {
        var mesh = ContextField.LShapedWalkmesh();

        var bounds = WalkmeshGeometry.Bounds(mesh)!.Value;
        bounds.Should().Be(new Rect(0, 0, 1001, 1001));
        bounds.Contains(new Point(1000, 1000)).Should().BeTrue();

        var near = WalkmeshGeometry.NearestWalkable(mesh, Blocked);
        WalkmeshGeometry.Contains(mesh, near).Should().BeTrue();
        var dist = Math.Sqrt(Math.Pow(near.X - Blocked.X, 2) + Math.Pow(near.Y - Blocked.Y, 2));
        dist.Should().BeLessThan(402, "the nearest walkable edge is 400 units away");

        WalkmeshGeometry.NearestWalkable(mesh, new Point(10, 10)).Should().Be(new Point(10, 10));
        WalkmeshGeometry.Bounds(null).Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // FieldContextBuilder
    // -----------------------------------------------------------------------

    [Fact]
    public void context_describes_walkmesh_with_occupancy_grid()
    {
        var text = FieldContextBuilder.Describe(new ContextField());

        text.Should().StartWith("Field: testmap");
        text.Should().Contain("4 triangles; X 0..1000, Y 0..1000");

        var gridRows = text.Split('\n')
            .Where(l => l.StartsWith("  ") && l.Trim().Length == FieldContextBuilder.GridColumns
                        && l.Trim().All(c => c is '#' or '.'))
            .Select(l => l.Trim())
            .ToList();
        gridRows.Should().HaveCount(FieldContextBuilder.GridRows);
        gridRows[0].Should().Be(new string('#', FieldContextBuilder.GridColumns), "row 0 is the fully walkable strip");
        gridRows[^1].Should().StartWith("#").And.EndWith(".", "bottom-right is blocked");
    }

    [Fact]
    public void context_lists_groups_exits_and_flattened_dialog()
    {
        var text = FieldContextBuilder.Describe(new ContextField(scripts: Scripts(), inf: InfWithExit()));

        text.Should().Contain("Existing groups (do not reuse these names):");
        text.Should().Contain("barret (character/NPC)");
        text.Should().Contain("director");
        text.Should().Contain("to field #123 across (100,0)-(300,0)");
        text.Should().NotContain("#32767");
        text.Should().Contain("- \"Barret 'Move it, spiky!'\"");
    }

    [Fact]
    public void context_is_deterministic_and_size_capped()
    {
        var field = new ContextField(scripts: Scripts(textCount: 200), inf: InfWithExit());

        var a = FieldContextBuilder.Describe(field);
        a.Should().Be(FieldContextBuilder.Describe(field));
        a.Length.Should().BeLessThanOrEqualTo(4000);
        a.Split('\n').Count(l => l.StartsWith("- \"")).Should().BeLessThanOrEqualTo(FieldContextBuilder.MaxDialogLines);

        var small = FieldContextBuilder.Describe(field, maxChars: 300);
        small.Length.Should().BeLessThanOrEqualTo(300);
        small.Should().StartWith("Field: testmap");
    }

    [Fact]
    public void context_without_walkmesh_says_so()
    {
        var text = FieldContextBuilder.Describe(new ContextField(withWalkmesh: false));

        text.Should().Contain("Walkmesh: none available");
    }

    // -----------------------------------------------------------------------
    // Prompt + request
    // -----------------------------------------------------------------------

    [Fact]
    public void prompt_includes_field_context_only_when_provided()
    {
        var prompt = new ScenePrompt { UserText = "Barret blocks the way" };

        var without = PromptBuilder.UserPrompt(prompt, 320, 240);
        var with = PromptBuilder.UserPrompt(prompt, 320, 240, "Field: testmap");

        without.Should().NotContain("Field Context:");
        with.Should().Contain("Field Context:\n" + PromptBuilder.FieldContextInstruction);
        with.Should().Contain("Field: testmap");
        with.IndexOf("Field Context:").Should().BeLessThan(with.IndexOf("JSON Schema"));
    }

    [Fact]
    public void build_request_uses_field_context_and_walkmesh_size()
    {
        var service = new SceneGenerationService(new LLMConfig(), new NoopBackend());
        var prompt = new ScenePrompt { UserText = "Barret blocks the way" };

        var request = service.BuildRequest(prompt, field: new ContextField(scripts: Scripts()));

        request.UserPrompt.Should().Contain("Field Context:");
        request.UserPrompt.Should().Contain("barret (character/NPC)");
        request.UserPrompt.Should().Contain("width=1001, height=1001");

        service.BuildRequest(prompt).UserPrompt.Should().NotContain("Field Context:");
    }

    // -----------------------------------------------------------------------
    // Layout + validation against the walkmesh
    // -----------------------------------------------------------------------

    [Fact]
    public void field_bounds_follow_walkmesh()
    {
        LayoutGenerator.FieldBounds(new ContextField()).Should().Be(new Rect(0, 0, 1001, 1001));
        LayoutGenerator.FieldBounds(new ContextField(withWalkmesh: false)).Should().Be(LayoutGenerator.DefaultBounds);
    }

    [Fact]
    public void layout_moves_off_mesh_points_onto_walkmesh()
    {
        var field = new ContextField();
        var plan = Parse(PlanJson(Blocked.X, Blocked.Y));
        plan.Layout.SpawnPoint = new Point(900, 900);

        var result = LayoutGenerator.Adjust(plan, field);

        WalkmeshGeometry.Contains(field.Walkmesh!, plan.Actors[0].Position).Should().BeTrue();
        WalkmeshGeometry.Contains(field.Walkmesh!, plan.Layout.SpawnPoint).Should().BeTrue();
        result.Notes.Should().Contain(n => n.Contains("actor 'cloud'") && n.Contains("onto walkmesh"));
    }

    [Fact]
    public void layout_creates_default_spawn_on_walkmesh()
    {
        var field = new ContextField();
        var plan = Parse("{\"meta\":{\"title\":\"T\",\"model\":\"m\",\"version\":\"1.0\"}}");
        plan.Layout = null!;

        LayoutGenerator.Adjust(plan, field);

        WalkmeshGeometry.Contains(field.Walkmesh!, plan.Layout.SpawnPoint).Should().BeTrue();
    }

    [Fact]
    public void validator_reports_off_mesh_actor_as_error_with_nearest_point()
    {
        var plan = Parse(PlanJson(Blocked.X, Blocked.Y));

        var result = ScenePlanValidator.Validate(plan, new ContextField());

        result.Issues.Should().ContainSingle(i => i.Path == "actors[0].position")
            .Which.Should().Match<Issue>(i => i.Level == Severity.Error
                                              && i.Message.Contains("not on the walkmesh")
                                              && i.Message.Contains("nearest walkable point"));
    }

    [Fact]
    public void validator_checks_move_targets_only_with_walkmesh()
    {
        var plan = Parse(PlanJson(100, 100, moveX: Blocked.X, moveY: Blocked.Y));

        ScenePlanValidator.Validate(plan, new ContextField()).Issues
            .Should().ContainSingle(i => i.Path == "events[0].steps[0].to" && i.Level == Severity.Error);

        ScenePlanValidator.Validate(plan, null).Issues
            .Should().NotContain(i => i.Path == "events[0].steps[0].to", "screen-space plans keep their old behaviour");
    }

    [Fact]
    public void on_mesh_plan_has_no_placement_errors()
    {
        var plan = Parse(PlanJson(500, 200, moveX: 200, moveY: 900));

        ScenePlanValidator.Validate(plan, new ContextField()).HasErrors.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // End to end: walkmesh errors feed the Phase 3 repair loop
    // -----------------------------------------------------------------------

    [Fact]
    public async Task off_mesh_move_target_is_repaired_via_repair_loop()
    {
        var bad = PlanJson(100, 100, moveX: Blocked.X, moveY: Blocked.Y);
        var good = PlanJson(100, 100, moveX: 200, moveY: 900);
        var backend = new SequenceBackend(bad, good);
        var service = new SceneGenerationService(new LLMConfig { MaxRepairAttempts = 1 }, backend);

        var result = await service.GenerateAsync(new ScenePrompt { UserText = "x" }, new ContextField());

        result.Ok.Should().BeTrue();
        result.Validation!.HasErrors.Should().BeFalse();
        result.RepairCount.Should().Be(1);
        backend.Prompts[0].Should().Contain("Field Context:");
        backend.Prompts[1].Should().Contain("events[0].steps[0].to: Move target (800,800) is not on the walkmesh");
    }

    private sealed class NoopBackend : ILLMBackend
    {
        public Task<LLMRawResult> RequestScenePlanAsync(LLMRequest req) =>
            Task.FromResult(LLMRawResult.Failure(0, "unused"));
        public void CancelAll() { }
        public void Dispose() { }
    }

    private sealed class SequenceBackend : ILLMBackend
    {
        private readonly string[] _responses;
        public List<string> Prompts { get; } = new();

        public SequenceBackend(params string[] responses) => _responses = responses;

        public Task<LLMRawResult> RequestScenePlanAsync(LLMRequest req)
        {
            var text = _responses[Math.Min(Prompts.Count, _responses.Length - 1)];
            Prompts.Add(req.UserPrompt);
            var bytes = Encoding.UTF8.GetBytes(text);
            return Task.FromResult(new LLMRawResult { Ok = true, Raw = bytes, Json = JsonDocument.Parse(bytes) });
        }

        public void CancelAll() { }
        public void Dispose() { }
    }
}

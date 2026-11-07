using FluentAssertions;

using MakouReactor.AI.Mapping;
using MakouReactor.AI.Parsing;
using MakouReactor.AI.Prompt;
using MakouReactor.Core.Models;
using MakouReactor.Tests.IO;

using Xunit;

namespace MakouReactor.Tests.Mapping;

/// <summary>
/// New actors get a Model Loader entry copied from a model the field already loads, and party
/// members the field already has are reused instead of duplicated.
/// </summary>
public sealed class ScenePlanMapperModelTests
{
    /// <summary>
    /// A field shaped like the shipped ones: "cl" is playable Cloud (CHAR 0, PC 0), "gu0" and
    /// "hei0" are NPCs (CHAR 1 and 2), and the Model Loader has one entry per model group.
    /// </summary>
    private static FieldPC BuildField()
    {
        var field = FieldPC.CreateEmpty("testmap");
        var section = field.ScriptsAndTexts!;
        section.InsertGrpScript(0, Group("cl", 0xA1, 0x00, 0xA0, 0x00, 0x00));
        section.InsertGrpScript(1, Group("gu0", 0xA1, 0x01, 0x00));
        section.InsertGrpScript(2, Group("hei0", 0xA1, 0x02, 0x00));

        var template = FieldModelLoaderPC.Open(FieldModelLoaderPCTests.BuildModelLoader()).Models[0];
        field.ModelLoader!.Models.Add(template with { Id = 0, HrcName = "AAAA" });
        field.ModelLoader.Models.Add(template with { Id = 1, HrcName = "GUAR" });
        field.ModelLoader.Models.Add(template with { Id = 2, HrcName = "HEII" });
        field.ApplyModelLoaderChanges();

        // Start from saved bytes so the test exercises a field as it is read from disk.
        return FieldPC.OpenDecompressed("testmap", field.SaveDecompressed());
    }

    private static GrpScript Group(string name, params byte[] init)
    {
        var group = new GrpScript(name);
        group.SetScript(0, new Script(init));
        group.SetScript(1, new Script([0x00]));
        group.DetectType();
        return group;
    }

    private static ScenePlan Plan(params Actor[] actors)
    {
        var plan = new ScenePlan();
        plan.Actors.AddRange(actors);
        return plan;
    }

    [Fact]
    public void new_actor_copies_the_model_it_names_and_lines_up_with_its_char_id()
    {
        var field = BuildField();
        var plan = Plan(new Actor("guard_a", "Guard", x: 10, y: 10) { Model = "hei0" });

        var result = ScenePlanMapper.ApplyToField(plan, field, new ApplyOptions { PreviewOnly = false });

        result.Ok.Should().BeTrue(result.Error);
        result.Warnings.Should().NotContain(w => w.Contains("Model Loader"));
        result.ModelsAdded.Should().ContainSingle().Which.HrcName.Should().Be("HEII");

        var reopened = FieldPC.OpenDecompressed("testmap", field.SaveDecompressed());
        var models = reopened.ModelLoader!.Models;
        models.Select(m => m.HrcName).Should().Equal("AAAA", "GUAR", "HEII", "HEII");
        var section = reopened.ScriptsAndTexts!;
        section.GrpScripts.Count(g => g.Type == GrpScriptType.Model).Should().Be(models.Count,
            "every model group needs its own Model Loader entry");

        var actorGroup = section.GrpScripts.Single(g => g.Name == "guard_a");
        actorGroup.Scripts[0].RawOpcodes[0].Bytes.Should().Equal(0xA1, 0x03); // CHAR 3 = the new entry
    }

    [Fact]
    public void actor_without_a_model_copies_the_first_npc_model_not_the_player()
    {
        var field = BuildField();

        var result = ScenePlanMapper.ApplyToField(Plan(new Actor("merchant", "Merchant")), field,
            new ApplyOptions { PreviewOnly = false });

        result.Ok.Should().BeTrue(result.Error);
        result.ModelsAdded.Select(m => m.HrcName).Should().Equal("GUAR");
        result.Summary.Should().Contain("Added 1 Model Loader entry");
    }

    [Fact]
    public void unknown_model_name_falls_back_with_a_warning()
    {
        var field = BuildField();

        var result = ScenePlanMapper.ApplyToField(Plan(new Actor("npc", "Npc") { Model = "nosuch" }), field,
            new ApplyOptions { PreviewOnly = true });

        result.Ok.Should().BeTrue(result.Error);
        result.Warnings.Should().Contain(w => w.Contains("'nosuch'") && w.Contains("gu0"));
        result.ModelsAdded.Select(m => m.HrcName).Should().Equal("GUAR");
    }

    [Fact]
    public void party_member_the_field_already_has_is_reused_not_duplicated()
    {
        var field = BuildField();
        var plan = Plan(new Actor("cloud", "Cloud", x: 10, y: 10), new Actor("guard", "Guard", x: 40, y: 40) { Model = "gu0" });
        var evt = new EventDef("intro", "on_enter");
        evt.Steps.Add(new EventStep { Type = EventStepType.Say, ActorId = "cloud", Text = "Let's go." });
        evt.Steps.Add(new EventStep { Type = EventStepType.Face, ActorId = "cloud", Dir = 'N' });
        plan.Events.Add(evt);

        var result = ScenePlanMapper.ApplyToField(plan, field, new ApplyOptions { PreviewOnly = false });

        result.Ok.Should().BeTrue(result.Error);
        field.ScriptsAndTexts!.GrpScripts.Select(g => g.Name).Should().Equal("cl", "gu0", "hei0", "dic", "LLMScene", "guard");
        result.ModelsAdded.Select(m => m.HrcName).Should().Equal("GUAR");
        result.Notes.Should().Contain(n => n.Contains("'cloud'") && n.Contains("\"cl\""));
        result.Warnings.Should().Contain(w => w.Contains("player's character"));
        result.TextsAdded.Select(t => t.Text).Should().Contain("Let's go.");
    }

    [Fact]
    public void preview_does_not_touch_the_model_loader()
    {
        var field = BuildField();

        var result = ScenePlanMapper.ApplyToField(Plan(new Actor("npc", "Npc")), field, new ApplyOptions { PreviewOnly = true });

        result.ModelsAdded.Should().ContainSingle();
        result.Summary.Should().Contain("Would add 1 Model Loader entry");
        field.ModelLoader!.ModelCount.Should().Be(3);
        field.IsModified.Should().BeFalse();
    }

    [Fact]
    public void parser_reads_the_actor_model()
    {
        var parsed = ScenePlanParser.Parse(System.Text.Encoding.UTF8.GetBytes(
            "{\"meta\":{\"title\":\"t\",\"model\":\"m\",\"version\":\"1.0\"}," +
            "\"actors\":[{\"id\":\"g\",\"model\":\"gu0\",\"position\":{\"x\":1,\"y\":2}}]}"));

        parsed.Ok.Should().BeTrue(parsed.Error);
        parsed.Plan.Actors[0].Model.Should().Be("gu0");
    }

    [Fact]
    public void field_context_marks_playable_characters_and_lists_reusable_models()
    {
        var context = FieldContextBuilder.Describe(BuildField());

        context.Should().Contain("cl (playable: Cloud)")
            .And.Contain("gu0 (character/NPC)")
            .And.Contain("Models new actors can reuse (actors[].model): gu0, hei0");
    }
}

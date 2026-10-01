using FluentAssertions;

using MakouReactor.AI.Mapping;
using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Mapping;

public class ScenePlanMapperScriptTests
{
    private static ScenePlan Plan()
    {
        var plan = new ScenePlan();
        plan.Actors.Add(new Actor("narrator", "Narrator"));
        plan.Dialog.Add(new DialogLine("narrator", "Standalone dialog line"));
        var ev = new EventDef("intro", "on_enter");
        ev.Steps.Add(new EventStep { Type = EventStepType.Say, ActorId = "narrator", Text = "Welcome to Sector 7." });
        ev.Steps.Add(new EventStep { Type = EventStepType.Wait, Ms = 500 });
        ev.Steps.Add(new EventStep { Type = EventStepType.Say, ActorId = "narrator", Text = "Stay close." });
        plan.Events.Add(ev);
        return plan;
    }

    private static ApplyOptions Apply(ScriptMode mode, string? groupName = null) =>
        new() { PreviewOnly = false, ScriptMode = mode, GroupNameOverride = groupName };

    [Fact]
    public void default_script_mode_adds_texts_but_no_group()
    {
        var field = FieldPC.CreateEmpty("md1stin");
        var groupsBefore = field.ScriptsAndTexts!.GrpScriptCount;

        var result = ScenePlanMapper.ApplyToField(Plan(), field, Apply(ScriptMode.None));

        result.Ok.Should().BeTrue(result.Error);
        field.ScriptsAndTexts!.GrpScriptCount.Should().Be(groupsBefore);
        field.ScriptsAndTexts.TextCount.Should().Be(2 + 3);
    }

    [Fact]
    public void append_group_writes_script_that_references_the_inserted_texts()
    {
        var field = FieldPC.CreateEmpty("md1stin");
        var textsBefore = field.ScriptsAndTexts!.TextCount; // "Map name", "Hello world!"

        var result = ScenePlanMapper.ApplyToField(Plan(), field, Apply(ScriptMode.AppendGroup));

        result.Ok.Should().BeTrue(result.Error);
        result.GroupName.Should().Be("ai_01");
        result.Summary.Should().Contain("Added script group 'ai_01' with 3 step(s)");

        var reopened = FieldPC.OpenCompressed("md1stin", field.SaveCompressed());
        var group = reopened.ScriptsAndTexts!.GrpScripts.Last();
        group.Name.Should().Be("ai_01");

        var opcodes = RawOpcodeReader.Read(group.Scripts[0].Compile().ToArray());
        opcodes.Select(o => o.Name).Should().Equal("RET", "WINDOW", "MESSAGE", "WAIT", "WINDOW", "MESSAGE", "RET");
        var messages = opcodes.Where(o => o.Name == "MESSAGE").Select(o => reopened.ScriptsAndTexts.Texts[o.Bytes[2]].Value).ToArray();
        messages.Should().Equal("Welcome to Sector 7.", "Stay close.");
        reopened.ScriptsAndTexts.Texts.Count.Should().Be(textsBefore + 3);
    }

    [Fact]
    public void the_script_group_survives_unmodified_reload_and_resave()
    {
        var field = FieldPC.CreateEmpty("md1stin");
        ScenePlanMapper.ApplyToField(Plan(), field, Apply(ScriptMode.AppendGroup));
        var firstBytes = field.SaveCompressed();

        var reopened = FieldPC.OpenCompressed("md1stin", firstBytes);

        reopened.SaveCompressed().Should().Equal(firstBytes, "an unmodified reopened field saves byte-identically");
    }

    [Fact]
    public void group_names_stay_unique_across_repeated_applies()
    {
        var field = FieldPC.CreateEmpty("md1stin");

        var first = ScenePlanMapper.ApplyToField(Plan(), field, Apply(ScriptMode.AppendGroup));
        var second = ScenePlanMapper.ApplyToField(Plan(), field, Apply(ScriptMode.AppendGroup));

        first.GroupName.Should().Be("ai_01");
        second.GroupName.Should().Be("ai_02");
        field.ScriptsAndTexts!.GrpScripts.Select(g => g.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void a_preferred_group_name_is_used_when_free_and_truncated_to_eight_characters()
    {
        var field = FieldPC.CreateEmpty("md1stin");

        var result = ScenePlanMapper.ApplyToField(Plan(), field, Apply(ScriptMode.AppendGroup, "MyScene_Long"));

        result.GroupName.Should().Be("MyScene_");
    }

    [Fact]
    public void a_plan_with_only_unsupported_events_adds_no_group_but_reports_skips()
    {
        var plan = new ScenePlan();
        var ev = new EventDef("talk", "on_interact");
        ev.Steps.Add(new EventStep { Type = EventStepType.Wait, Ms = 100 });
        plan.Events.Add(ev);
        var field = FieldPC.CreateEmpty("md1stin");
        var groups = field.ScriptsAndTexts!.GrpScriptCount;

        var result = ScenePlanMapper.ApplyToField(plan, field, Apply(ScriptMode.AppendGroup));

        result.Ok.Should().BeTrue(result.Error);
        field.ScriptsAndTexts!.GrpScriptCount.Should().Be(groups);
        result.Summary.Should().Contain("skipped: event 'talk' (on_interact)");
    }

    [Fact]
    public void preview_describes_the_group_and_skips_without_changing_the_field()
    {
        var plan = Plan();
        plan.Events[0].Steps.Add(new EventStep { Type = EventStepType.Move, ActorId = "narrator" });
        var field = FieldPC.CreateEmpty("md1stin");

        var preview = ScenePlanMapper.ApplyToField(plan, field,
            new ApplyOptions { PreviewOnly = true, ScriptMode = ScriptMode.AppendGroup });

        preview.Summary.Should().Contain("Add a script group with 3 step(s)")
            .And.Contain("skipped: event 'intro' step 3 (move)");
        field.IsModified.Should().BeFalse();
        field.ScriptsAndTexts!.GrpScriptCount.Should().Be(FieldPC.CreateEmpty("x").ScriptsAndTexts!.GrpScriptCount);
    }

    [Fact]
    public void preview_with_script_mode_off_says_events_are_not_written()
    {
        var preview = ScenePlanMapper.ApplyToField(Plan(), FieldPC.CreateEmpty("md1stin"),
            new ApplyOptions { PreviewOnly = true });

        preview.Summary.Should().Contain("1 event(s) with 3 step(s) (script mode is Off)");
    }

    [Fact]
    public void exceeding_the_group_limit_fails_before_any_change()
    {
        var field = FieldPC.CreateEmpty("md1stin");
        while (field.ScriptsAndTexts!.GrpScripts.Count < Section1File.MaxGrpScriptCount)
            field.ScriptsAndTexts.AppendGrpScript(new GrpScript($"g{field.ScriptsAndTexts.GrpScripts.Count}"));
        var textsBefore = field.ScriptsAndTexts.TextCount;

        var result = ScenePlanMapper.ApplyToField(Plan(), field, Apply(ScriptMode.AppendGroup));

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("maximum");
        field.ScriptsAndTexts.TextCount.Should().Be(textsBefore);
    }

    [Fact]
    public void applier_writes_a_script_group_into_an_archive_with_backup()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mr_script_{Guid.NewGuid():N}.lgp");
        LgpArchive.Create(path, [new LgpArchiveFile("md1stin", FieldPC.CreateEmpty("md1stin").SaveCompressed())]);
        try
        {
            var outcome = ScenePlanApplier.ApplyToArchive(path, "md1stin", Plan(), dryRun: false,
                scriptMode: ScriptMode.AppendGroup);

            outcome.Ok.Should().BeTrue(outcome.Error);
            outcome.Write.Should().NotBeNull();
            var field = FieldArchive.Open(path).OpenField("md1stin");
            field.ScriptsAndTexts!.GrpScripts.Select(g => g.Name).Should().Contain("ai_01");
        }
        finally
        {
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*"))
                File.Delete(file);
        }
    }
}

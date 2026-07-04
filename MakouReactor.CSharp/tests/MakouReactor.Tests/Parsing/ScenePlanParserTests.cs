using System.IO;
using System.Text.Json;

using FluentAssertions;

using MakouReactor.AI.Parsing;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Parsing;

public class ScenePlanParserTests
{
    private static byte[] LoadFixture(string name)
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", name);
        return File.ReadAllBytes(path);
    }

    // --- Successful parsing ---

    [Fact]
    public void parse_valid_full_plan()
    {
        var result = ScenePlanParser.Parse(LoadFixture("plan_valid_full.json"));

        result.Ok.Should().BeTrue();
        result.Plan.Should().NotBeNull();
        result.Plan.Meta.Title.Should().Be("Test Scene");
        result.Plan.Meta.Model.Should().Be("llama3");
        result.Plan.Meta.Version.Should().Be("1.0");
        result.Plan.Actors.Should().HaveCount(2);
        result.Plan.Actors[0].Id.Should().Be("cloud");
        result.Plan.Actors[1].Id.Should().Be("tifa");
        result.Plan.Dialog.Should().HaveCount(2);
        result.Plan.Events.Should().HaveCount(1);
        result.Plan.Layout.SpawnPoint.X.Should().Be(90);
        result.Plan.Layout.Props.Should().HaveCount(1);
    }

    [Fact]
    public void parse_minimal_valid_plan()
    {
        var result = ScenePlanParser.Parse(LoadFixture("plan_valid_minimal.json"));

        result.Ok.Should().BeTrue();
        result.Plan.Meta.Title.Should().Be("Minimal Scene");
        result.Plan.Actors.Should().BeEmpty();
        result.Plan.Dialog.Should().BeEmpty();
        result.Plan.Events.Should().BeEmpty();
        result.Plan.Layout.Props.Should().BeEmpty();
    }

    [Fact]
    public void parse_all_step_types()
    {
        var result = ScenePlanParser.Parse(LoadFixture("plan_all_steps.json"));

        result.Ok.Should().BeTrue();
        var steps = result.Plan.Events[0].Steps;

        steps.Should().HaveCount(10);
        steps[0].Type.Should().Be(EventStepType.Say);
        steps[1].Type.Should().Be(EventStepType.Move);
        steps[2].Type.Should().Be(EventStepType.Face);
        steps[3].Type.Should().Be(EventStepType.Wait);
        steps[4].Type.Should().Be(EventStepType.PlayMusic);
        steps[5].Type.Should().Be(EventStepType.SetFlag);
        steps[6].Type.Should().Be(EventStepType.GiveItem);
        steps[7].Type.Should().Be(EventStepType.Battle);
        steps[8].Type.Should().Be(EventStepType.CustomNote);
        steps[9].Type.Should().Be(EventStepType.IfFlag);
    }

    [Fact]
    public void parse_nested_ifflag()
    {
        var result = ScenePlanParser.Parse(LoadFixture("plan_nested_ifflag.json"));

        result.Ok.Should().BeTrue();
        var ifFlag = result.Plan.Events[0].Steps[0];
        ifFlag.Type.Should().Be(EventStepType.IfFlag);
        ifFlag.Key.Should().Be("has_key");
        ifFlag.ThenSteps.Should().HaveCount(2);
        ifFlag.ElseSteps.Should().HaveCount(1);

        // Nested if_flag
        var nestedIfFlag = ifFlag.ThenSteps[1];
        nestedIfFlag.Type.Should().Be(EventStepType.IfFlag);
        nestedIfFlag.Key.Should().Be("door_unlocked");
        nestedIfFlag.ThenSteps.Should().HaveCount(1);
        nestedIfFlag.ElseSteps.Should().HaveCount(1);
    }

    [Fact]
    public void parse_actor_with_all_fields()
    {
        var result = ScenePlanParser.Parse(LoadFixture("plan_valid_full.json"));

        result.Ok.Should().BeTrue();
        var cloud = result.Plan.Actors[0];
        cloud.Id.Should().Be("cloud");
        cloud.DisplayName.Should().Be("Cloud");
        cloud.Position.X.Should().Be(100);
        cloud.Position.Y.Should().Be(120);
        cloud.Facing.Should().Be('S');
    }

    // --- Parsing failures ---

    [Fact]
    public void parse_invalid_missing_actor_reference()
    {
        // This plan is syntactically valid but has a missing actor reference.
        // Parser should succeed; validator should catch the issue.
        var result = ScenePlanParser.Parse(LoadFixture("plan_invalid_missing_actor.json"));

        result.Ok.Should().BeTrue(); // Parser doesn't validate references
    }

    [Fact]
    public void parse_invalid_wrong_type_fails()
    {
        var result = ScenePlanParser.Parse(LoadFixture("plan_invalid_wrong_type.json"));

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("ms");
        result.Error.Should().Contain("number");
    }

    [Fact]
    public void parse_non_object_root_fails()
    {
        var result = ScenePlanParser.Parse(System.Text.Encoding.UTF8.GetBytes("[1,2,3]"));

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("Root must be an object");
    }

    [Fact]
    public void parse_missing_meta_fails()
    {
        var json = @"{""actors"": []}";
        var result = ScenePlanParser.Parse(System.Text.Encoding.UTF8.GetBytes(json));

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("meta");
    }

    [Fact]
    public void parse_incomplete_meta_fails()
    {
        var json = @"{""meta"": {""title"": ""Test""}}";
        var result = ScenePlanParser.Parse(System.Text.Encoding.UTF8.GetBytes(json));

        result.Ok.Should().BeFalse();
    }

    [Fact]
    public void parse_invalid_actor_position_fails()
    {
        var json = @"{
            ""meta"": {""title"": ""T"", ""model"": ""m"", ""version"": ""v""},
            ""actors"": [{""id"": ""a1"", ""position"": {""x"": ""not_num"", ""y"": 10}}]
        }";
        var result = ScenePlanParser.Parse(System.Text.Encoding.UTF8.GetBytes(json));

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("actors[0].position");
    }

    [Fact]
    public void parse_invalid_facing_direction_fails()
    {
        var json = @"{
            ""meta"": {""title"": ""T"", ""model"": ""m"", ""version"": ""v""},
            ""actors"": [{""id"": ""a1"", ""position"": {""x"": 10, ""y"": 10}, ""facing"": ""NW""}]
        }";
        var result = ScenePlanParser.Parse(System.Text.Encoding.UTF8.GetBytes(json));

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("facing");
    }

    [Fact]
    public void parse_invalid_trigger_type_fails()
    {
        var json = @"{
            ""meta"": {""title"": ""T"", ""model"": ""m"", ""version"": ""v""},
            ""events"": [{""id"": ""e1"", ""trigger"": ""invalid"", ""steps"": []}]
        }";
        var result = ScenePlanParser.Parse(System.Text.Encoding.UTF8.GetBytes(json));

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("trigger");
    }

    [Fact]
    public void parse_unknown_step_type_fails()
    {
        var json = @"{
            ""meta"": {""title"": ""T"", ""model"": ""m"", ""version"": ""v""},
            ""events"": [{""id"": ""e1"", ""trigger"": ""auto"", ""steps"": [{""type"": ""fly""}]}]
        }";
        var result = ScenePlanParser.Parse(System.Text.Encoding.UTF8.GetBytes(json));

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("unknown step");
    }

    [Fact]
    public void parse_missing_required_step_fields_fails()
    {
        var json = @"{
            ""meta"": {""title"": ""T"", ""model"": ""m"", ""version"": ""v""},
            ""events"": [{""id"": ""e1"", ""trigger"": ""auto"", ""steps"": [{""type"": ""say""}]}]
        }";
        var result = ScenePlanParser.Parse(System.Text.Encoding.UTF8.GetBytes(json));

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("actorId");
    }

    [Fact]
    public void parse_layout_missing_props_array_fails()
    {
        var json = @"{
            ""meta"": {""title"": ""T"", ""model"": ""m"", ""version"": ""v""},
            ""layout"": {""props"": ""not_array""}
        }";
        var result = ScenePlanParser.Parse(System.Text.Encoding.UTF8.GetBytes(json));

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("layout.props");
    }

    [Fact]
    public void parse_empty_json_object_fails()
    {
        var result = ScenePlanParser.Parse(System.Text.Encoding.UTF8.GetBytes("{}"));

        result.Ok.Should().BeFalse();
    }

    [Fact]
    public void parse_invalid_json_fails()
    {
        var result = ScenePlanParser.Parse(System.Text.Encoding.UTF8.GetBytes("{invalid}"));

        result.Ok.Should().BeFalse();
    }
}

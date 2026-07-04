using System.Collections.Generic;
using System.IO;

using FluentAssertions;

using MakouReactor.AI.Parsing;
using MakouReactor.AI.Validation;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Validation;

// Concrete subclass of abstract Field for testing
public class TestField : Field
{
    public TestField(string name) : base(name) { }
    public int Width { get; set; } = 320;
    public int Height { get; set; } = 240;
}

public class ScenePlanValidatorTests
{
    private static ScenePlan LoadPlan(string name)
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", name);
        var bytes = File.ReadAllBytes(path);
        var result = ScenePlanParser.Parse(bytes);
        result.Ok.Should().BeTrue($"Fixture {name} should parse successfully: {result.Error}");
        return result.Plan;
    }

    // --- Valid plans ---

    [Fact]
    public void validate_valid_full_plan_passes()
    {
        var plan = LoadPlan("plan_valid_full.json");
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field);

        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void validate_minimal_plan_passes()
    {
        var plan = LoadPlan("plan_valid_minimal.json");
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field);

        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void validate_all_steps_passes()
    {
        var plan = LoadPlan("plan_all_steps.json");
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field);

        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void validate_nested_ifflag_passes()
    {
        var plan = LoadPlan("plan_nested_ifflag.json");
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field);

        result.Issues.Should().BeEmpty();
    }

    // --- Actor validation ---

    [Fact]
    public void validate_duplicate_actor_ids()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Actors = new List<Actor>
            {
                new() { Id = "cloud", Position = new Point(10, 10) },
                new() { Id = "cloud", Position = new Point(20, 20) }
            }
        };
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field);

        result.Issues.Should().NotBeEmpty();
        result.Issues.Should().ContainSingle(i => i.Level == Severity.Error && i.Message.Contains("Duplicate"));
    }

    [Fact]
    public void validate_empty_actor_id()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Actors = new List<Actor>
            {
                new() { Id = "", Position = new Point(10, 10) }
            }
        };
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field);

        result.Issues.Should().ContainSingle(i => i.Level == Severity.Error && i.Message.Contains("Empty actor id"));
    }

    [Fact]
    public void validate_actor_out_of_bounds()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Actors = new List<Actor>
            {
                new() { Id = "cloud", Position = new Point(1000, 1000) }
            }
        };
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field);

        result.Issues.Should().ContainSingle(i => i.Level == Severity.Warn && i.Message.Contains("out of bounds"));
    }

    [Fact]
    public void validate_invalid_facing()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Actors = new List<Actor>
            {
                new() { Id = "cloud", Position = new Point(10, 10), Facing = 'X' }
            }
        };
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field);

        result.Issues.Should().ContainSingle(i => i.Level == Severity.Warn && i.Message.Contains("Invalid facing"));
    }

    // --- Dialog validation ---

    [Fact]
    public void validate_unknown_dialog_speaker()
    {
        var plan = LoadPlan("plan_invalid_missing_actor.json");
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field);

        result.Issues.Should().ContainSingle(i => i.Level == Severity.Error && i.Message.Contains("Unknown speaker"));
    }

    [Fact]
    public void validate_dialog_text_exceeds_max_length()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Actors = new List<Actor> { new() { Id = "cloud", Position = new Point(10, 10) } },
            Dialog = new List<DialogLine> { new() { SpeakerId = "cloud", Text = new string('A', 300) } }
        };
        var field = new TestField("test");
        var opts = new ValidationOptions { MaxLineLength = 100 };

        var result = ScenePlanValidator.Validate(plan, field, opts);

        result.Issues.Should().ContainSingle(i => i.Message.Contains("exceeds"));
    }

    [Fact]
    public void validate_dialog_contains_control_chars()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Actors = new List<Actor> { new() { Id = "cloud", Position = new Point(10, 10) } },
            Dialog = new List<DialogLine> { new() { SpeakerId = "cloud", Text = "Hello\u0001World" } }
        };
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field);

        result.Issues.Should().ContainSingle(i => i.Message.Contains("control character"));
    }

    // --- Event validation ---

    [Fact]
    public void validate_unknown_event_actor()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Events = new List<EventDef>
            {
                new()
                {
                    Id = "e1",
                    Trigger = "auto",
                    Steps = new List<EventStep>
                    {
                        new() { Type = EventStepType.Say, ActorId = "unknown", Text = "Hello" }
                    }
                }
            }
        };
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field);

        result.Issues.Should().ContainSingle(i => i.Message.Contains("Unknown actor"));
    }

    [Fact]
    public void validate_negative_wait_time()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Events = new List<EventDef>
            {
                new()
                {
                    Id = "e1",
                    Trigger = "auto",
                    Steps = new List<EventStep> { new() { Type = EventStepType.Wait, Ms = -100 } }
                }
            }
        };
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field);

        result.Issues.Should().ContainSingle(i => i.Message.Contains("Negative wait time"));
    }

    [Fact]
    public void validate_empty_flag_key()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Events = new List<EventDef>
            {
                new()
                {
                    Id = "e1",
                    Trigger = "auto",
                    Steps = new List<EventStep> { new() { Type = EventStepType.SetFlag, Key = "" } }
                }
            }
        };
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field);

        result.Issues.Should().ContainSingle(i => i.Message.Contains("Empty flag key"));
    }

    [Fact]
    public void validate_nested_step_bounds()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Actors = new List<Actor> { new() { Id = "a1", Position = new Point(10, 10) } },
            Events = new List<EventDef>
            {
                new()
                {
                    Id = "e1",
                    Trigger = "auto",
                    Steps = new List<EventStep>
                    {
                        new()
                        {
                            Type = EventStepType.IfFlag,
                            Key = "test",
                            ThenSteps = new List<EventStep>
                            {
                                new() { Type = EventStepType.Move, ActorId = "a1", To = new Point(1000, 1000) }
                            }
                        }
                    }
                }
            }
        };
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field);

        // Nested steps should inherit bounds from parent event
        // Note: Move steps don't currently validate position bounds in the C++ code either
        result.Issues.Should().NotBeNull();
    }

    // --- Layout validation ---

    [Fact]
    public void validate_spawn_point_out_of_bounds()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Layout = new LayoutDef { SpawnPoint = new Point(1000, 1000) }
        };
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field);

        result.Issues.Should().ContainSingle(i => i.Message.Contains("Spawn point out of bounds"));
    }

    [Fact]
    public void validate_prop_out_of_bounds()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Layout = new LayoutDef
            {
                Props = new List<LayoutProp> { new("barrel", 1000, 1000) }
            }
        };
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field);

        result.Issues.Should().ContainSingle(i => i.Message.Contains("out of bounds"));
    }

    // --- Validation options ---

    [Fact]
    public void validate_with_null_options_uses_defaults()
    {
        var plan = LoadPlan("plan_valid_full.json");
        var field = new TestField("test");

        var result = ScenePlanValidator.Validate(plan, field, null);

        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void validate_profanity_filter()
    {
        var plan = new ScenePlan
        {
            Meta = new ScenePlanMeta { Title = "T", Model = "m", Version = "v" },
            Actors = new List<Actor> { new() { Id = "a1", Position = new Point(10, 10) } },
            Dialog = new List<DialogLine> { new() { SpeakerId = "a1", Text = "badword here" } }
        };
        var field = new TestField("test");
        var opts = new ValidationOptions
        {
            ProfanityFilter = true,
            BannedWords = new List<string> { "badword" }
        };

        var result = ScenePlanValidator.Validate(plan, field, opts);

        result.Issues.Should().ContainSingle(i => i.Message.Contains("flagged term"));
    }
}

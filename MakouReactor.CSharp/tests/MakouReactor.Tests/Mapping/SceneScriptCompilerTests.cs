using FluentAssertions;

using MakouReactor.AI.Mapping;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Mapping;

public class SceneScriptCompilerTests
{
    private static EventDef Event(string trigger, params EventStep[] steps) =>
        new("e1", trigger, default, steps.ToList());

    private static EventStep Say(string text) => new() { Type = EventStepType.Say, ActorId = "narrator", Text = text };

    private static CompiledScene Compile(ScenePlan plan, Func<EventStep, int?>? ids = null) =>
        SceneScriptCompiler.Compile(plan, ids ?? (_ => 0));

    [Fact]
    public void wait_compiles_to_frames_at_30fps_rounded_up()
    {
        var plan = new ScenePlan { Events = { Event("on_enter", new EventStep { Type = EventStepType.Wait, Ms = 1000 }) } };

        var result = Compile(plan);

        result.Script.Should().Equal(0x00, 0x24, 0x1E, 0x00, 0x00);
        result.StepsWritten.Should().Be(1);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(34, 2)]
    [InlineData(0, 1)]
    [InlineData(10_000_000, 65535)]
    public void wait_frame_count_is_clamped(int ms, int frames)
    {
        var plan = new ScenePlan { Events = { Event("auto", new EventStep { Type = EventStepType.Wait, Ms = ms }) } };

        var script = Compile(plan).Script;

        (script[2] | (script[3] << 8)).Should().Be(frames);
    }

    [Fact]
    public void say_compiles_to_window_then_message_with_a_fitting_window()
    {
        var say = Say("Hello there");
        var plan = new ScenePlan { Events = { Event("on_enter", say) } };

        var result = Compile(plan, step => step == say ? 7 : null);

        var script = result.Script;
        script[0].Should().Be(0x00, "an empty Init comes first");
        script[1].Should().Be(0x50, "WINDOW");
        script[2].Should().Be(0, "window id");
        var width = (ushort)(script[7] | (script[8] << 8));
        var height = (ushort)(script[9] | (script[10] << 8));
        var (expectedWidth, expectedHeight) = Script.EstimateTextWindowSize("Hello there");
        width.Should().Be(expectedWidth);
        height.Should().Be(expectedHeight);
        script[11..14].Should().Equal(0x40, 0x00, 0x07); // MESSAGE window 0, text 7
        script[^1].Should().Be(0x00);
    }

    [Fact]
    public void numeric_battle_and_item_compile_with_literal_operands()
    {
        var plan = new ScenePlan
        {
            Events =
            {
                Event("on_enter",
                    new EventStep { Type = EventStepType.Battle, EncounterId = "300" },
                    new EventStep { Type = EventStepType.GiveItem, ItemId = "5", Qty = 3 }),
            },
        };

        var result = Compile(plan);

        result.Script.Should().Equal(
            0x00,
            0x70, 0x00, 0x2C, 0x01,         // BATTLE 300
            0x58, 0x00, 0x05, 0x00, 0x03,   // STITM item 5 x3
            0x00);
    }

    [Fact]
    public void compiled_bytes_parse_cleanly_with_the_raw_opcode_reader()
    {
        var say = Say("A line\nwith two rows");
        var plan = new ScenePlan
        {
            Events =
            {
                Event("on_enter",
                    say,
                    new EventStep { Type = EventStepType.Wait, Ms = 500 },
                    new EventStep { Type = EventStepType.Battle, EncounterId = "42" },
                    new EventStep { Type = EventStepType.GiveItem, ItemId = "0", Qty = 1 }),
            },
        };

        var script = Compile(plan, _ => 3).Script;

        var opcodes = RawOpcodeReader.Read(script);
        opcodes.Should().OnlyContain(o => !o.IsTruncated);
        opcodes.Select(o => o.Name).Should().Equal("RET", "WINDOW", "MESSAGE", "WAIT", "BATTLE", "STITM", "RET");
        opcodes.Sum(o => o.Bytes.Length).Should().Be(script.Length);
    }

    [Fact]
    public void unsupported_steps_and_triggers_are_skipped_with_reasons_and_never_guessed()
    {
        var plan = new ScenePlan
        {
            Events =
            {
                Event("on_interact", new EventStep { Type = EventStepType.Wait, Ms = 100 }),
                Event("on_enter",
                    new EventStep { Type = EventStepType.Move, ActorId = "cloud" },
                    new EventStep { Type = EventStepType.Face, ActorId = "cloud" },
                    new EventStep { Type = EventStepType.PlayMusic, Track = "main" },
                    new EventStep { Type = EventStepType.SetFlag, Key = "k" },
                    new EventStep { Type = EventStepType.IfFlag, Key = "k" },
                    new EventStep { Type = EventStepType.Battle, EncounterId = "guards" },
                    new EventStep { Type = EventStepType.GiveItem, ItemId = "potion", Qty = 1 },
                    new EventStep { Type = EventStepType.GiveItem, ItemId = "5", Qty = 0 },
                    new EventStep { Type = EventStepType.CustomNote, Text = "director note" }),
            },
        };

        var result = Compile(plan);

        result.IsEmpty.Should().BeTrue();
        result.StepsWritten.Should().Be(0);
        result.Skipped.Should().HaveCount(9);
        result.Skipped[0].Should().Contain("on_interact");
        result.Skipped.Should().Contain(s => s.Contains("guards") && s.Contains("numeric battle id"));
        result.Skipped.Should().Contain(s => s.Contains("give_item"));
    }

    [Fact]
    public void say_without_a_stored_text_is_skipped()
    {
        var plan = new ScenePlan { Events = { Event("on_enter", Say("lost")) } };

        var result = Compile(plan, _ => null);

        result.IsEmpty.Should().BeTrue();
        result.Skipped.Should().ContainSingle().Which.Should().Contain("text could not be stored");
    }

    [Theory]
    [InlineData("on_enter", true)]
    [InlineData("AUTO", true)]
    [InlineData("zone", false)]
    [InlineData("on_interact", false)]
    public void only_self_starting_triggers_are_automatic(string trigger, bool expected)
    {
        SceneScriptCompiler.IsAutomatic(trigger).Should().Be(expected);
    }
}

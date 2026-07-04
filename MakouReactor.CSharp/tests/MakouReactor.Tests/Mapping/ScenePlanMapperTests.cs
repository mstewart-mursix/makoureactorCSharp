using FluentAssertions;
using System.Buffers.Binary;
using System.Text;

using MakouReactor.AI.Mapping;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Mapping;

public sealed class ScenePlanMapperTests
{
    [Fact]
    public void preview_returns_summary_without_modifying_field()
    {
        var field = new TestField("md1stin");
        var plan = BuildPlan();

        var result = ScenePlanMapper.ApplyToField(
            plan,
            field,
            new ApplyOptions { PreviewOnly = true, GroupNameOverride = "LLM_Test" });

        result.Ok.Should().BeTrue(result.Error);
        result.GroupName.Should().Be("LLM_Test");
        result.Summary.Should().Contain("Title: Ambush");
        field.IsModified.Should().BeFalse();
    }

    [Fact]
    public void apply_marks_field_modified()
    {
        var field = new TestField("md1stin");

        var result = ScenePlanMapper.ApplyToField(
            BuildPlan(),
            field,
            new ApplyOptions { PreviewOnly = false, GroupNameOverride = "LLM_Test" });

        result.Ok.Should().BeTrue(result.Error);
        result.GroupName.Should().Be("LLM_Test");
        field.IsModified.Should().BeTrue();
    }

    [Fact]
    public void apply_adds_dialogue_text_to_pc_field_section1()
    {
        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField(BuildSection1()));

        var result = ScenePlanMapper.ApplyToField(
            BuildPlan(),
            field,
            new ApplyOptions { PreviewOnly = false, GroupNameOverride = "LLM_Test" });

        result.Ok.Should().BeTrue(result.Error);
        result.Summary.Should().Contain("Added 1 dialogue text entry starting at text 2.");
        field.IsModified.Should().BeTrue();
        field.ScriptsAndTexts!.Texts.Select(text => text.Value)
            .Should().Equal("Map name", "Existing line", "Let's move.");

        var reopened = FieldPC.OpenDecompressed("md1stin", field.SaveDecompressed());
        reopened.ScriptsAndTexts!.Texts.Select(text => text.Value)
            .Should().Equal("Map name", "Existing line", "Let's move.");
    }

    [Fact]
    public void preview_does_not_add_dialogue_text_to_pc_field_section1()
    {
        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField(BuildSection1()));

        var result = ScenePlanMapper.ApplyToField(
            BuildPlan(),
            field,
            new ApplyOptions { PreviewOnly = true, GroupNameOverride = "LLM_Test" });

        result.Ok.Should().BeTrue(result.Error);
        field.IsModified.Should().BeFalse();
        field.ScriptsAndTexts!.Texts.Select(text => text.Value)
            .Should().Equal("Map name", "Existing line");
    }

    [Fact]
    public void apply_requires_active_field()
    {
        var result = ScenePlanMapper.ApplyToField(BuildPlan(), null);

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("No active field");
    }

    private static ScenePlan BuildPlan() => new()
    {
        Meta = new ScenePlanMeta
        {
            Title = "Ambush",
            Model = "test",
            Version = "1.0",
        },
        Actors =
        {
            new Actor("cloud", "Cloud", x: 10, y: 20),
        },
        Dialog =
        {
            new DialogLine("cloud", "Let's move."),
        },
        Events =
        {
            new EventDef("start", "on_enter"),
        },
    };

    private sealed class TestField : Field
    {
        public TestField(string name) : base(name)
        {
        }
    }

    private static byte[] BuildPcField(byte[] section1)
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[0] = section1;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write((ushort)0);
        writer.Write(9u);
        for (var i = 0; i < 9; i++)
            writer.Write(0u);

        var offsets = new uint[9];
        for (var i = 0; i < 9; i++)
        {
            offsets[i] = checked((uint)stream.Position);
            writer.Write((uint)sections[i].Length);
            writer.Write(sections[i]);
        }

        writer.Write("FINAL FANTASY7"u8);

        stream.Position = 6;
        foreach (var offset in offsets)
            writer.Write(offset);

        return stream.ToArray();
    }

    private static byte[] BuildSection1()
    {
        var texts = BuildTextSection(["Map name", "Existing line"]);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)0x0502);
        writer.Write((byte)0);
        writer.Write((byte)1);
        writer.Write((ushort)32);
        writer.Write((ushort)0);
        writer.Write((ushort)512);
        writer.Write(new byte[6]);
        WriteFixed(writer, "makou", 8);
        WriteFixed(writer, "md1stin", 8);
        writer.Write(texts);

        return stream.ToArray();
    }

    private static byte[] BuildTextSection(IReadOnlyList<string> texts)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)texts.Count);
        var offset = checked((ushort)(2 + (texts.Count * 2)));
        foreach (var text in texts)
        {
            writer.Write(offset);
            offset += checked((ushort)(Encoding.ASCII.GetByteCount(text) + 1));
        }

        foreach (var text in texts)
        {
            writer.Write(Encoding.ASCII.GetBytes(text));
            writer.Write((byte)0xFF);
        }

        return stream.ToArray();
    }

    private static void WriteFixed(BinaryWriter writer, string value, int length)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        writer.Write(bytes.AsSpan(0, Math.Min(bytes.Length, length)));
        if (bytes.Length < length)
            writer.Write(new byte[length - bytes.Length]);
    }
}

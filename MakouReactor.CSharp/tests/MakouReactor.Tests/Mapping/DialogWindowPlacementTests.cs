using System.Text;

using FluentAssertions;

using MakouReactor.AI.Mapping;
using MakouReactor.Core.Models;
using MakouReactor.Core.Text;

using Xunit;

namespace MakouReactor.Tests.Mapping;

/// <summary>
/// Phase 8: <c>say</c> windows go in the vertical half of the screen opposite the speaker and stay
/// on screen (synthetic in-memory fields only, no game data).
/// </summary>
public sealed class DialogWindowPlacementTests
{
    private const string ShortLine = "Hello there.";

    [Fact]
    public void actor_in_top_half_gets_a_bottom_window_and_vice_versa_without_walkmesh()
    {
        // No walkmesh: plan positions are 320x240 screen pixels.
        var top = EmitSayWindow(FieldPC.CreateEmpty("llmtest"), actorY: 40, ShortLine);
        var bottom = EmitSayWindow(FieldPC.CreateEmpty("llmtest"), actorY: 200, ShortLine);

        var (width, height) = FF7TextMetrics.CalcWindowSize(FF7TextCodec.Encode(ShortLine));
        top.Window.Should().Be(((short)((320 - width) / 2), (short)(223 - height), (ushort)width, (ushort)height));
        bottom.Window.Should().Be(((short)((320 - width) / 2), (short)8, (ushort)width, (ushort)height));
        top.Warnings.Should().NotContain(w => w.Contains("approximate"));
    }

    [Theory]
    [InlineData("narrator")]
    [InlineData("Narrator")]
    [InlineData("ghost")]
    [InlineData("")]
    public void narrator_and_unknown_speakers_keep_the_default_placement(string speaker)
    {
        var result = EmitSayWindow(FieldPC.CreateEmpty("llmtest"), actorY: 40, ShortLine, speaker);

        var (width, height) = FF7TextMetrics.CalcWindowSize(FF7TextCodec.Encode(ShortLine));
        result.Window.Should().Be(((short)((320 - width) / 2), (short)8, (ushort)width, (ushort)height));
    }

    [Theory]
    [InlineData(-5000)]
    [InlineData(0)]
    [InlineData(119)]
    [InlineData(120)]
    [InlineData(239)]
    [InlineData(30000)]
    public void multi_page_windows_stay_on_screen_for_edge_positions(int actorY)
    {
        var words = string.Join(" ", Enumerable.Range(1, 60).Select(i => $"word{i}"));
        var result = EmitSayWindow(FieldPC.CreateEmpty("llmtest"), actorY, words);

        result.Text.Should().Contain(0xE8, "the text spans several pages of one window");
        var (x, y, w, h) = result.Window;
        var (width, height) = FF7TextMetrics.CalcWindowSize(result.Text);
        ((int)w, (int)h).Should().Be((width, height), "the window is sized for the tallest page");
        AssertOnScreen(x, y, w, h);
        y.Should().Be(actorY < 120 ? (short)(223 - h) : (short)8);
    }

    [Theory]
    [InlineData(-1000.0, 304, 25)]
    [InlineData(1000.0, 304, 25)]
    [InlineData(10.0, 322, 226)]
    [InlineData(230.0, 322, 226)]
    [InlineData(10.0, 304, 215)]
    [InlineData(10.0, 20, 25)]
    public void clamping_keeps_large_windows_on_screen(double actorScreenY, int width, int height)
    {
        var (x, y) = DialogWindowPlacement.AvoidingActor(actorScreenY, width, height);

        x.Should().BeGreaterThanOrEqualTo(8);
        y.Should().BeGreaterThanOrEqualTo(8);
        if (width <= 304)
            (x + width).Should().BeLessThanOrEqualTo(312);
        if (height <= 215)
            (y + height).Should().BeLessThanOrEqualTo(223);
        (y + height).Should().BeLessThanOrEqualTo(240, "even the tallest window (226 px) fits the 240 px screen");
    }

    [Fact]
    public void real_pos_matches_the_cpp_clamp_order()
    {
        DialogWindowPlacement.RealPos(300, 220, 40, 30).Should().Be(((short)272, (short)193));
        DialogWindowPlacement.RealPos(-20, -20, 40, 30).Should().Be(((short)8, (short)8));
        // Larger than the allowed area: the minimum is applied last and wins.
        DialogWindowPlacement.RealPos(0, 0, 322, 226).Should().Be(((short)8, (short)8));
        DialogWindowPlacement.Default(100, 41).Should().Be(((short)110, (short)8));
    }

    [Fact]
    public void walkmesh_without_camera_uses_the_bounds_approximation_and_warns()
    {
        // Walkmesh over (-800,-800)-(800,800); larger Y is taken as higher on screen.
        var top = EmitSayWindow(BuildField(withCamera: false), actorY: 600, ShortLine);
        var bottom = EmitSayWindow(BuildField(withCamera: false), actorY: -150, ShortLine);

        var (_, height) = FF7TextMetrics.CalcWindowSize(FF7TextCodec.Encode(ShortLine));
        top.Window.Y.Should().Be((short)(223 - height));
        bottom.Window.Y.Should().Be(8);
        top.Warnings.Should().ContainSingle(w => w.Contains("no camera data") && w.Contains("approximate"));
    }

    [Fact]
    public void walkmesh_with_camera_projects_the_actor_through_the_field_camera()
    {
        // With CaFile::initEmpty's camera, field Y = -150 projects to screen y ~ 55 (top half) even
        // though it is in the lower half of the walkmesh bounds; Y = -600 projects to ~ 170.
        DialogWindowPlacement.TryProjectToScreen(FieldCamera.Default, 0, -150, 0, out var sx, out var sy).Should().BeTrue();
        sx.Should().BeApproximately(145.1, 0.5);
        sy.Should().BeApproximately(55.1, 0.5);
        DialogWindowPlacement.TryProjectToScreen(FieldCamera.Default, 0, -600, 0, out _, out var lowY).Should().BeTrue();
        lowY.Should().BeApproximately(170.5, 0.5);

        var top = EmitSayWindow(BuildField(withCamera: true), actorY: -150, ShortLine);
        var bottom = EmitSayWindow(BuildField(withCamera: true), actorY: -600, ShortLine);

        var (_, height) = FF7TextMetrics.CalcWindowSize(FF7TextCodec.Encode(ShortLine));
        top.Window.Y.Should().Be((short)(223 - height));
        bottom.Window.Y.Should().Be(8);
        top.Warnings.Should().NotContain(w => w.Contains("approximate"));
    }

    [Fact]
    public void projection_rejects_points_behind_the_camera_and_zero_zoom()
    {
        DialogWindowPlacement.TryProjectToScreen(FieldCamera.Default, 0, -5000, 0, out _, out _).Should().BeFalse();
        DialogWindowPlacement.TryProjectToScreen(FieldCamera.Default with { Zoom = 0 }, 0, 0, 0, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void move_steps_update_the_speaker_position_within_an_event()
    {
        var field = FieldPC.CreateEmpty("llmtest");
        var plan = new ScenePlan
        {
            Actors = { new Actor("guard", "Guard", x: 160, y: 40) },
            Events =
            {
                new EventDef("intro", "on_enter", steps:
                [
                    new EventStep { Type = EventStepType.Say, ActorId = "guard", Text = "Up here." },
                    new EventStep { Type = EventStepType.Move, ActorId = "guard", To = new Point(160, 200) },
                    new EventStep { Type = EventStepType.Say, ActorId = "guard", Text = "Down here." },
                ]),
            },
        };

        var result = ScenePlanMapper.ApplyToField(plan, field, new ApplyOptions { PreviewOnly = false });

        result.Ok.Should().BeTrue(result.Error);
        var windows = RawOpcodeReader.Read(field.ScriptsAndTexts!.GrpScripts[1].Scripts[1].RawBytes.Span)
            .Where(op => op.Name == "WINDOW")
            .Select(DecodeWindow)
            .ToList();
        windows.Should().HaveCount(2);
        windows[0].Y.Should().Be((short)(223 - windows[0].H));
        windows[1].Y.Should().Be(8);
    }

    [Fact]
    public void camera_section_parses_pc_and_ps_layouts()
    {
        var pc = CaFile.Open(CameraBytes(FieldCamera.Default));
        pc.Should().NotBeNull();
        pc!.Cameras.Should().ContainSingle().Which.Should().Be(FieldCamera.Default);

        var two = CaFile.Open([.. CameraBytes(FieldCamera.Default), .. CameraBytes(FieldCamera.Default with { Zoom = 300 })]);
        two!.Cameras.Select(c => c.Zoom).Should().Equal(525, 300);

        // PlayStation: 38 bytes + 18-byte cameras that store only their axes.
        byte[] ps = [.. CameraBytes(FieldCamera.Default), .. CameraBytes(FieldCamera.Default with { AxisX = new CameraAxis(1, 2, 3) })[..18]];
        var psFile = CaFile.Open(ps);
        psFile!.Cameras.Should().HaveCount(2);
        psFile.Cameras[1].Should().Be(FieldCamera.Default with { AxisX = new CameraAxis(1, 2, 3) });

        CaFile.Open([]).Should().BeNull();
        CaFile.Open(new byte[39]).Should().BeNull();
        FieldPC.CreateEmpty("llmtest").Camera.Should().BeNull();
        BuildField(withCamera: true).Camera!.Cameras.Should().ContainSingle().Which.Should().Be(FieldCamera.Default);
    }

    private static void AssertOnScreen(short x, short y, ushort w, ushort h)
    {
        x.Should().BeGreaterThanOrEqualTo(8);
        y.Should().BeGreaterThanOrEqualTo(8);
        (x + w).Should().BeLessThanOrEqualTo(312);
        (y + h).Should().BeLessThanOrEqualTo(223);
    }

    private static (short X, short Y, ushort W, ushort H) DecodeWindow(RawOpcode op)
    {
        op.Name.Should().Be("WINDOW");
        op.Bytes.Should().HaveCount(10);
        return (
            BitConverter.ToInt16(op.Bytes, 2),
            BitConverter.ToInt16(op.Bytes, 4),
            BitConverter.ToUInt16(op.Bytes, 6),
            BitConverter.ToUInt16(op.Bytes, 8));
    }

    private sealed record SayResult((short X, short Y, ushort W, ushort H) Window, byte[] Text, IReadOnlyList<string> Warnings);

    /// <summary>Applies a one-actor plan whose on_enter event has one say step; decodes its WINDOW.</summary>
    private static SayResult EmitSayWindow(FieldPC field, int actorY, string text, string speaker = "guard")
    {
        var plan = new ScenePlan
        {
            Actors = { new Actor("guard", "Guard", x: 0, y: actorY) },
            Events =
            {
                new EventDef("intro", "on_enter", steps: [new EventStep { Type = EventStepType.Say, ActorId = speaker, Text = text }]),
            },
        };

        var textCount = field.ScriptsAndTexts!.TextCount;
        var result = ScenePlanMapper.ApplyToField(plan, field, new ApplyOptions { PreviewOnly = false });
        result.Ok.Should().BeTrue(result.Error);

        var main = RawOpcodeReader.Read(field.ScriptsAndTexts.GrpScripts[1].Scripts[1].RawBytes.Span);
        main.Select(op => op.Name).Should().Equal("IFUB", "BITON", "WINDOW", "MESSAGE", "RET");
        return new SayResult(DecodeWindow(main[2]), field.ScriptsAndTexts.Texts[textCount].RawBytes!, result.Warnings);
    }

    /// <summary>
    /// <see cref="FieldPC.CreateEmpty"/> with a flat two-triangle walkmesh over (-800,-800)-(800,800)
    /// and, optionally, a camera section holding <see cref="FieldCamera.Default"/>.
    /// </summary>
    private static FieldPC BuildField(bool withCamera)
    {
        var empty = FieldPC.CreateEmpty("llmtest");
        var sections = empty.Sections
            .OrderBy(s => s.Index)
            .Select(s => s.Section switch
            {
                FieldSection.Walkmesh => BuildWalkmesh(),
                FieldSection.Camera when withCamera => CameraBytes(FieldCamera.Default),
                _ => empty.GetSectionData(s.Section),
            })
            .ToArray();
        return FieldPC.OpenDecompressed("llmtest", BuildPcField(sections));
    }

    private static byte[] BuildWalkmesh()
    {
        var walkmesh = new IdFile();
        walkmesh.InsertTriangle(0, new Triangle(
            new VertexSR(-800, -800, 0, 0), new VertexSR(800, -800, 0, 0), new VertexSR(800, 800, 0, 0)));
        walkmesh.InsertTriangle(1, new Triangle(
            new VertexSR(-800, -800, 0, 0), new VertexSR(800, 800, 0, 0), new VertexSR(-800, 800, 0, 0)));
        return walkmesh.Save();
    }

    /// <summary>A 38-byte PC camera record (<c>CaFile::save</c> layout).</summary>
    private static byte[] CameraBytes(FieldCamera camera)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        foreach (var axis in new[] { camera.AxisX, camera.AxisY, camera.AxisZ })
        {
            writer.Write(axis.X);
            writer.Write(axis.Y);
            writer.Write(axis.Z);
        }

        writer.Write(camera.AxisZ.Z); // camera_axis2z
        writer.Write(camera.PositionX);
        writer.Write(camera.PositionY);
        writer.Write(camera.PositionZ);
        writer.Write(0); // blank
        writer.Write(camera.Zoom);
        return stream.ToArray();
    }

    private static byte[] BuildPcField(IReadOnlyList<byte[]> sections)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write((ushort)0);
        writer.Write((uint)sections.Count);
        for (var i = 0; i < sections.Count; i++)
            writer.Write(0u);

        var offsets = new uint[sections.Count];
        for (var i = 0; i < sections.Count; i++)
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
}

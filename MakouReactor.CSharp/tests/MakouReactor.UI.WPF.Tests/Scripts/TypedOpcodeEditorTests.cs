using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.Scripts;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Scripts;

public sealed class TypedOpcodeEditorTests
{
    [Fact]
    public void supports_message_and_window_opcodes()
    {
        TypedOpcodeEditor.Supports(new RawOpcode(0, 0x40, "MESSAGE", 3, [0x40, 1, 2], false))
            .Should().BeTrue();
        TypedOpcodeEditor.Supports(new RawOpcode(0, 0x50, "WINDOW", 10, [0x50, 1, 0, 0, 0, 0, 10, 0, 20, 0], false))
            .Should().BeTrue();
        TypedOpcodeEditor.Supports(new RawOpcode(0, 0x51, "WMOVE", 6, [0x51, 1, 10, 0, 20, 0], false))
            .Should().BeTrue();
        TypedOpcodeEditor.Supports(new RawOpcode(0, 0x60, "MAPJUMP", 10, [0x60, 1, 0, 2, 0, 3, 0, 4, 0, 5], false))
            .Should().BeTrue();
        TypedOpcodeEditor.Supports(new RawOpcode(0, 0x85, "PLUS", 4, [0x85, 0x21, 0x30, 0x40], false))
            .Should().BeTrue();
        TypedOpcodeEditor.Supports(new RawOpcode(0, 0xF1, "SOUND", 5, [0xF1, 1, 2, 3, 4], false))
            .Should().BeTrue();
        TypedOpcodeEditor.Supports(new RawOpcode(0, 0xA1, "CHAR", 2, [0xA1, 5], false))
            .Should().BeTrue();
        TypedOpcodeEditor.Supports(new RawOpcode(0, 0xB0, "CANIM1", 5, [0xB0, 1, 2, 3, 4], false))
            .Should().BeTrue();
        TypedOpcodeEditor.Supports(new RawOpcode(0, 0xD0, "LINE", 13, [0xD0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], false))
            .Should().BeTrue();
        TypedOpcodeEditor.Supports(new RawOpcode(0, 0xD8, "PMJMP", 3, [0xD8, 1, 0], false))
            .Should().BeTrue();
        TypedOpcodeEditor.Supports(new RawOpcode(0, 0x0F, "SPECIAL", 2, [0x0F, 0xFD], false))
            .Should().BeTrue();
        TypedOpcodeEditor.Supports(new RawOpcode(0, 0x24, "WAIT", 3, [0x24, 1, 0], false))
            .Should().BeFalse();
    }

    [Fact]
    public void builds_message_bytes_from_typed_fields()
    {
        TypedOpcodeEditor.TryBuildMessage(1, 42, out var bytes, out var error)
            .Should().BeTrue();

        bytes.Should().Equal([0x40, 0x01, 0x2A]);
        error.Should().BeEmpty();
    }

    [Fact]
    public void builds_window_bytes_from_typed_fields()
    {
        TypedOpcodeEditor.TryBuildWindow(2, 100, 200, 320, 120, out var bytes, out var error)
            .Should().BeTrue();

        bytes.Should().Equal([0x50, 0x02, 0x64, 0x00, 0xC8, 0x00, 0x40, 0x01, 0x78, 0x00]);
        error.Should().BeEmpty();
    }

    [Fact]
    public void exposes_typed_fields_for_map_jump()
    {
        var fields = TypedOpcodeEditor.GetFields(
            new RawOpcode(0, 0x60, "MAPJUMP", 10, [0x60, 0x34, 0x12, 10, 0, 20, 0, 2, 0, 6], false));

        fields.Should().Equal(
            new TypedOpcodeField("Field", 0x1234, ushort.MaxValue),
            new TypedOpcodeField("X", 10, ushort.MaxValue),
            new TypedOpcodeField("Y", 20, ushort.MaxValue),
            new TypedOpcodeField("Triangle", 2, ushort.MaxValue),
            new TypedOpcodeField("Direction", 6, byte.MaxValue));
    }

    [Fact]
    public void builds_window_movement_bytes_from_typed_fields()
    {
        var opcode = new RawOpcode(0, 0x51, "WMOVE", 6, [0x51, 0, 0, 0, 0, 0], false);

        TypedOpcodeEditor.TryBuild(opcode, [3, 320, 128], out var bytes, out var error)
            .Should().BeTrue();

        bytes.Should().Equal([0x51, 0x03, 0x40, 0x01, 0x80, 0x00]);
        error.Should().BeEmpty();
    }

    [Fact]
    public void builds_map_jump_bytes_from_typed_fields()
    {
        var opcode = new RawOpcode(0, 0x60, "MAPJUMP", 10, [0x60, 0, 0, 0, 0, 0, 0, 0, 0, 0], false);

        TypedOpcodeEditor.TryBuild(opcode, [0x1234, 10, 20, 2, 6], out var bytes, out var error)
            .Should().BeTrue();

        bytes.Should().Equal([0x60, 0x34, 0x12, 10, 0, 20, 0, 2, 0, 6]);
        error.Should().BeEmpty();
    }

    [Fact]
    public void builds_binary_variable_math_bytes_from_typed_fields()
    {
        var opcode = new RawOpcode(0, 0x85, "PLUS", 4, [0x85, 0, 0, 0], false);

        TypedOpcodeEditor.TryBuild(opcode, [1, 0x30, 2, 0x40], out var bytes, out var error)
            .Should().BeTrue();

        bytes.Should().Equal([0x85, 0x21, 0x30, 0x40]);
        error.Should().BeEmpty();
    }

    [Fact]
    public void builds_unary_variable_math_bytes_from_typed_fields()
    {
        var opcode = new RawOpcode(0, 0x95, "INC", 3, [0x95, 0, 0], false);

        TypedOpcodeEditor.TryBuild(opcode, [2, 1, 0x44], out var bytes, out var error)
            .Should().BeTrue();

        bytes.Should().Equal([0x95, 0x21, 0x44]);
        error.Should().BeEmpty();
    }

    [Fact]
    public void rejects_typed_values_outside_declared_field_range()
    {
        var opcode = new RawOpcode(0, 0x85, "PLUS", 4, [0x85, 0, 0, 0], false);

        TypedOpcodeEditor.TryBuild(opcode, [16, 0x30, 2, 0x40], out _, out var error)
            .Should().BeFalse();

        error.Should().Be("Dest bank must be between 0 and 15.");
    }

    [Fact]
    public void exposes_signed_walkmesh_line_fields()
    {
        var fields = TypedOpcodeEditor.GetFields(
            new RawOpcode(0, 0xD0, "LINE", 13, [0xD0, 0x9C, 0xFF, 20, 0, 30, 0, 40, 0, 0xC4, 0xFF, 60, 0], false));

        fields.Should().Equal(
            new TypedOpcodeField("Point 1 X", -100, short.MaxValue, short.MinValue),
            new TypedOpcodeField("Point 1 Y", 20, short.MaxValue, short.MinValue),
            new TypedOpcodeField("Point 1 Z", 30, short.MaxValue, short.MinValue),
            new TypedOpcodeField("Point 2 X", 40, short.MaxValue, short.MinValue),
            new TypedOpcodeField("Point 2 Y", -60, short.MaxValue, short.MinValue),
            new TypedOpcodeField("Point 2 Z", 60, short.MaxValue, short.MinValue));
    }

    [Fact]
    public void builds_walkmesh_line_bytes_from_typed_fields()
    {
        var opcode = new RawOpcode(0, 0xD0, "LINE", 13, [0xD0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], false);

        TypedOpcodeEditor.TryBuild(opcode, [-100, 20, 30, 40, -60, 60], out var bytes, out var error)
            .Should().BeTrue();

        bytes.Should().Equal([0xD0, 0x9C, 0xFF, 20, 0, 30, 0, 40, 0, 0xC4, 0xFF, 60, 0]);
        error.Should().BeEmpty();
    }

    [Fact]
    public void builds_walkmesh_reference_bytes_from_typed_fields()
    {
        TypedOpcodeEditor.TryBuild(
                new RawOpcode(0, 0xD8, "PMJMP", 3, [0xD8, 0, 0], false),
                [0x1234],
                out var mapJumpBytes,
                out var mapJumpError)
            .Should().BeTrue();
        TypedOpcodeEditor.TryBuild(
                new RawOpcode(0, 0xC5, "TALKR", 3, [0xC5, 0, 0], false),
                [2, 1, 44],
                out var talkRangeBytes,
                out var talkRangeError)
            .Should().BeTrue();
        TypedOpcodeEditor.TryBuild(
                new RawOpcode(0, 0xD6, "TLKR2", 4, [0xD6, 0, 0, 0], false),
                [2, 1, 0x1234],
                out var talkRange2Bytes,
                out var talkRange2Error)
            .Should().BeTrue();

        mapJumpBytes.Should().Equal([0xD8, 0x34, 0x12]);
        talkRangeBytes.Should().Equal([0xC5, 0x21, 44]);
        talkRange2Bytes.Should().Equal([0xD6, 0x21, 0x34, 0x12]);
        mapJumpError.Should().BeEmpty();
        talkRangeError.Should().BeEmpty();
        talkRange2Error.Should().BeEmpty();
    }

    [Fact]
    public void exposes_and_builds_special_subkey_opcode()
    {
        var opcode = new RawOpcode(0, 0x0F, "SPECIAL", 2, [0x0F, 0xFD], false);

        var fields = TypedOpcodeEditor.GetFields(opcode);
        TypedOpcodeEditor.TryBuild(opcode, [0xF6], out var bytes, out var error)
            .Should().BeTrue();

        fields.Should().Equal(new TypedOpcodeField("Subkey (SPCNM)", 0xFD, byte.MaxValue));
        bytes.Should().Equal([0x0F, 0xF6]);
        error.Should().BeEmpty();
    }

    [Fact]
    public void exposes_typed_fields_for_sound_opcode()
    {
        var fields = TypedOpcodeEditor.GetFields(
            new RawOpcode(0, 0xF1, "SOUND", 5, [0xF1, 10, 20, 30, 40], false));

        fields.Should().Equal(
            new TypedOpcodeField("Sound", 10, byte.MaxValue),
            new TypedOpcodeField("Volume", 20, byte.MaxValue),
            new TypedOpcodeField("Pan", 30, byte.MaxValue),
            new TypedOpcodeField("Channel", 40, byte.MaxValue));
    }

    [Fact]
    public void builds_music_sound_and_movie_bytes_from_typed_fields()
    {
        TypedOpcodeEditor.TryBuild(
                new RawOpcode(0, 0xF0, "MUSIC", 2, [0xF0, 0], false),
                [77],
                out var musicBytes,
                out var musicError)
            .Should().BeTrue();
        TypedOpcodeEditor.TryBuild(
                new RawOpcode(0, 0xF1, "SOUND", 5, [0xF1, 0, 0, 0, 0], false),
                [10, 20, 30, 40],
                out var soundBytes,
                out var soundError)
            .Should().BeTrue();
        TypedOpcodeEditor.TryBuild(
                new RawOpcode(0, 0xFA, "MVIEF", 3, [0xFA, 0, 0], false),
                [3, 1],
                out var movieBytes,
                out var movieError)
            .Should().BeTrue();

        musicBytes.Should().Equal([0xF0, 77]);
        soundBytes.Should().Equal([0xF1, 10, 20, 30, 40]);
        movieBytes.Should().Equal([0xFA, 3, 1]);
        musicError.Should().BeEmpty();
        soundError.Should().BeEmpty();
        movieError.Should().BeEmpty();
    }

    [Fact]
    public void exposes_typed_fields_for_model_animation_opcode()
    {
        var fields = TypedOpcodeEditor.GetFields(
            new RawOpcode(0, 0xB0, "CANIM1", 5, [0xB0, 7, 1, 12, 4], false));

        fields.Should().Equal(
            new TypedOpcodeField("Animation", 7, byte.MaxValue),
            new TypedOpcodeField("Start", 1, byte.MaxValue),
            new TypedOpcodeField("End", 12, byte.MaxValue),
            new TypedOpcodeField("Speed", 4, byte.MaxValue));
    }

    [Fact]
    public void builds_model_animation_bytes_from_typed_fields()
    {
        TypedOpcodeEditor.TryBuild(
                new RawOpcode(0, 0xA1, "CHAR", 2, [0xA1, 0], false),
                [5],
                out var characterBytes,
                out var characterError)
            .Should().BeTrue();
        TypedOpcodeEditor.TryBuild(
                new RawOpcode(0, 0xB0, "CANIM1", 5, [0xB0, 0, 0, 0, 0], false),
                [7, 1, 12, 4],
                out var animationBytes,
                out var animationError)
            .Should().BeTrue();

        characterBytes.Should().Equal([0xA1, 5]);
        animationBytes.Should().Equal([0xB0, 7, 1, 12, 4]);
        characterError.Should().BeEmpty();
        animationError.Should().BeEmpty();
    }
}

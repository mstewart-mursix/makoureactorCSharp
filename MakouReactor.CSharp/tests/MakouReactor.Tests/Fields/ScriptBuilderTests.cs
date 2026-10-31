using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Fields;

public sealed class ScriptBuilderTests
{
    [Fact]
    public void every_opcode_encodes_the_cpp_layout_and_decodes_with_its_declared_size()
    {
        var bytes = new ScriptBuilder()
            .ReqEw(groupId: 4, scriptId: 3, priority: 1)
            .Wait(0x1234)
            .Window(2, -8, 16, 200, 41)
            .Message(2, 7)
            .StItm(300, 9)
            .Battle(0x0102)
            .BitOn(8, 0xFF, 3)
            .BitOff(8, 0xFE, 7)
            .Char(5)
            .Xyzi(-100, 200, -3, 6)
            .Move(10, -20)
            .Dir(192)
            .Music(1)
            .Ret()
            .Compile();

        var opcodes = RawOpcodeReader.Read(bytes);
        opcodes.Select(op => op.Name).Should().Equal(
            "REQEW", "WAIT", "WINDOW", "MESSAGE", "STITM", "BATTLE", "BITON", "BITOFF",
            "CHAR", "XYZI", "MOVE", "DIR", "MUSIC", "RET");
        opcodes.Should().OnlyContain(op => !op.IsTruncated && op.Size == op.DeclaredSize);

        opcodes[0].Bytes.Should().Equal(0x03, 4, 3 | (1 << 5));
        opcodes[1].Bytes.Should().Equal(0x24, 0x34, 0x12);
        opcodes[2].Bytes.Should().Equal(0x50, 2, 0xF8, 0xFF, 16, 0, 200, 0, 41, 0);
        opcodes[3].Bytes.Should().Equal(0x40, 2, 7);
        opcodes[4].Bytes.Should().Equal(0x58, 0, 0x2C, 0x01, 9);
        opcodes[5].Bytes.Should().Equal(0x70, 0, 0x02, 0x01);
        opcodes[6].Bytes.Should().Equal(0x82, 0x80, 0xFF, 3);
        opcodes[7].Bytes.Should().Equal(0x83, 0x80, 0xFE, 7);
        opcodes[8].Bytes.Should().Equal(0xA1, 5);
        opcodes[9].Bytes.Should().Equal(0xA5, 0, 0, 0x9C, 0xFF, 0xC8, 0x00, 0xFD, 0xFF, 6, 0);
        opcodes[10].Bytes.Should().Equal(0xA8, 0, 10, 0, 0xEC, 0xFF);
        opcodes[11].Bytes.Should().Equal(0xB3, 0, 192);
        opcodes[12].Bytes.Should().Equal(0xF0, 1);
        opcodes[13].Bytes.Should().Equal(0x00);
    }

    [Fact]
    public void if_else_labels_resolve_to_forward_jumps_relative_to_the_opcode()
    {
        var builder = new ScriptBuilder();
        var elseLabel = builder.NewLabel();
        var endLabel = builder.NewLabel();
        var bytes = builder
            .IfBit(8, 0xFF, 2, bitOn: true, elseLabel)
            .Wait(1)
            .JumpForward(endLabel)
            .MarkLabel(elseLabel)
            .Wait(2)
            .MarkLabel(endLabel)
            .Ret()
            .Compile();

        var opcodes = RawOpcodeReader.Read(bytes);
        opcodes.Select(op => op.Name).Should().Equal("IFUB", "WAIT", "JMPF", "WAIT", "RET");

        // IFUB: banks = BANK(8, 0), value1 = address, value2 = bit, oper = BitOn (9).
        opcodes[0].Bytes[..5].Should().Equal(0x14, 0x80, 0xFF, 2, 9);
        // Opcode::jump() = stored + jumpShift (5 for IFUB, 1 for JMPF), relative to the opcode start.
        (opcodes[0].Offset + 5 + opcodes[0].Bytes[5]).Should().Be(opcodes[3].Offset);
        (opcodes[2].Offset + 1 + opcodes[2].Bytes[1]).Should().Be(opcodes[4].Offset);
    }

    [Fact]
    public void jumps_widen_to_long_forms_when_the_target_is_out_of_byte_range()
    {
        var builder = new ScriptBuilder();
        var elseLabel = builder.NewLabel();
        var endLabel = builder.NewLabel();
        builder.IfBit(8, 0x10, 0, bitOn: false, elseLabel);
        for (var i = 0; i < 100; i++)
            builder.Wait(1);
        builder.JumpForward(endLabel).MarkLabel(elseLabel);
        for (var i = 0; i < 100; i++)
            builder.Wait(2);
        builder.MarkLabel(endLabel).Ret();

        var opcodes = RawOpcodeReader.Read(builder.Compile());
        var ifub = opcodes[0];
        ifub.Name.Should().Be("IFUBL");
        ifub.Bytes[4].Should().Be(10); // BitOff
        var jump = opcodes.Single(op => op.Name == "JMPFL");
        var elseTarget = ifub.Offset + 5 + (ifub.Bytes[5] | (ifub.Bytes[6] << 8));
        var endTarget = jump.Offset + 1 + (jump.Bytes[1] | (jump.Bytes[2] << 8));
        elseTarget.Should().Be(jump.Offset + jump.Size);
        endTarget.Should().Be(opcodes[^1].Offset);
        opcodes[^1].Name.Should().Be("RET");
    }

    [Fact]
    public void append_renumbers_labels()
    {
        var inner = new ScriptBuilder();
        var innerLabel = inner.NewLabel();
        inner.IfBit(8, 0xFF, 0, bitOn: true, innerLabel).Wait(3).MarkLabel(innerLabel);

        var outer = new ScriptBuilder();
        var outerLabel = outer.NewLabel();
        var bytes = outer
            .IfBit(8, 0xFF, 1, bitOn: false, outerLabel)
            .Append(inner)
            .MarkLabel(outerLabel)
            .Ret()
            .Compile();

        var opcodes = RawOpcodeReader.Read(bytes);
        opcodes.Select(op => op.Name).Should().Equal("IFUB", "IFUB", "WAIT", "RET");
        (opcodes[0].Offset + 5 + opcodes[0].Bytes[5]).Should().Be(opcodes[3].Offset);
        (opcodes[1].Offset + 5 + opcodes[1].Bytes[5]).Should().Be(opcodes[3].Offset);
    }

    [Fact]
    public void missing_label_is_reported()
    {
        var builder = new ScriptBuilder();
        builder.JumpForward(builder.NewLabel());

        var act = () => builder.Compile();

        act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }
}

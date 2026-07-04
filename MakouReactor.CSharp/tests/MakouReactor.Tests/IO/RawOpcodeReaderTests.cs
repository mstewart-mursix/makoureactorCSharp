using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public class RawOpcodeReaderTests
{
    [Fact]
    public void read_uses_original_opcode_lengths_and_names()
    {
        var opcodes = RawOpcodeReader.Read([0x24, 0x34, 0x12, 0x00]);

        opcodes.Should().HaveCount(2);
        opcodes[0].Offset.Should().Be(0);
        opcodes[0].Name.Should().Be("WAIT");
        opcodes[0].DeclaredSize.Should().Be(3);
        opcodes[0].Bytes.Should().Equal([0x24, 0x34, 0x12]);
        opcodes[0].IsTruncated.Should().BeFalse();
        opcodes[1].Offset.Should().Be(3);
        opcodes[1].Name.Should().Be("RET");
        opcodes[1].Bytes.Should().Equal([0x00]);
    }

    [Fact]
    public void read_keeps_truncated_opcode_as_final_row()
    {
        var opcodes = RawOpcodeReader.Read([0x50, 0x01, 0x02]);

        opcodes.Should().ContainSingle();
        opcodes[0].Name.Should().Be("WINDOW");
        opcodes[0].DeclaredSize.Should().Be(10);
        opcodes[0].Size.Should().Be(3);
        opcodes[0].IsTruncated.Should().BeTrue();
        opcodes[0].Warning.Should().Contain("Truncated opcode");
    }

    [Fact]
    public void raw_opcode_exposes_typed_arguments_for_known_opcodes()
    {
        var opcodes = RawOpcodeReader.Read([0x24, 0x34, 0x12, 0x40, 0x02, 0x05]);

        opcodes[0].Arguments.Should().Be("duration=4660");
        opcodes[1].Name.Should().Be("MESSAGE");
        opcodes[1].Arguments.Should().Be("window=2, text=5");
    }

    [Fact]
    public void script_exposes_raw_opcode_rows()
    {
        var script = new Script([0x5F, 0x00]);

        script.RawOpcodes.Select(opcode => opcode.Name)
            .Should().Equal("NOP", "RET");
    }
}

using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

/// <summary>
/// Audit of <see cref="RawOpcodeReader"/> against <c>Opcode::fixedSize()</c> / <c>Opcode::size()</c>
/// in <c>src/core/field/Opcode.cpp</c>: SPECIAL (0x0F) sizes depend on the sub-opcode, KAWAI (0x28)
/// carries its own total size, and Unused1C (0x1C) carries a trailing data size.
/// </summary>
public class RawOpcodeLengthAuditTests
{
    [Theory]
    [InlineData(0xF5, 3)] // ARROW
    [InlineData(0xF6, 3)] // PNAME
    [InlineData(0xF7, 3)] // GMSPD
    [InlineData(0xF8, 4)] // SMSPD
    [InlineData(0xF9, 2)] // FLMAT
    [InlineData(0xFA, 2)] // FLITM
    [InlineData(0xFB, 3)] // BTLCK
    [InlineData(0xFC, 3)] // MVLCK
    [InlineData(0xFD, 4)] // SPCNM
    [InlineData(0xFE, 2)] // RSGLB
    [InlineData(0xFF, 2)] // CLITM
    public void special_opcode_size_depends_on_sub_opcode(byte subKey, int expectedSize)
    {
        var script = new List<byte> { 0x0F, subKey };
        script.AddRange(Enumerable.Repeat((byte)0x7F, expectedSize - 2));
        script.Add(0x00); // RET

        var opcodes = RawOpcodeReader.Read(script.ToArray());

        opcodes.Should().HaveCount(2);
        opcodes[0].Name.Should().Be("SPECIAL");
        opcodes[0].Size.Should().Be(expectedSize);
        opcodes[0].IsTruncated.Should().BeFalse();
        opcodes[1].Name.Should().Be("RET");
    }

    [Fact]
    public void kawai_opcode_uses_embedded_total_size()
    {
        // KAWAI: id, opcodeSize (total, including id), subKey, parameters...
        var script = new byte[] { 0x28, 0x07, 0x00, 0x01, 0x02, 0x03, 0x04, 0x00 };

        var opcodes = RawOpcodeReader.Read(script);

        opcodes.Should().HaveCount(2);
        opcodes[0].Name.Should().Be("KAWAI");
        opcodes[0].Size.Should().Be(7);
        opcodes[1].Name.Should().Be("RET");
    }

    [Fact]
    public void kawai_opcode_with_zero_size_consumes_one_byte()
    {
        // Opcode::size(): std::max(opcodeSize, 1).
        var script = new byte[] { 0x28, 0x00, 0x00 };

        var opcodes = RawOpcodeReader.Read(script);

        opcodes.Select(opcode => opcode.Name).Should().Equal("KAWAI", "RET", "RET");
        opcodes[0].Size.Should().Be(1);
    }

    [Fact]
    public void unused1c_opcode_appends_sub_size_bytes()
    {
        // Unused1C: id, u32 address, u8 subSize, then subSize bytes (capped at 128).
        var script = new byte[] { 0x1C, 0x00, 0x00, 0x00, 0x00, 0x03, 0xAA, 0xBB, 0xCC, 0x00 };

        var opcodes = RawOpcodeReader.Read(script);

        opcodes.Should().HaveCount(2);
        opcodes[0].Size.Should().Be(9);
        opcodes[1].Name.Should().Be("RET");
    }

    [Fact]
    public void unused1c_sub_size_is_capped_at_128()
    {
        var script = new List<byte> { 0x1C, 0, 0, 0, 0, 0xFF };
        script.AddRange(Enumerable.Repeat((byte)0x01, 128));
        script.Add(0x00);

        var opcodes = RawOpcodeReader.Read(script.ToArray());

        opcodes[0].Size.Should().Be(6 + 128);
        opcodes[1].Name.Should().Be("RET");
    }
}

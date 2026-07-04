using FluentAssertions;

using MakouReactor.UI.WPF.Scripts;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Scripts;

public sealed class ScriptOpcodeTreeBuilderTests
{
    [Fact]
    public void build_creates_script_root_and_labeled_opcode_children()
    {
        OpcodeProjection[] opcodes =
        [
            new("0x0000", "IFUB", "var=1"),
            new("0x0005", "JMPF", "target=0x0010"),
            new("0x0008", "RET", string.Empty),
            new("0x0009", "WINDOW", "window=0, text=1"),
        ];

        var roots = ScriptOpcodeTreeBuilder.Build(
            "S0 - Main",
            opcodes,
            static opcode => opcode.Offset,
            static opcode => opcode.Name,
            static opcode => opcode.Arguments);

        roots.Should().ContainSingle();
        roots[0].Text.Should().Be("S0 - Main (4 opcode(s))");
        roots[0].Children.Select(static child => child.Text).Should().Equal(
            "Condition - 0x0000: IFUB - var=1",
            "Branch - 0x0005: JMPF - target=0x0010",
            "Return - 0x0008: RET",
            "Opcode - 0x0009: WINDOW - window=0, text=1");
        roots[0].Children[0].OpcodeItem.Should().BeSameAs(opcodes[0]);
    }

    [Theory]
    [InlineData("IFSW", "Condition - ")]
    [InlineData("MAPJUMP", "Branch - ")]
    [InlineData("REQ", "Branch - ")]
    [InlineData("RETTO", "Return - ")]
    [InlineData("PLUS!", "Opcode - ")]
    public void prefix_classifies_opcode_names(string opcodeName, string expectedPrefix)
    {
        ScriptOpcodeTreeBuilder.Prefix(opcodeName).Should().Be(expectedPrefix);
    }

    private sealed record OpcodeProjection(string Offset, string Name, string Arguments);
}

using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.Core.Services;

using Xunit;

namespace MakouReactor.Tests.Search;

public sealed class VariableReferenceScannerTests
{
    [Fact]
    public void scan_finds_variable_references_in_binary_operations()
    {
        var section = BuildSection([0x80, 0x21, 0x10, 0x20]);

        var references = new VariableReferenceScanner().Scan(section);

        references.Should().HaveCount(2);
        references[0].OpcodeName.Should().Be("SETBYTE");
        references[0].Bank.Should().Be(0x1);
        references[0].Address.Should().Be(0x10);
        references[0].Writable.Should().BeTrue();
        references[1].Bank.Should().Be(0x2);
        references[1].Address.Should().Be(0x20);
        references[1].Writable.Should().BeFalse();
    }

    [Fact]
    public void scan_ignores_constant_operands_with_zero_bank()
    {
        var section = BuildSection([0x80, 0x01, 0x10, 0x20]);

        var references = new VariableReferenceScanner().Scan(section);

        references.Should().ContainSingle();
        references[0].Bank.Should().Be(0x1);
        references[0].Address.Should().Be(0x10);
    }

    [Fact]
    public void scan_finds_unary_operation_reference()
    {
        var section = BuildSection([0x95, 0x30, 0x44]);

        var references = new VariableReferenceScanner().Scan(section);

        references.Should().ContainSingle();
        references[0].OpcodeName.Should().Be("INC");
        references[0].Bank.Should().Be(0x3);
        references[0].Address.Should().Be(0x44);
        references[0].Size.Should().Be("Byte");
    }

    private static Section1File BuildSection(byte[] scriptBytes)
    {
        var section = new Section1File();
        var group = new GrpScript("cloud");
        group.SetScript(0, new Script(scriptBytes));
        section.GrpScripts.Add(group);
        return section;
    }
}

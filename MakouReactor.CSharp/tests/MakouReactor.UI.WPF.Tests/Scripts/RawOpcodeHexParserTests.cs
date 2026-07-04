using FluentAssertions;

using MakouReactor.UI.WPF.Scripts;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Scripts;

public sealed class RawOpcodeHexParserTests
{
    [Fact]
    public void parse_accepts_hex_bytes_with_common_separators()
    {
        var ok = RawOpcodeHexParser.TryParse("40 00, 02", 3, out var bytes, out var error);

        ok.Should().BeTrue();
        bytes.Should().Equal([0x40, 0x00, 0x02]);
        error.Should().BeEmpty();
    }

    [Fact]
    public void parse_rejects_wrong_length()
    {
        var ok = RawOpcodeHexParser.TryParse("40 00", 3, out _, out var error);

        ok.Should().BeFalse();
        error.Should().Be("Expected 3 byte(s), got 2.");
    }

    [Fact]
    public void parse_rejects_invalid_hex()
    {
        var ok = RawOpcodeHexParser.TryParse("40 ZZ 02", 3, out _, out var error);

        ok.Should().BeFalse();
        error.Should().Be("'ZZ' is not a valid hex byte.");
    }
}

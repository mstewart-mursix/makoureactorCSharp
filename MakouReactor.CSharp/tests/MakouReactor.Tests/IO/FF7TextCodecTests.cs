using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public class FF7TextCodecTests
{
    [Fact]
    public void decode_stops_at_terminator()
    {
        FF7TextCodec.Decode([0x28, 0x49, 0xFF, 0x21])
            .Should().Be("Hi");
    }

    [Fact]
    public void decode_names_known_control_tokens()
    {
        FF7TextCodec.Decode([0x21, 0xE8, 0x22, 0xFF])
            .Should().Be("A\n{NEW PAGE}\nB");
    }

    [Fact]
    public void encode_round_trips_unknown_hex_escapes()
    {
        var encoded = FF7TextCodec.Encode("A{x5F}\n{NEW PAGE}\nB");

        encoded.Should().Equal([0x21, 0x5F, 0xE8, 0x22, 0xFF]);
        FF7TextCodec.Decode(encoded).Should().Be("A{x5F}\n{NEW PAGE}\nB");
    }

    [Fact]
    public void decode_names_character_and_color_tokens()
    {
        FF7TextCodec.Decode([0xFE, 0xD4, 0xEA, 0x00, 0xEC, 0xFE, 0xD9, 0xFF])
            .Should().Be("{RED}{CLOUD} {TIFA}{WHITE}");
    }

    [Fact]
    public void encode_accepts_text_manager_control_token_aliases()
    {
        var encoded = FF7TextCodec.Encode("{NEW PAGE}{AERIS}{CIRCLE}{CHOICE}");

        encoded.Should().Equal([0xE8, 0xED, 0xF6, 0xE0, 0xFF]);
    }

    [Fact]
    public void encode_accepts_color_and_unknown_tokens()
    {
        var encoded = FF7TextCodec.Encode("{BLUE}Hi{xE5}{WHITE}");

        encoded.Should().Equal([0xFE, 0xD3, 0x28, 0x49, 0xE5, 0xFE, 0xD9, 0xFF]);
        FF7TextCodec.Decode(encoded).Should().Be("{BLUE}Hi{xE5}{WHITE}");
    }

    [Fact]
    public void japanese_mode_decodes_japanese_table_bytes()
    {
        FF7TextCodec.Decode([0x00, 0x01, 0xEA, 0xFF], japanese: true)
            .Should().Be("バば{CLOUD}");
        FF7TextCodec.Decode([0x00, 0x01, 0xEA, 0xFF])
            .Should().Be(" !{CLOUD}");
    }

    [Fact]
    public void encode_round_trips_japanese_text()
    {
        var encoded = FF7TextCodec.Encode("バば{CLOUD}", japanese: true);

        encoded.Should().Equal([0x00, 0x01, 0xEA, 0xFF]);
        FF7TextCodec.Decode(encoded, japanese: true).Should().Be("バば{CLOUD}");
    }

    [Fact]
    public void find_unsupported_tokens_reports_unknown_brace_tokens()
    {
        FF7TextCodec.FindUnsupportedTokens("{CLOUD} uses {NOT_A_TOKEN} and {x0A}")
            .Should().Equal("{NOT_A_TOKEN}");
    }
}

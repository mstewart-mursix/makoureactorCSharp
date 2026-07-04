using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public class FF7TextCodecTests
{
    [Fact]
    public void decode_stops_at_terminator()
    {
        FF7TextCodec.Decode([0x48, 0x69, 0xFF, 0x21])
            .Should().Be("Hi");
    }

    [Fact]
    public void decode_names_known_control_tokens()
    {
        FF7TextCodec.Decode([0x41, 0xED, 0x42, 0xFF])
            .Should().Be("A{NEW}B");
    }

    [Fact]
    public void encode_round_trips_unknown_hex_escapes()
    {
        var encoded = FF7TextCodec.Encode("A{x01}{NEW}B");

        encoded.Should().Equal([0x41, 0x01, 0xED, 0x42, 0xFF]);
        FF7TextCodec.Decode(encoded).Should().Be("A{x01}{NEW}B");
    }

    [Fact]
    public void decode_names_character_and_color_tokens()
    {
        FF7TextCodec.Decode([0xD4, 0xEE, 0x20, 0xF0, 0xD9, 0xFF])
            .Should().Be("{RED}{CLOUD} {TIFA}{WHITE}");
    }

    [Fact]
    public void encode_accepts_text_manager_control_token_aliases()
    {
        var encoded = FF7TextCodec.Encode("{NEW PAGE}{EOL}{AERIS}{CIRCLE}{CHOICE}");

        encoded.Should().Equal([0xED, 0xED, 0xF1, 0xF6, 0xE0, 0xFF]);
    }

    [Fact]
    public void encode_accepts_color_and_unknown_tokens()
    {
        var encoded = FF7TextCodec.Encode("{BLUE}Hi{x03}{WHITE}");

        encoded.Should().Equal([0xD3, 0x48, 0x69, 0x03, 0xD9, 0xFF]);
        FF7TextCodec.Decode(encoded).Should().Be("{BLUE}Hi{x03}{WHITE}");
    }

    [Fact]
    public void japanese_mode_decodes_halfwidth_kana_bytes()
    {
        FF7TextCodec.Decode([0xA6, 0xA7, 0xED, 0xFF], japanese: true)
            .Should().Be("ｦｧ{NEW}");
        FF7TextCodec.Decode([0xA6, 0xA7, 0xED, 0xFF])
            .Should().Be("{xA6}{xA7}{NEW}");
    }

    [Fact]
    public void encode_round_trips_japanese_halfwidth_kana()
    {
        var encoded = FF7TextCodec.Encode("ｦｧ{NEW}");

        encoded.Should().Equal([0xA6, 0xA7, 0xED, 0xFF]);
        FF7TextCodec.Decode(encoded, japanese: true).Should().Be("ｦｧ{NEW}");
    }

    [Fact]
    public void find_unsupported_tokens_reports_unknown_brace_tokens()
    {
        FF7TextCodec.FindUnsupportedTokens("{CLOUD} uses {NOT_A_TOKEN} and {x0A}")
            .Should().Equal("{NOT_A_TOKEN}");
    }
}

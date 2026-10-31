using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

/// <summary>
/// Audit of <see cref="FF7TextCodec"/> against ff7tk <c>FF7Text</c> (used by the C++ reference for
/// all text conversion) and the byte handling in <c>src/core/FF7Font.cpp</c> /
/// <c>src/widgets/TextPreview.cpp</c>.
/// </summary>
public class FF7TextCodecAuditTests
{
    [Fact]
    public void characters_are_offset_by_0x20_from_ascii()
    {
        FF7TextCodec.Decode([0x21, 0x41, 0x00, 0x10, 0x5E, 0xFF]).Should().Be("Aa 0~");
        FF7TextCodec.Encode("Hello").Should().Equal(0x28, 0x45, 0x4C, 0x4C, 0x4F, 0xFF);
    }

    [Fact]
    public void every_international_character_round_trips()
    {
        for (var value = 0; value < 0xE0; value++)
        {
            var bytes = new[] { (byte)value };
            var text = FF7TextCodec.Decode(bytes);
            FF7TextCodec.Encode(text, includeTerminator: false).Should().Equal(
                RoundTripExpectation(value), $"byte 0x{value:X2} decoded as '{text}'");
        }
    }

    [Fact]
    public void extended_characters_use_ff7_table()
    {
        FF7TextCodec.Decode([0x60, 0x63, 0x87, 0xA9, 0xFF]).Should().Be("ÄÉß…");
        FF7TextCodec.Encode("Éé", includeTerminator: false).Should().Equal(0x63, 0x6E);
    }

    [Fact]
    public void newline_and_new_pages_match_ff7tk()
    {
        FF7TextCodec.Decode([0x41, 0xE7, 0x42]).Should().Be("a\nb");
        FF7TextCodec.Decode([0x41, 0xE8, 0x42]).Should().Be("a\n{NEW PAGE}\nb");
        FF7TextCodec.Decode([0x41, 0xE9, 0x42]).Should().Be("a\n{NEW PAGE 2}\nb");

        FF7TextCodec.Encode("a\nb", includeTerminator: false).Should().Equal(0x41, 0xE7, 0x42);
        FF7TextCodec.Encode("a\n{NEW PAGE}\nb", includeTerminator: false).Should().Equal(0x41, 0xE8, 0x42);
        FF7TextCodec.Encode("a\n{NEW PAGE 2}\nb", includeTerminator: false).Should().Equal(0x41, 0xE9, 0x42);
    }

    [Theory]
    [InlineData(0xE0, "{CHOICE}")]
    [InlineData(0xE1, "\t")]
    [InlineData(0xEA, "{CLOUD}")]
    [InlineData(0xEB, "{BARRET}")]
    [InlineData(0xEC, "{TIFA}")]
    [InlineData(0xED, "{AERITH}")]
    [InlineData(0xEE, "{RED XIII}")]
    [InlineData(0xEF, "{YUFFIE}")]
    [InlineData(0xF0, "{CAIT SITH}")]
    [InlineData(0xF1, "{VINCENT}")]
    [InlineData(0xF2, "{CID}")]
    [InlineData(0xF3, "{MEMBER 1}")]
    [InlineData(0xF4, "{MEMBER 2}")]
    [InlineData(0xF5, "{MEMBER 3}")]
    [InlineData(0xF6, "{CIRCLE}")]
    [InlineData(0xF7, "{TRIANGLE}")]
    [InlineData(0xF8, "{SQUARE}")]
    [InlineData(0xF9, "{CROSS}")]
    public void single_byte_controls_round_trip(byte value, string token)
    {
        FF7TextCodec.Decode([value]).Should().Be(token);
        FF7TextCodec.Encode(token, includeTerminator: false).Should().Equal(value);
    }

    [Theory]
    [InlineData(0xE2, ", ")]
    [InlineData(0xE3, ".\"")]
    [InlineData(0xE4, "…\"")]
    public void two_character_glyphs_decode_and_encode_like_ff7tk(byte value, string text)
    {
        FF7TextCodec.Decode([value]).Should().Be(text);
        FF7TextCodec.Encode(text, includeTerminator: false).Should().Equal(value);
    }

    [Theory]
    [InlineData(0xD2, "{GREY}")]
    [InlineData(0xD3, "{BLUE}")]
    [InlineData(0xD4, "{RED}")]
    [InlineData(0xD5, "{PURPLE}")]
    [InlineData(0xD6, "{GREEN}")]
    [InlineData(0xD7, "{CYAN}")]
    [InlineData(0xD8, "{YELLOW}")]
    [InlineData(0xD9, "{WHITE}")]
    [InlineData(0xDA, "{BLINK}")]
    [InlineData(0xDB, "{MULTICOLOUR}")]
    [InlineData(0xDC, "{PAUSE}")]
    [InlineData(0xDE, "{VARDEC}")]
    [InlineData(0xDF, "{VARHEX}")]
    [InlineData(0xE0, "{SCROLLING}")]
    [InlineData(0xE1, "{VARDECR}")]
    [InlineData(0xE8, "{SPACED CHARACTERS}")]
    public void fe_prefixed_controls_round_trip(byte value, string token)
    {
        FF7TextCodec.Decode([0xFE, value]).Should().Be(token);
        FF7TextCodec.Encode(token, includeTerminator: false).Should().Equal(0xFE, value);
    }

    [Fact]
    public void pause_with_argument_uses_three_bytes()
    {
        FF7TextCodec.Decode([0xFE, 0xDD, 0x05, 0x21]).Should().Be("{PAUSE005}A");
        FF7TextCodec.Encode("{PAUSE030}A", includeTerminator: false).Should().Equal(0xFE, 0xDD, 30, 0x21);
    }

    [Fact]
    public void terminator_inside_multi_byte_sequence_is_an_argument()
    {
        FF7TextCodec.Decode([0xFE, 0xDD, 0xFF, 0x21, 0xFF, 0x22]).Should().Be("{PAUSE255}A");
        FF7TextCodec.FindTerminator([0xFE, 0xDD, 0xFF, 0x21, 0xFF, 0x22]).Should().Be(4);
    }

    [Fact]
    public void unknown_bytes_and_sequences_round_trip_as_hex_escapes()
    {
        byte[] raw = [0x5F, 0xE5, 0xFE, 0xE2, 0x01, 0x02, 0x03, 0x04, 0xFA, 0x10, 0x21];

        var text = FF7TextCodec.Decode(raw);

        text.Should().Be("{x5F}{xE5}{xFE}{xE2}{x01}{x02}{x03}{x04}{xFA}{x10}A");
        FF7TextCodec.Encode(text, includeTerminator: false).Should().Equal(raw);
    }

    [Fact]
    public void truncated_prefix_decodes_as_escape()
    {
        FF7TextCodec.Decode([0x21, 0xFE]).Should().Be("A{xFE}");
    }

    [Fact]
    public void decode_stops_at_terminator()
    {
        FF7TextCodec.Decode([0x28, 0x49, 0xFF, 0x21]).Should().Be("Hi");
    }

    [Fact]
    public void japanese_tables_and_pages_round_trip()
    {
        byte[] raw = [0x00, 0xFA, 0x00, 0xFE, 0x00, 0xE7, 0xEA];

        var text = FF7TextCodec.Decode(raw, japanese: true);

        text.Should().Be("バ必孫\n{CLOUD}");
        FF7TextCodec.Encode(text, includeTerminator: false, japanese: true).Should().Equal(raw);
    }

    [Fact]
    public void crlf_from_editors_encodes_as_line_feed_and_new_page()
    {
        FF7TextCodec.Encode("a\r\nb\r\n{NEW PAGE}\r\nc", includeTerminator: false)
            .Should().Equal(0x41, 0xE7, 0x42, 0xE8, 0x43);
    }

    [Fact]
    public void literal_braces_that_are_not_tokens_encode_as_characters()
    {
        FF7TextCodec.Encode("{a}", includeTerminator: false).Should().Equal(0x5B, 0x41, 0x5D);
    }

    [Fact]
    public void aliases_used_by_text_manager_are_accepted()
    {
        FF7TextCodec.Encode("{AERIS}{NEW PAGE}{NEW PAGE 2}", includeTerminator: false)
            .Should().Equal(0xED, 0xE8, 0xE9);
    }

    private static byte[] RoundTripExpectation(int value) => value switch
    {
        0x5F or 0xAA or 0xD0 or (>= 0xD4 and <= 0xDF) => [(byte)value], // empty slots -> {xNN}
        0xD2 => [0x84], // duplicate "Ù": encode picks the first occurrence
        0xD3 => [0x85], // duplicate "Û"
        _ => [(byte)value],
    };
}

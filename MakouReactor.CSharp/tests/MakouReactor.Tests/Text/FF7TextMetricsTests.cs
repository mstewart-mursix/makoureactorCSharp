using FluentAssertions;

using MakouReactor.Core.Text;

using Xunit;

namespace MakouReactor.Tests.Text;

/// <summary>
/// Expected values are worked by hand from FF7Font::calcSize (src/core/FF7Font.cpp) with the
/// default right margin of 14: an empty window is 8 + 14 - 3 = 19 px wide and 25 px tall.
/// Bytes are raw FF7 text ('H' = 0x28, 'i' = 0x49, 'W' = 0x37, newline = 0xE7).
/// </summary>
public class FF7TextMetricsTests
{
    private static readonly byte[] Hi = [0x28, 0x49];

    [Theory]
    [InlineData(0x00, 3)]   // space: width 3, no padding
    [InlineData(0x02, 10)]  // '"': table 72 = width 8 + padding 2
    [InlineData(0x0C, 8)]   // ',': table 39 = width 7 + padding 1
    [InlineData(0x28, 8)]   // 'H'
    [InlineData(0x49, 3)]   // 'i'
    [InlineData(0x37, 11)]  // 'W'
    public void char_full_width_decodes_width_and_padding(byte c, int expected)
    {
        FF7TextMetrics.CharFullWidth(c).Should().Be(expected);
    }

    [Fact]
    public void empty_text_is_base_window()
    {
        FF7TextMetrics.CalcWindowSize([0xFF]).Should().Be((19, 25));
    }

    [Fact]
    public void single_line_adds_character_widths()
    {
        FF7TextMetrics.CalcWindowSize([.. Hi, 0xFF]).Should().Be((19 + 8 + 3, 25));
    }

    [Fact]
    public void newline_adds_line_height_and_first_line_bonus()
    {
        // line 1: 19 + 11 + 3 (first-line bonus) = 33; line 2: 22 + 11 = 33
        FF7TextMetrics.CalcWindowSize([.. Hi, 0xE7, .. Hi, 0xFF]).Should().Be((33, 41));
    }

    [Fact]
    public void new_page_resets_height()
    {
        FF7TextMetrics.CalcWindowSize([.. Hi, 0xE7, .. Hi, 0xE8, .. Hi, 0xFF]).Height.Should().Be(41);
    }

    [Fact]
    public void stops_at_terminator()
    {
        FF7TextMetrics.CalcWindowSize([.. Hi, 0xFF, 0x37, 0x37, 0x37]).Should().Be((30, 25));
    }

    [Fact]
    public void character_name_reserves_nine_widest_glyphs()
    {
        FF7TextMetrics.CalcWindowSize([0xEA, 0xFF]).Width.Should().Be(19 + 9 * 11);
    }

    [Fact]
    public void wide_line_overflows_and_is_capped()
    {
        var thirtyWs = Enumerable.Repeat((byte)0x37, 30).Append((byte)0xFF).ToArray();

        FF7TextMetrics.Fits(thirtyWs).Should().BeFalse();
        FF7TextMetrics.CalcUncappedSize(thirtyWs).Width.Should().Be(19 + 30 * 11);
        FF7TextMetrics.CalcWindowSize(thirtyWs).Width.Should().Be(FF7TextMetrics.MaxWindowWidth);
        FF7TextMetrics.Fits([.. Hi, 0xFF]).Should().BeTrue();
    }

    [Fact]
    public void too_many_lines_overflow_height()
    {
        var lines = new List<byte>();
        for (var i = 0; i < 14; i++)
            lines.AddRange([.. Hi, 0xE7]);
        lines.Add(0xFF);

        FF7TextMetrics.Fits(lines.ToArray()).Should().BeFalse();
        FF7TextMetrics.CalcWindowSize(lines.ToArray()).Height.Should().Be(FF7TextMetrics.MaxWindowHeight);
    }

    [Fact]
    public void pause_and_spaced_controls_are_handled()
    {
        // {PAUSE} = FE DD + 1 arg byte (no width); {SPACED CHARACTERS} toggles 13px per glyph
        FF7TextMetrics.CalcWindowSize([0xFE, 0xDD, 0x10, .. Hi, 0xFF]).Width.Should().Be(30);
        FF7TextMetrics.CalcWindowSize([0xFE, 0xE9, .. Hi, 0xFF]).Width.Should().Be(19 + 2 * 13);
    }
}

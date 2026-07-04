using System.Text;

using FluentAssertions;

using MakouReactor.Core.IO;

using Xunit;

namespace MakouReactor.Tests.IO;

public class LzsCompressionTests
{
    [Fact]
    public void decompresses_literal_tokens()
    {
        var compressed = new byte[] { 0x07, (byte)'A', (byte)'B', (byte)'C' };

        var decompressed = LzsCompression.Decompress(compressed);

        Encoding.ASCII.GetString(decompressed).Should().Be("ABC");
    }

    [Fact]
    public void decompresses_reference_tokens()
    {
        var compressed = new byte[]
        {
            0x07,
            (byte)'A',
            (byte)'B',
            (byte)'C',
            0xEE,
            0xF3,
        };

        var decompressed = LzsCompression.Decompress(compressed);

        Encoding.ASCII.GetString(decompressed).Should().Be("ABCABCABC");
    }

    [Fact]
    public void compress_round_trips_structured_payload()
    {
        var payload = Encoding.ASCII.GetBytes(
            "AAAAABBBBBCCCCCAAAAABBBBBCCCCC field script text field script text");

        var compressed = LzsCompression.Compress(payload);
        var decompressed = LzsCompression.Decompress(compressed);

        decompressed.Should().Equal(payload);
    }

    [Fact]
    public void compress_round_trips_all_byte_values()
    {
        var payload = Enumerable.Range(0, 2048)
            .Select(i => (byte)(i & 0xFF))
            .ToArray();

        var compressed = LzsCompression.Compress(payload);
        var decompressed = LzsCompression.Decompress(compressed);

        decompressed.Should().Equal(payload);
    }

    [Fact]
    public void compress_round_trips_empty_payload()
    {
        var compressed = LzsCompression.Compress([]);
        var decompressed = LzsCompression.Decompress(compressed);

        decompressed.Should().BeEmpty();
    }
}

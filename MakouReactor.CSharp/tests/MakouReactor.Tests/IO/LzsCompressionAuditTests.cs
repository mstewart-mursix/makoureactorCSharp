using System.Buffers.Binary;
using System.Text;

using FluentAssertions;

using MakouReactor.Core.IO;

using Xunit;

namespace MakouReactor.Tests.IO;

/// <summary>
/// Audit of <see cref="LzsCompression"/> against the ff7tk <c>LZS</c> implementation used by
/// the C++ reference (<c>LZS::decompress</c>: 4096-byte ring zero-filled, write cursor starting
/// at 4078, length = (nibble + 3), byte-by-byte ring copy).
/// </summary>
public class LzsCompressionAuditTests
{
    [Fact]
    public void decompress_empty_stream_returns_empty()
    {
        LzsCompression.Decompress([]).Should().BeEmpty();
    }

    [Fact]
    public void decompress_flag_byte_without_tokens_returns_empty()
    {
        LzsCompression.Decompress([0xFF]).Should().BeEmpty();
    }

    [Fact]
    public void decompress_reference_into_initial_ring_yields_zero_bytes()
    {
        // Flag 0 = reference; offset 0x000, length nibble 0 -> 3 bytes from the zero-filled ring.
        LzsCompression.Decompress([0x00, 0x00, 0x00]).Should().Equal(0, 0, 0);
    }

    [Fact]
    public void decompress_reference_wraps_around_ring_end()
    {
        // 18 literals fill ring[4078..4095]; the reference at 4094 then reads 4094, 4095, 0, 1, 2
        // while ring[0..] is being overwritten by the same copy (C++ copies byte by byte).
        var stream = new List<byte>();
        var literals = Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOPQR");
        for (var i = 0; i < 16; i += 8)
        {
            stream.Add(0xFF);
            stream.AddRange(literals.Skip(i).Take(8));
        }

        stream.Add(0x03); // two literals then a reference
        stream.AddRange(literals.Skip(16));
        stream.Add(0xFE); // offset low byte (0xFFE & 0xFF)
        stream.Add(0xF2); // offset high nibble 0xF, length nibble 2 -> 5 bytes

        var output = LzsCompression.Decompress(stream.ToArray());

        Encoding.ASCII.GetString(output).Should().Be("ABCDEFGHIJKLMNOPQR" + "QRQRQ");
    }

    [Fact]
    public void decompress_max_length_overlapping_reference_repeats_byte()
    {
        // Literal 'Z' at ring 4078, then reference offset 4078 (0xFEE) length 18 (nibble 0xF).
        var output = LzsCompression.Decompress([0x01, (byte)'Z', 0xEE, 0xFF]);

        output.Should().Equal(Enumerable.Repeat((byte)'Z', 19));
    }

    [Fact]
    public void decompress_stops_cleanly_after_trailing_flag_byte()
    {
        var stream = new byte[] { 0xFF, 1, 2, 3, 4, 5, 6, 7, 8, 0x00 };

        LzsCompression.Decompress(stream).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
    }

    [Fact]
    public void decompress_rejects_truncated_reference_token()
    {
        // The C++ reads past the buffer here (undefined behaviour); the port reports it.
        var act = () => LzsCompression.Decompress([0x00, 0x12]);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void decompress_with_header_honours_declared_size()
    {
        var payload = Encoding.ASCII.GetBytes("header-test");
        var compressed = LzsCompression.CompressWithHeader(payload);
        var withGarbage = compressed.Concat(new byte[] { 0x00, 0x00, 0x00 }).ToArray();

        LzsCompression.DecompressWithHeader(withGarbage).Should().Equal(payload);
    }

    [Fact]
    public void decompress_with_header_returns_uncompressed_0x90000_data_unchanged()
    {
        // Field::open: "if (size != lzsSize + 4 && lzsSize == 0x90000) // Maybe it is not compressed".
        var data = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0x90000);
        data[10] = 0x42;

        LzsCompression.DecompressWithHeader(data).Should().Equal(data);
    }

    [Fact]
    public void compress_with_header_writes_compressed_length_prefix()
    {
        var payload = Enumerable.Repeat((byte)7, 100).ToArray();

        var compressed = LzsCompression.CompressWithHeader(payload);

        BinaryPrimitives.ReadUInt32LittleEndian(compressed).Should().Be((uint)(compressed.Length - 4));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(20000)]
    public void compress_round_trips_zero_runs(int length)
    {
        AssertRoundTrip(new byte[length]);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 100)]
    [InlineData(3, 4096)]
    [InlineData(4, 10000)]
    [InlineData(5, 65536)]
    public void compress_round_trips_random_buffers(int seed, int length)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);

        var compressed = AssertRoundTrip(data);

        // Literal-only worst case: one flag byte per eight literals.
        compressed.Length.Should().BeLessOrEqualTo(length + ((length + 7) / 8));
    }

    [Theory]
    [InlineData(4094)]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    public void compress_round_trips_repeats_at_window_boundary(int period)
    {
        var block = new byte[period];
        new Random(period).NextBytes(block);
        var data = block.Concat(block).Concat(block.Take(100)).ToArray();

        AssertRoundTrip(data);
    }

    [Fact]
    public void compress_round_trips_low_entropy_random_buffer()
    {
        var random = new Random(42);
        var data = new byte[30000];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)random.Next(0, 3);

        AssertRoundTrip(data);
    }

    [Fact]
    public void compress_round_trips_structured_field_like_payload()
    {
        var builder = new List<byte>();
        for (var i = 0; i < 500; i++)
        {
            builder.AddRange(Encoding.ASCII.GetBytes($"group{i % 17:D2}"));
            builder.AddRange(BitConverter.GetBytes((ushort)(i * 37)));
            builder.AddRange(Enumerable.Repeat((byte)(i & 0xFF), i % 25));
        }

        var data = builder.ToArray();
        var compressed = AssertRoundTrip(data);

        compressed.Length.Should().BeLessThan(data.Length);
    }

    [Fact]
    public void compressed_long_run_uses_max_length_references()
    {
        var data = new byte[18 * 9 + 1];

        var compressed = LzsCompression.Compress(data);

        // First literal, then 9 max-length references: 2 flag bytes + 1 literal + 18 reference bytes.
        compressed.Length.Should().Be(21);
        AssertRoundTrip(data);
    }

    private static byte[] AssertRoundTrip(byte[] data)
    {
        var compressed = LzsCompression.Compress(data);
        LzsCompression.Decompress(compressed).Should().Equal(data);

        var withHeader = LzsCompression.CompressWithHeader(data);
        LzsCompression.DecompressWithHeader(withHeader).Should().Equal(data);
        return compressed;
    }
}

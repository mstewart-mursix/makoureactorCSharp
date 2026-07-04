using System;
using System.Collections.Generic;
using System.IO;

namespace MakouReactor.Core.IO;

/// <summary>
/// Implements the LZSS variant used by Final Fantasy VII PC field files.
/// </summary>
public static class LzsCompression
{
    private const int RingSize = 4096;
    private const int RingMask = RingSize - 1;
    private const int MaxMatchLength = 18;
    private const int MinMatchLength = 3;
    private const int InitialRingPosition = RingSize - MaxMatchLength;
    private const int MaxCandidatesPerKey = 256;

    /// <summary>
    /// Decompresses an FF7 LZS stream with the four-byte compressed-size header.
    /// </summary>
    /// <param name="data">The header-prefixed compressed bytes.</param>
    /// <returns>The decompressed bytes.</returns>
    public static byte[] DecompressWithHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
            throw new InvalidDataException("LZS stream is missing the four-byte size header.");

        var compressedSize = BitConverter.ToUInt32(data[..sizeof(uint)]);
        if (compressedSize == 0x90000 && data.Length != compressedSize + sizeof(uint))
            return data.ToArray();

        var availableSize = Math.Min((int)compressedSize, data.Length - sizeof(uint));
        if (availableSize < 0)
            throw new InvalidDataException("LZS stream has an invalid compressed size.");

        return Decompress(data.Slice(sizeof(uint), availableSize));
    }

    /// <summary>
    /// Compresses bytes into an FF7 LZS stream with the four-byte compressed-size header.
    /// </summary>
    /// <param name="data">The uncompressed bytes.</param>
    /// <returns>The header-prefixed compressed bytes.</returns>
    public static byte[] CompressWithHeader(ReadOnlySpan<byte> data)
    {
        var compressed = Compress(data);
        using var output = new MemoryStream(compressed.Length + sizeof(uint));
        using var writer = new BinaryWriter(output);
        writer.Write((uint)compressed.Length);
        writer.Write(compressed);
        return output.ToArray();
    }

    /// <summary>
    /// Decompresses raw FF7 LZSS data without the four-byte size header.
    /// </summary>
    /// <param name="data">The compressed byte stream after any LZS size header.</param>
    /// <returns>The decompressed bytes.</returns>
    /// <exception cref="InvalidDataException">Thrown when the compressed stream ends inside a token.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return [];

        var ring = new byte[RingSize];
        var ringPosition = InitialRingPosition;
        var sourcePosition = 0;
        var flags = 0;

        using var output = new MemoryStream(Math.Max(data.Length * 2, 256));

        while (sourcePosition < data.Length)
        {
            flags >>= 1;
            if ((flags & 0x100) == 0)
            {
                flags = data[sourcePosition++] | 0xFF00;
                if (sourcePosition >= data.Length)
                    break;
            }

            if ((flags & 1) != 0)
            {
                var value = data[sourcePosition++];
                output.WriteByte(value);
                ring[ringPosition] = value;
                ringPosition = (ringPosition + 1) & RingMask;
                continue;
            }

            if (sourcePosition + 1 >= data.Length)
                throw new InvalidDataException("LZS reference token is truncated.");

            var offset = (int)data[sourcePosition++];
            var encodedLength = data[sourcePosition++];
            offset |= (encodedLength & 0xF0) << 4;
            var length = (encodedLength & 0x0F) + MinMatchLength;

            for (var i = 0; i < length; i++)
            {
                var value = ring[(offset + i) & RingMask];
                output.WriteByte(value);
                ring[ringPosition] = value;
                ringPosition = (ringPosition + 1) & RingMask;
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// Compresses bytes into raw FF7 LZSS data without the four-byte size header.
    /// </summary>
    /// <param name="data">The uncompressed bytes.</param>
    /// <returns>A compressed byte stream that <see cref="Decompress"/> can restore exactly.</returns>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return [];

        using var output = new MemoryStream(data.Length + (data.Length / 8) + 16);
        var candidatesByKey = new Dictionary<int, List<int>>();
        var position = 0;

        while (position < data.Length)
        {
            var flagPosition = output.Position;
            output.WriteByte(0);

            byte flags = 0;
            byte mask = 1;
            var tokenCount = 0;

            while (tokenCount < 8 && position < data.Length)
            {
                var match = FindBestMatch(data, position, candidatesByKey);
                if (match.Length >= MinMatchLength)
                {
                    output.WriteByte((byte)(match.RingOffset & 0xFF));
                    output.WriteByte((byte)(((match.RingOffset >> 4) & 0xF0) |
                                            (match.Length - MinMatchLength)));
                    AddCandidatePositions(data, position, match.Length, candidatesByKey);
                    position += match.Length;
                }
                else
                {
                    flags |= mask;
                    output.WriteByte(data[position]);
                    AddCandidatePositions(data, position, 1, candidatesByKey);
                    position++;
                }

                mask <<= 1;
                tokenCount++;
            }

            var endPosition = output.Position;
            output.Position = flagPosition;
            output.WriteByte(flags);
            output.Position = endPosition;
        }

        return output.ToArray();
    }

    private static Match FindBestMatch(
        ReadOnlySpan<byte> data,
        int position,
        Dictionary<int, List<int>> candidatesByKey)
    {
        if (position + MinMatchLength > data.Length)
            return default;

        var key = KeyAt(data, position);
        if (!candidatesByKey.TryGetValue(key, out var candidates))
            return default;

        RemoveExpiredCandidates(candidates, position - RingSize);
        if (candidates.Count == 0)
            return default;

        var bestPosition = -1;
        var bestLength = 0;
        var maxLength = Math.Min(MaxMatchLength, data.Length - position);

        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            var candidate = candidates[i];
            var length = 0;

            while (length < maxLength &&
                   data[candidate + length] == data[position + length])
            {
                length++;
            }

            if (length > bestLength)
            {
                bestLength = length;
                bestPosition = candidate;
                if (bestLength == MaxMatchLength)
                    break;
            }
        }

        if (bestLength < MinMatchLength)
            return default;

        return new Match((InitialRingPosition + bestPosition) & RingMask, bestLength);
    }

    private static void AddCandidatePositions(
        ReadOnlySpan<byte> data,
        int start,
        int count,
        Dictionary<int, List<int>> candidatesByKey)
    {
        var end = Math.Min(start + count, data.Length);
        for (var position = start; position < end; position++)
        {
            if (position + MinMatchLength > data.Length)
                return;

            var key = KeyAt(data, position);
            if (!candidatesByKey.TryGetValue(key, out var candidates))
            {
                candidates = [];
                candidatesByKey.Add(key, candidates);
            }

            candidates.Add(position);
            if (candidates.Count > MaxCandidatesPerKey)
                candidates.RemoveRange(0, candidates.Count - MaxCandidatesPerKey);
        }
    }

    private static void RemoveExpiredCandidates(List<int> candidates, int minimumPosition)
    {
        var removeCount = 0;
        while (removeCount < candidates.Count && candidates[removeCount] < minimumPosition)
            removeCount++;

        if (removeCount > 0)
            candidates.RemoveRange(0, removeCount);
    }

    private static int KeyAt(ReadOnlySpan<byte> data, int position) =>
        (data[position] << 16) | (data[position + 1] << 8) | data[position + 2];

    private readonly record struct Match(int RingOffset, int Length);
}

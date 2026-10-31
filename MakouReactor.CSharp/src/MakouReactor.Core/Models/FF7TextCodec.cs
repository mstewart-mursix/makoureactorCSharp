using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MakouReactor.Core.Models;

/// <summary>
/// Converts FF7 field text bytes to and from editable strings, following ff7tk <c>FF7Text</c>
/// (the library used by the C++ reference) and the byte layout walked by
/// <c>src/core/FF7Font.cpp</c>: one-byte characters and controls, 0xFA-0xFD page prefixes,
/// 0xFE-prefixed controls (0xFE 0xDD takes one argument byte, 0xFE 0xE2 four) and the 0xFF
/// terminator. Bytes without a known meaning are written as <c>{xNN}</c> so they round-trip.
/// </summary>
public static partial class FF7TextCodec
{
    private const byte Terminator = 0xFF;
    private const byte PagePrefixFirst = 0xFA;
    private const byte PagePrefixLast = 0xFD;
    private const byte ControlPrefix = 0xFE;
    private const byte PauseWithArgument = 0xDD;
    private const byte Memory = 0xE2;
    private const int MemoryArgumentCount = 4;
    private const byte FirstFeControl = 0xD2;

    private static readonly string[][] JapanesePages =
    [
        FF7TextTables.JapaneseFA,
        FF7TextTables.JapaneseFB,
        FF7TextTables.JapaneseFC,
        FF7TextTables.JapaneseFD,
        FF7TextTables.JapaneseFE,
    ];

    private static readonly Dictionary<string, byte[]> InternationalReverse = BuildReverse(japanese: false);
    private static readonly Dictionary<string, byte[]> JapaneseReverse = BuildReverse(japanese: true);

    /// <summary>Extra spellings accepted by <see cref="Encode"/> (never produced by <see cref="Decode"/>).</summary>
    private static readonly Dictionary<string, byte[]> TokenAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["{NEW PAGE}"] = [0xE8],
        ["{NEW PAGE 2}"] = [0xE9],
        ["{AERIS}"] = [0xED],
        ["{TAB}"] = [0xE1],
        ["{, }"] = [0xE2],
        ["{.\"}"] = [0xE3],
        ["{…\"}"] = [0xE4],
        ["{...\"}"] = [0xE4],
    };

    /// <summary>
    /// Returns the index of the 0xFF terminator, skipping argument bytes of multi-byte sequences,
    /// or the length of <paramref name="bytes"/> when there is none.
    /// </summary>
    /// <param name="bytes">FF7 text bytes.</param>
    /// <returns>The terminator index.</returns>
    public static int FindTerminator(ReadOnlySpan<byte> bytes)
    {
        for (var index = 0; index < bytes.Length; index += SequenceLength(bytes, index))
        {
            if (bytes[index] == Terminator)
                return index;
        }

        return bytes.Length;
    }

    /// <summary>
    /// Decodes FF7 text bytes (up to the terminator) into an editable string.
    /// </summary>
    /// <param name="bytes">FF7 text bytes.</param>
    /// <param name="japanese">Use the Japanese tables instead of the international table.</param>
    /// <returns>The decoded string.</returns>
    public static string Decode(ReadOnlySpan<byte> bytes, bool japanese = false)
    {
        bytes = bytes[..FindTerminator(bytes)];
        var table = japanese ? FF7TextTables.Japanese : FF7TextTables.International;
        var builder = new StringBuilder(bytes.Length);

        for (var index = 0; index < bytes.Length;)
        {
            var value = bytes[index];
            var length = Math.Min(SequenceLength(bytes, index), bytes.Length - index);
            var sequence = bytes.Slice(index, length);
            index += length;

            if (value is >= PagePrefixFirst and <= ControlPrefix && sequence.Length >= 2)
            {
                var text = DecodePrefixed(sequence, japanese);
                if (text != null)
                {
                    builder.Append(text);
                    continue;
                }

                AppendEscapes(builder, sequence);
                continue;
            }

            if (value < PagePrefixFirst && table[value].Length > 0)
                builder.Append(table[value]);
            else
                AppendEscapes(builder, sequence);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Encodes an editable string into FF7 text bytes.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="includeTerminator">Append the 0xFF terminator.</param>
    /// <param name="japanese">Use the Japanese tables instead of the international table.</param>
    /// <returns>The FF7 text bytes. CRLF is treated as a line feed; characters with no FF7 equivalent become '?'.</returns>
    public static byte[] Encode(string text, bool includeTerminator = true, bool japanese = false)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Editors (WPF TextBox) use CRLF; FF7 only has a line-feed control (0xE7).
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var reverse = japanese ? JapaneseReverse : InternationalReverse;
        var maxKeyLength = reverse.Keys.Max(static key => key.Length);
        var bytes = new List<byte>(text.Length + 1);

        for (var index = 0; index < text.Length;)
        {
            if (text[index] == '{' && TryEncodeBraceToken(text, index, out var tokenBytes, out var tokenLength))
            {
                bytes.AddRange(tokenBytes);
                index += tokenLength;
                continue;
            }

            var matched = false;
            for (var length = Math.Min(maxKeyLength, text.Length - index); length > 0; length--)
            {
                if (reverse.TryGetValue(text.Substring(index, length), out var encoded))
                {
                    bytes.AddRange(encoded);
                    index += length;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                bytes.AddRange(reverse.TryGetValue("?", out var question) ? question : reverse["？"]);
                index++;
            }
        }

        if (includeTerminator)
            bytes.Add(Terminator);

        return [.. bytes];
    }

    /// <summary>
    /// Lists brace tokens in <paramref name="text"/> that <see cref="Encode"/> does not recognize
    /// (they would be written as literal characters).
    /// </summary>
    /// <param name="text">The text to inspect.</param>
    /// <returns>Distinct unsupported tokens.</returns>
    public static IReadOnlyList<string> FindUnsupportedTokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return BraceTokenRegex()
            .Matches(text)
            .Select(static match => match.Value)
            .Where(static token => !TryEncodeBraceToken(token, 0, out _, out var length) || length != token.Length)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int SequenceLength(ReadOnlySpan<byte> bytes, int index)
    {
        var value = bytes[index];
        if (value is >= PagePrefixFirst and <= PagePrefixLast)
            return 2;

        if (value != ControlPrefix || index + 1 >= bytes.Length)
            return 1;

        return bytes[index + 1] switch
        {
            PauseWithArgument => 3,
            Memory => 2 + MemoryArgumentCount,
            _ => 2,
        };
    }

    private static string? DecodePrefixed(ReadOnlySpan<byte> sequence, bool japanese)
    {
        var prefix = sequence[0];
        var value = sequence[1];

        if (prefix != ControlPrefix)
        {
            var page = JapanesePages[prefix - PagePrefixFirst];
            return japanese && value < page.Length && page[value].Length > 0 ? page[value] : null;
        }

        if (value == PauseWithArgument)
        {
            return sequence.Length == 3
                ? string.Create(CultureInfo.InvariantCulture, $"{{PAUSE{sequence[2]:D3}}}")
                : null;
        }

        var fePage = FF7TextTables.JapaneseFE;
        if (value >= fePage.Length || fePage[value].Length == 0)
            return null;

        return value >= FirstFeControl || japanese ? fePage[value] : null;
    }

    private static void AppendEscapes(StringBuilder builder, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
            builder.Append(CultureInfo.InvariantCulture, $"{{x{value:X2}}}");
    }

    private static bool TryEncodeBraceToken(string text, int index, out byte[] bytes, out int length)
    {
        bytes = [];
        length = 0;
        var end = text.IndexOf('}', index + 1);
        if (end < 0)
            return false;

        var token = text[index..(end + 1)];
        if (TokenAliases.TryGetValue(token, out var alias))
        {
            bytes = alias;
            length = token.Length;
            return true;
        }

        var hex = HexTokenRegex().Match(token);
        if (hex.Success)
        {
            bytes = [byte.Parse(hex.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)];
            length = token.Length;
            return true;
        }

        var pause = PauseTokenRegex().Match(token);
        if (pause.Success)
        {
            var duration = int.Parse(pause.Groups[1].Value, CultureInfo.InvariantCulture);
            if (duration <= byte.MaxValue)
            {
                bytes = [ControlPrefix, PauseWithArgument, (byte)duration];
                length = token.Length;
                return true;
            }
        }

        // Named tokens ({CLOUD}, {GREY}, ...) are entries of the reverse tables; they are
        // matched case-insensitively here so hand-typed tokens are accepted.
        if (InternationalReverse.TryGetValue(token, out var named) ||
            NamedTokensIgnoreCase.TryGetValue(token, out named))
        {
            bytes = named;
            length = token.Length;
            return true;
        }

        return false;
    }

    private static readonly Dictionary<string, byte[]> NamedTokensIgnoreCase =
        InternationalReverse
            .Where(static pair => pair.Key.StartsWith('{') && pair.Key.EndsWith('}'))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, byte[]> BuildReverse(bool japanese)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var table = japanese ? FF7TextTables.Japanese : FF7TextTables.International;

        for (var value = 0; value < PagePrefixFirst; value++)
        {
            if (table[value].Length > 0)
                result.TryAdd(table[value], [(byte)value]);
        }

        for (var page = 0; page < JapanesePages.Length; page++)
        {
            var entries = JapanesePages[page];
            for (var value = 0; value < entries.Length; value++)
            {
                if (entries[value].Length == 0)
                    continue;
                if (!japanese && value < FirstFeControl)
                    continue;
                if (page < 4 && !japanese)
                    continue;

                result.TryAdd(entries[value], [(byte)(PagePrefixFirst + page), (byte)value]);
            }
        }

        return result;
    }

    [GeneratedRegex("""^\{x([0-9A-Fa-f]{2})\}$""")]
    private static partial Regex HexTokenRegex();

    [GeneratedRegex("""^\{PAUSE(\d{3})\}$""", RegexOptions.IgnoreCase)]
    private static partial Regex PauseTokenRegex();

    [GeneratedRegex("""\{[^}]+\}""")]
    private static partial Regex BraceTokenRegex();
}

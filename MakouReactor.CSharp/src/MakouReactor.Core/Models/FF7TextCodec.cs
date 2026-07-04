using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MakouReactor.Core.Models;

public static partial class FF7TextCodec
{
    private const byte Terminator = 0xFF;
    private const byte JapaneseHalfWidthStart = 0xA1;
    private const byte JapaneseHalfWidthEnd = 0xDF;
    private const char JapaneseHalfWidthUnicodeStart = '\uFF61';

    private static readonly Dictionary<byte, string> ControlTokens = new()
    {
        [0xD2] = "{GREY}",
        [0xD3] = "{BLUE}",
        [0xD4] = "{RED}",
        [0xD5] = "{PURPLE}",
        [0xD6] = "{GREEN}",
        [0xD7] = "{CYAN}",
        [0xD8] = "{YELLOW}",
        [0xD9] = "{WHITE}",
        [0xDA] = "{BLINK}",
        [0xDB] = "{MULTICOLOUR}",
        [0xDD] = "{PAUSE}",
        [0xDE] = "{VARHEX}",
        [0xDF] = "{VARDEC}",
        [0xE0] = "{CHOICE}",
        [0xE1] = "{TAB}",
        [0xE2] = "{, }",
        [0xE3] = "{.\"}",
        [0xE4] = "{...\"}",
        [0xE7] = "{CHOICE}",
        [0xE8] = "{TAB}",
        [0xE9] = "{NUM}",
        [0xEA] = "{HEX}",
        [0xEB] = "{SCROLL}",
        [0xEC] = "{RNUM}",
        [0xED] = "{NEW}",
        [0xEE] = "{CLOUD}",
        [0xEF] = "{BARRET}",
        [0xF0] = "{TIFA}",
        [0xF1] = "{AERITH}",
        [0xF2] = "{RED XIII}",
        [0xF3] = "{YUFFIE}",
        [0xF4] = "{CAIT SITH}",
        [0xF5] = "{VINCENT}",
        [0xF6] = "{CIRCLE}",
        [0xF7] = "{TRIANGLE}",
        [0xF8] = "{SQUARE}",
        [0xF9] = "{CROSS}",
    };

    private static readonly Dictionary<string, byte> ReverseControlTokens =
        BuildReverseControlTokens();

    private static readonly Dictionary<string, byte> TokenAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["{NEW PAGE}"] = 0xED,
        ["{NEW PAGE 2}"] = 0xED,
        ["{EOL}"] = 0xED,
        ["{AERIS}"] = 0xF1,
        ["{CID}"] = 0xF5,
        ["{MEMBER 1}"] = 0xEE,
        ["{MEMBER 2}"] = 0xEF,
        ["{MEMBER 3}"] = 0xF0,
        ["{SCROLLING}"] = 0xEB,
        ["{VARDECR}"] = 0xEC,
        ["{SPACED CHARACTERS}"] = 0xE9,
    };

    public static string Decode(ReadOnlySpan<byte> bytes, bool japanese = false)
    {
        var terminator = bytes.IndexOf(Terminator);
        if (terminator >= 0)
            bytes = bytes[..terminator];

        var builder = new StringBuilder(bytes.Length);
        foreach (var value in bytes)
        {
            if (value is >= 0x20 and <= 0x7E)
            {
                builder.Append((char)value);
            }
            else if (japanese && value is >= JapaneseHalfWidthStart and <= JapaneseHalfWidthEnd)
            {
                builder.Append((char)(JapaneseHalfWidthUnicodeStart + (value - JapaneseHalfWidthStart)));
            }
            else if (ControlTokens.TryGetValue(value, out var token))
            {
                builder.Append(token);
            }
            else
            {
                builder.Append(CultureInfo.InvariantCulture, $"{{x{value:X2}}}");
            }
        }

        return builder.ToString();
    }

    public static byte[] Encode(string text, bool includeTerminator = true)
    {
        ArgumentNullException.ThrowIfNull(text);

        var bytes = new List<byte>(text.Length + (includeTerminator ? 1 : 0));
        for (var index = 0; index < text.Length;)
        {
            if (text[index] == '{')
            {
                var end = text.IndexOf('}', index + 1);
                if (end > index)
                {
                    var token = text[index..(end + 1)];
                    if (TryEncodeToken(token, out var value))
                    {
                        bytes.Add(value);
                        index = end + 1;
                        continue;
                    }
                }
            }

            var character = text[index];
            if (character is >= ' ' and <= '~')
                bytes.Add((byte)character);
            else if (TryEncodeJapaneseHalfWidth(character, out var japaneseValue))
                bytes.Add(japaneseValue);
            else
                bytes.Add((byte)'?');
            index++;
        }

        if (includeTerminator)
            bytes.Add(Terminator);

        return [.. bytes];
    }

    public static IReadOnlyList<string> FindUnsupportedTokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return BraceTokenRegex()
            .Matches(text)
            .Select(static match => match.Value)
            .Where(static token => !IsSupportedToken(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSupportedToken(string token) => TryEncodeToken(token, out _);

    private static bool TryEncodeToken(string token, out byte value)
    {
        if (ReverseControlTokens.TryGetValue(token, out value) ||
            TokenAliases.TryGetValue(token, out value))
            return true;

        var pause = PauseTokenRegex().Match(token);
        if (pause.Success)
        {
            value = 0xDD;
            return true;
        }

        var match = HexTokenRegex().Match(token);
        if (!match.Success)
            return false;

        value = byte.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryEncodeJapaneseHalfWidth(char character, out byte value)
    {
        var offset = character - JapaneseHalfWidthUnicodeStart;
        if (offset is >= 0 and <= JapaneseHalfWidthEnd - JapaneseHalfWidthStart)
        {
            value = checked((byte)(JapaneseHalfWidthStart + offset));
            return true;
        }

        value = 0;
        return false;
    }

    private static Dictionary<string, byte> BuildReverseControlTokens()
    {
        var result = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var (value, token) in ControlTokens.OrderBy(static pair => pair.Key))
            result.TryAdd(token, value);

        return result;
    }

    [GeneratedRegex("""^\{x([0-9A-Fa-f]{2})\}$""")]
    private static partial Regex HexTokenRegex();

    [GeneratedRegex("""^\{PAUSE\d{3}\}$""", RegexOptions.IgnoreCase)]
    private static partial Regex PauseTokenRegex();

    [GeneratedRegex("""\{[^}]+\}""")]
    private static partial Regex BraceTokenRegex();
}

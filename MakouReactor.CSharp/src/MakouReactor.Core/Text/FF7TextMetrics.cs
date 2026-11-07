using System.Text;

using MakouReactor.Core.Models;

namespace MakouReactor.Core.Text;

/// <summary>
/// FF7 dialog-window metrics for international (non-Japanese) field text, ported from
/// <c>FF7Font::calcSize</c> in <c>src/core/FF7Font.cpp</c> with the built-in width table
/// (the C++ app can also read <c>window.bin</c>; this port uses the embedded defaults).
/// Inputs are raw FF7 text bytes (see the ff7tk <c>FF7Text</c> table: <c>0x00</c> = space,
/// <c>0xE7</c> = newline, <c>0xFF</c> = terminator).
/// </summary>
public static class FF7TextMetrics
{
    /// <summary>Widest window the game can show (pixels); larger sizes are capped, as in the C++ code.</summary>
    public const int MaxWindowWidth = 322;

    /// <summary>Tallest window the game can show (pixels).</summary>
    public const int MaxWindowHeight = 226;

    /// <summary>Default right margin used by the C++ auto-size (<c>autoSizeMarginRight</c>).</summary>
    public const int DefaultMarginRight = 14;

    /// <summary>Line height in pixels; a one-line window is <see cref="FirstLineHeight"/> tall.</summary>
    public const int LineHeight = 16;

    /// <summary>Height of a window with a single line of text.</summary>
    public const int FirstLineHeight = 25;

    private const byte NewLine = 0xE7;
    private const byte NewPage = 0xE8;
    private const byte NewPage2 = 0xE9;
    private const byte Terminator = 0xFF;
    private const int ChoiceWidth = 10;
    private const int TabWidth = 4;
    private const int SpacedCharsWidth = 13;

    /// <summary>
    /// International character width table: low 5 bits = glyph width, high 3 bits = left padding
    /// (ff7tk <c>CHARACTER_WIDTH</c>/<c>LEFT_PADD</c>). Copied verbatim from <c>FF7Font::charWidth[0]</c>.
    /// </summary>
    private static readonly byte[] CharWidthTable =
    [
        3, 3, 72, 10, 7, 10, 9, 3, 72, 72, 7, 7, 39, 5, 38, 6,
        8, 71, 8, 8, 8, 8, 8, 8, 8, 8, 69, 4, 7, 8, 7, 6,
        10, 9, 7, 8, 8, 7, 7, 8, 8, 3, 6, 7, 7, 11, 8, 9,
        7, 9, 7, 7, 7, 8, 9, 11, 8, 9, 7, 4, 6, 4, 7, 8,
        4, 7, 7, 6, 7, 7, 6, 7, 7, 3, 4, 6, 3, 11, 7, 7,
        7, 7, 5, 6, 6, 7, 7, 11, 7, 7, 6, 5, 3, 5, 8, 68,
        73, 76, 72, 73, 73, 9, 8, 7, 7, 7, 7, 7, 7, 7, 7, 7,
        7, 7, 4, 4, 4, 4, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
        11, 6, 7, 8, 11, 6, 7, 7, 9, 9, 11, 4, 5, 8, 12, 9,
        11, 7, 7, 7, 9, 7, 7, 7, 9, 8, 4, 6, 6, 9, 11, 7,
        6, 3, 8, 7, 8, 8, 9, 7, 7, 9, 1, 9, 9, 9, 12, 11,
        8, 12, 6, 6, 4, 4, 7, 7, 7, 9, 7, 9, 5, 5, 7, 7,
        8, 3, 4, 6, 13, 9, 7, 9, 7, 7, 3, 4, 4, 3, 9, 9,
        8, 9, 8, 8, 8, 3, 6, 7, 5, 6, 3, 6, 5, 6, 5, 5,
        1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ];

    /// <summary>Bytes for the optimised duo characters 0xE2..0xE4 (", ", ".\"", "...\"").</summary>
    private static readonly byte[][] OptimisedDuo =
    [
        [0x0C, 0x00],
        [0x0E, 0x02],
        [0xA9, 0x02],
    ];

    private static readonly int BiggestCharWidth = CharFullWidth(0x37); // 'W'

    /// <summary>Full advance (glyph width + left padding) of an international character byte, in pixels.</summary>
    public static int CharFullWidth(byte c)
    {
        var w = CharWidthTable[c];
        return (w & 0x1F) + (w >> 5);
    }

    /// <summary>
    /// Window size needed for <paramref name="ff7Text"/>, capped at
    /// <see cref="MaxWindowWidth"/> x <see cref="MaxWindowHeight"/> exactly like the C++ code.
    /// </summary>
    public static (int Width, int Height) CalcWindowSize(ReadOnlySpan<byte> ff7Text, int marginRight = DefaultMarginRight)
    {
        var (w, h) = CalcUncappedSize(ff7Text, marginRight);
        return (Math.Min(w, MaxWindowWidth), Math.Min(h, MaxWindowHeight));
    }

    /// <summary>True when every line fits in the widest window and every page fits in the tallest one.</summary>
    public static bool Fits(ReadOnlySpan<byte> ff7Text, int marginRight = DefaultMarginRight)
    {
        var (w, h) = CalcUncappedSize(ff7Text, marginRight);
        return w <= MaxWindowWidth && h <= MaxWindowHeight;
    }

    /// <summary>Port of <c>FF7Font::calcSize</c> without the final cap (international text only).</summary>
    public static (int Width, int Height) CalcUncappedSize(ReadOnlySpan<byte> ff7Text, int marginRight = DefaultMarginRight)
    {
        var baseWidth = 8 + marginRight;
        int line = 0, width = baseWidth - 3, height = FirstLineHeight, maxW = 0, maxH = 0;
        var spaced = false;

        for (var i = 0; i < ff7Text.Length; i++)
        {
            var c = ff7Text[i];
            if (c == Terminator)
                break;

            switch (c)
            {
                case NewPage:
                case NewPage2:
                    if (line == 0) width += 3;
                    maxH = Math.Max(maxH, height);
                    maxW = Math.Max(maxW, width);
                    line++;
                    width = baseWidth;
                    height = FirstLineHeight;
                    break;

                case NewLine:
                    if (line == 0) width += 3;
                    maxW = Math.Max(maxW, width);
                    line++;
                    width = baseWidth;
                    height += LineHeight;
                    break;

                case 0xFA: // Japanese pages: in international mode only 0xFA contributes (1px per char < 0xD2)
                    i++;
                    if (i < ff7Text.Length && ff7Text[i] < 0xD2)
                        width += spaced ? SpacedCharsWidth : 1;
                    break;

                case 0xFB:
                case 0xFC:
                case 0xFD:
                    i++;
                    break;

                case 0xFE:
                    i++;
                    if (i >= ff7Text.Length)
                        break;
                    c = ff7Text[i];
                    if (c == 0xDD)
                        i++; // {PAUSE} argument
                    else if (c is 0xDE or 0xDF or 0xE1) // {VARHEX} {VARDEC} {VARDECR}
                        width += (spaced ? SpacedCharsWidth : CharFullWidth(0x10)) * 5;
                    else if (c == 0xE2) // {MEMORY}
                    {
                        if (i + 3 >= ff7Text.Length)
                            break;
                        width += (spaced ? SpacedCharsWidth : BiggestCharWidth) * ff7Text[i + 3];
                        i += 4;
                    }
                    else if (c == 0xE9) // {SPACED CHARACTERS}
                        spaced = !spaced;
                    break;

                case 0xE0: // {CHOICE}
                    width += spaced ? SpacedCharsWidth * ChoiceWidth : 3 * ChoiceWidth;
                    break;

                case 0xE1: // tab
                    width += spaced ? SpacedCharsWidth * TabWidth : 3 * TabWidth;
                    break;

                case >= 0xE2 and <= 0xE4: // optimised duo
                    var duo = OptimisedDuo[c - 0xE2];
                    width += spaced ? SpacedCharsWidth : CharFullWidth(duo[0]);
                    width += spaced ? SpacedCharsWidth : CharFullWidth(duo[1]);
                    break;

                case >= 0xEA and <= 0xF5: // character names (9 chars max)
                    width += (spaced ? SpacedCharsWidth : BiggestCharWidth) * 9;
                    break;

                case >= 0xF6 and <= 0xF9: // button glyphs
                    width += 17;
                    break;

                default:
                    width += spaced ? SpacedCharsWidth : CharFullWidth(c);
                    break;
            }
        }

        maxH = Math.Max(maxH, height);
        maxW = Math.Max(maxW, width);
        return (maxW, maxH);
    }
}

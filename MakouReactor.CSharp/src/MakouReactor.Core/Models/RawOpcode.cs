using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MakouReactor.Core.Models;

public sealed record RawOpcode(
    int Offset,
    byte Id,
    string Name,
    int DeclaredSize,
    byte[] Bytes,
    bool IsTruncated)
{
    public string OffsetHex => $"0x{Offset:X4}";
    public string IdHex => $"0x{Id:X2}";
    public int Size => Bytes.Length;
    public string RawBytesHex => string.Join(" ", Bytes.Select(static b => b.ToString("X2", CultureInfo.InvariantCulture)));
    public string Arguments => BuildArguments();
    public string Warning => IsTruncated
        ? $"Truncated opcode: expected {DeclaredSize} byte(s), found {Size}."
        : string.Empty;

    private string BuildArguments()
    {
        if (Bytes.Length <= 1)
            return string.Empty;

        return Name switch
        {
            "WAIT" when Bytes.Length >= 3 =>
                $"duration={ReadUInt16(1)}",
            "MESSAGE" when Bytes.Length >= 3 =>
                $"window={Bytes[1]}, text={Bytes[2]}",
            "MPARA" when Bytes.Length >= 5 =>
                $"text={Bytes[1]}, x={Bytes[2]}, y={Bytes[3]}, width={Bytes[4]}",
            "MPNAM" when Bytes.Length >= 2 =>
                $"character={Bytes[1]}",
            "WINDOW" when Bytes.Length >= 10 =>
                $"id={Bytes[1]}, x={ReadUInt16(2)}, y={ReadUInt16(4)}, width={ReadUInt16(6)}, height={ReadUInt16(8)}",
            "WCLS" when Bytes.Length >= 2 =>
                $"window={Bytes[1]}",
            "WMOVE" when Bytes.Length >= 6 =>
                $"window={Bytes[1]}, x={ReadUInt16(2)}, y={ReadUInt16(4)}",
            "MAPJUMP" when Bytes.Length >= 10 =>
                $"field={ReadUInt16(1)}, x={ReadUInt16(3)}, y={ReadUInt16(5)}, triangle={ReadUInt16(7)}, direction={Bytes[9]}",
            "BATTLE" when Bytes.Length >= 4 =>
                $"battle={ReadUInt16(1)}, flags={Bytes[3]}",
            _ => $"args={string.Join(" ", Bytes.Skip(1).Select(static b => b.ToString("X2", CultureInfo.InvariantCulture)))}",
        };
    }

    private ushort ReadUInt16(int offset) =>
        Bytes.Length >= offset + 2
            ? (ushort)(Bytes[offset] | (Bytes[offset + 1] << 8))
            : (ushort)0;
}

public static class RawOpcodeReader
{
    private static readonly byte[] Lengths =
    [
        1, 3, 3, 3, 3, 3, 3, 2, 2, 15, 6, 6, 1, 1, 2, 2,
        2, 3, 2, 3, 6, 7, 8, 9, 8, 9, 10, 3, 6, 1, 1, 1,
        11, 2, 5, 3, 3, 9, 2, 2, 3, 1, 2, 2, 5, 7, 2, 10,
        4, 4, 4, 2, 2, 4, 5, 8, 6, 6, 6, 4, 1, 1, 1, 1,
        3, 5, 6, 2, 1, 5, 1, 5, 7, 4, 2, 2, 1, 5, 1, 5,
        10, 6, 4, 2, 2, 3, 7, 7, 5, 5, 5, 7, 8, 10, 8, 1,
        10, 2, 5, 6, 6, 1, 9, 1, 9, 2, 7, 9, 1, 4, 3, 6,
        4, 2, 3, 4, 4, 8, 4, 5, 4, 5, 3, 3, 3, 3, 2, 3,
        4, 5, 4, 4, 4, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4,
        5, 4, 5, 4, 5, 3, 3, 3, 3, 3, 4, 5, 6, 7, 7, 11,
        2, 2, 3, 3, 2, 11, 9, 9, 6, 6, 2, 4, 1, 6, 3, 3,
        5, 5, 4, 3, 6, 6, 2, 4, 5, 4, 3, 5, 5, 4, 1, 2,
        11, 8, 15, 12, 1, 3, 3, 2, 2, 2, 4, 3, 3, 3, 2, 2,
        13, 2, 2, 16, 10, 10, 4, 4, 3, 1, 15, 2, 4, 1, 1, 11,
        4, 4, 3, 3, 3, 5, 5, 5, 7, 10, 10, 5, 5, 8, 8, 11,
        2, 5, 14, 2, 2, 2, 2, 4, 2, 1, 3, 2, 2, 8, 3, 1,
    ];

    private static readonly string[] Names =
    [
        "RET", "REQ", "REQSW", "REQEW", "PREQ", "PRQSW", "PRQEW", "RETTO",
        "JOIN", "SPLIT", "SPTYE", "GTPYE", "Unknown1", "Unknown2", "DSKCG", "SPECIAL",
        "JMPF", "JMPFL", "JMPB", "JMPBL", "IFUB", "IFUBL", "IFSW", "IFSWL",
        "IFUW", "IFUWL", "Unknown3", "Unknown4", "Unknown5", "Unknown6", "Unknown7", "Unknown8",
        "MINIGAME", "TUTOR", "BTMD2", "BTRLD", "WAIT", "NFADE", "BLINK", "BGMOVIE",
        "KAWAI", "KAWIW", "PMOVA", "SLIP", "BGPDH", "BGSCR", "WCLS", "WSIZW",
        "IFKEY", "IFKEYON", "IFKEYOFF", "UC", "PDIRA", "PTURA", "WSPCL", "WNUMB",
        "STTIM", "GOLDu", "GOLDd", "CHGLD", "HMPMAX1", "HMPMAX2", "MHMMX", "HMPMAX3",
        "MESSAGE", "MPARA", "MPRA2", "MPNAM", "Unknown9", "MPu", "Unknown10", "MPd",
        "ASK", "MENU", "MENU2", "BTLTB", "Unknown11", "HPu", "Unknown12", "HPd",
        "WINDOW", "WMOVE", "WMODE", "WREST", "WCLSE", "WROW", "GWCOL", "SWCOL",
        "STITM", "DLITM", "CKITM", "SMTRA", "DMTRA", "CMTRA", "SHAKE", "NOP",
        "MAPJUMP", "SCRLO", "SCRLC", "SCRLA", "SCR2D", "SCRCC", "SCR2DC", "SCRLW",
        "SCR2DL", "MPDSP", "VWOFT", "FADE", "FADEW", "IDLCK", "LSTMP", "SCRLP",
        "BATTLE", "BTLON", "BTLMD", "PGTDR", "GETPC", "PXYZI", "PLUS!", "PLUS2!",
        "MINUS!", "MINUS2!", "INC!", "INC2!", "DEC!", "DEC2!", "TLKON", "RDMSD",
        "SETBYTE", "SETWORD", "BITON", "BITOFF", "BITXOR", "PLUS", "PLUS2", "MINUS",
        "MINUS2", "MUL", "MUL2", "DIV", "DIV2", "MOD", "MOD2", "AND",
        "AND2", "OR", "OR2", "XOR", "XOR2", "INC", "INC2", "DEC",
        "DEC2", "RANDOM", "LBYTE", "HBYTE", "2BYTE", "SETX", "GETX", "SEARCHX",
        "PC", "CHAR", "DFANM", "ANIME1", "VISI", "XYZI", "XYI", "XYZ",
        "MOVE", "CMOVE", "MOVA", "TURA", "ANIMW", "FMOVE", "ANIME2", "ANIM!1",
        "CANIM1", "CANM!1", "MSPED", "DIR", "TURNGEN", "TURN", "DIRA", "GETDIR",
        "GETAXY", "GETAI", "ANIM!2", "CANIM2", "CANM!2", "ASPED", "Unknown13", "CC",
        "JUMP", "AXYZI", "LADER", "OFST", "OFSTW", "TALKR", "SLIDR", "SOLID",
        "PRTYP", "PRTYM", "PRTYE", "IFPRTYQ", "IFMEMBQ", "MMBud", "MMBLK", "MMBUK",
        "LINE", "LINON", "MPJPO", "SLINE", "SIN", "COS", "TLKR2", "SLDR2",
        "PMJMP", "PMJMP2", "AKAO2", "FCFIX", "CCANM", "ANIMB", "TURNW", "MPPAL",
        "BGON", "BGOFF", "BGROL", "BGROL2", "BGCLR", "STPAL", "LDPAL", "CPPAL",
        "RTPAL", "ADPAL", "MPPAL2", "STPLS", "LDPLS", "CPPAL2", "RTPAL2", "ADPAL2",
        "MUSIC", "SOUND", "AKAO", "MUSVT", "MUSVM", "MULCK", "BMUSC", "CHMPH",
        "PMVIE", "MOVIE", "MVIEF", "MVCAM", "FMUSC", "CMUSC", "CHMST", "GAMEOVER",
    ];

    public static IReadOnlyList<RawOpcode> Read(ReadOnlySpan<byte> scriptData)
    {
        var opcodes = new List<RawOpcode>();
        var offset = 0;

        while (offset < scriptData.Length)
        {
            var id = scriptData[offset];
            var declaredSize = Lengths[id];
            var availableSize = Math.Min(declaredSize, scriptData.Length - offset);
            opcodes.Add(new RawOpcode(
                offset,
                id,
                Names[id],
                declaredSize,
                scriptData.Slice(offset, availableSize).ToArray(),
                availableSize < declaredSize));

            if (declaredSize == 0)
                break;

            offset += availableSize;
            if (availableSize < declaredSize)
                break;
        }

        return opcodes;
    }
}

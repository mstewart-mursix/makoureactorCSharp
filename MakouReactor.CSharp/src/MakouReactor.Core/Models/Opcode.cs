using System;
using System.Collections.Generic;
using System.Linq;

namespace MakouReactor.Core.Models;

// ──────────────────────────────────────────────
// Opcode definitions (from src/core/field/Opcode.h)
// Simplified subset covering key opcodes
// ──────────────────────────────────────────────

public enum OpcodeKey : byte
{
    RET = 0x00,
    NOP = 0x01,
    STRAY = 0x02,
    REQ = 0x03,
    REQSW = 0x04,
    REQEW = 0x05,
    WAIT = 0x06,
    BATTLEM = 0x07,
    BATTLEF = 0x08,
    BATTLEA = 0x09,
    MOVEN = 0x0A,
    MOVEE = 0x0B,
    MOVES = 0x0C,
    MOVSW = 0x0D,
    MOVEW = 0x0E,
    MOVEVAR = 0x0F,
    MOVEVAR2 = 0x10,
    MOVEVAR3 = 0x11,
    MOVEN2 = 0x12,
    MOVEE2 = 0x13,
    MOVES2 = 0x14,
    MOVEW2 = 0x15,
    MOVESW2 = 0x16,
    FAacenN = 0x17,
    FACEE = 0x18,
    FACES = 0x19,
    FACEW = 0x1A,
    FACEVAR = 0x1B,
    ACEN = 0x1C,
    ACEE = 0x1D,
    ACES = 0x1E,
    ACEW = 0x1F,
    ACEVAR = 0x20,
    PLUS = 0x21,
    MINUS = 0x22,
    MUL = 0x23,
    DIV = 0x24,
    AND = 0x25,
    OR = 0x26,
    XOR = 0x27,
    ONBIT = 0x28,
    OFFBIT = 0x29,
    PLUS2 = 0x2A,
    MINUS2 = 0x2B,
    MUL2 = 0x2C,
    DIV2 = 0x2D,
    AND2 = 0x2E,
    OR2 = 0x2F,
    XOR2 = 0x30,
    ONBIT2 = 0x31,
    OFFBIT2 = 0x32,
    INC = 0x33,
    DEC = 0x34,
    EQUAL = 0x35,
    NOTEQUAL = 0x36,
    LESSTHAN = 0x37,
    GREATERTHAN = 0x38,
    LESSEQUAL = 0x39,
    GREATEREQUAL = 0x3A,
    IFEQUAL = 0x3B,
    IFNOTEQUAL = 0x3C,
    IFLESSTHAN = 0x3D,
    IFGREATERTHAN = 0x3E,
    IFLESSEQUAL = 0x3F,
    IFGREATEREQUAL = 0x40,
    IFAND = 0x41,
    IFOR = 0x42,
    IFXOR = 0x43,
    IFOB = 0x44,
    IFIO = 0x45,
    JMPF = 0x46,
    JMPFL = 0x47,
    JMPB = 0x48,
    JMPBL = 0x49,
    JMPEF = 0x4A,
    JMPEFL = 0x4B,
    JUMPEB = 0x4C,
    JUMPEBL = 0x4D,
    JMPOF = 0x4E,
    JMPIF = 0x4F,
    JMPPF = 0x50,
    CHARAP = 0x51,
    CHARAP2 = 0x52,
    CHARDP = 0x53,
    CHARDP2 = 0x54,
    CHARS = 0x55,
    CHARS2 = 0x56,
    CHARPOS = 0x57,
    CHARPOS2 = 0x58,
    CHARSPE = 0x59,
    CHARSPE2 = 0x5A,
    CHARSPS = 0x5B,
    CHARSPS2 = 0x5C,
    CHARSPD = 0x5D,
    CHARSPD2 = 0x5E,
    CHARSPD3 = 0x5F,
    CHARSPD4 = 0x60,
    CHARSPE3 = 0x61,
    CHARSPE4 = 0x62,
    CHARSPE5 = 0x63,
    CHARSPE6 = 0x64,
    CHARSPE7 = 0x65,
    CHARSPE8 = 0x66,
    CHARSPS3 = 0x67,
    CHARSPS4 = 0x68,
    CHARSPS5 = 0x69,
    CHARSPE9 = 0x6A,
    CHARSPE10 = 0x6B,
    CHARSPE11 = 0x6C,
    CHARSPE12 = 0x6D,
    CHARSPE13 = 0x6E,
    CHARSPE14 = 0x6F,
    CHARSPE15 = 0x70,
    CHARSPE16 = 0x71,
    CHARSPE17 = 0x72,
    CHARSPE18 = 0x73,
    CHARSPE19 = 0x74,
    CHARSPE20 = 0x75,
    CHARSPE21 = 0x76,
    CHARSPE22 = 0x77,
    CHARSPE23 = 0x78,
    CHARSPE24 = 0x79,
    CHARSPE25 = 0x7A,
    CHARSPE26 = 0x7B,
    CHARSPE27 = 0x7C,
    CHARSPE28 = 0x7D,
    CHARSPE29 = 0x7E,
    CHARSPE30 = 0x7F,
    CHARSPE31 = 0x80,
    CHARSPE32 = 0x81,
    CHARSPE33 = 0x82,
    CHARSPE34 = 0x83,
    CHARSPE35 = 0x84,
    CHARSPE36 = 0x85,
    CHARSPE37 = 0x86,
    CHARSPE38 = 0x87,
    CHARSPE39 = 0x88,
    CHARSPE40 = 0x89,
    CHARSPE41 = 0x8A,
    CHARSPE42 = 0x8B,
    CHARSPE43 = 0x8C,
    CHARSPE44 = 0x8D,
    CHARSPE45 = 0x8E,
    CHARSPE46 = 0x8F,
    CHARSPE47 = 0x90,
    CHARSPE48 = 0x91,
    CHARSPE49 = 0x92,
    CHARSPE50 = 0x93,
    CHARSPE51 = 0x94,
    CHARSPE52 = 0x95,
    CHARSPE53 = 0x96,
    CHARSPE54 = 0x97,
    CHARSPE55 = 0x98,
    CHARSPE56 = 0x99,
    CHARSPE57 = 0x9A,
    CHARSPE58 = 0x9B,
    CHARSPE59 = 0x9C,
    CHARSPE60 = 0x9D,
    CHARSPE61 = 0x9E,
    CHARSPE62 = 0x9F,
    CHARSPE63 = 0xA0,
    CHARSPE64 = 0xA1,
    CHARSPE65 = 0xA2,
    CHARSPE66 = 0xA3,
    CHARSPE67 = 0xA4,
    CHARSPE68 = 0xA5,
    CHARSPE69 = 0xA6,
    CHARSPE70 = 0xA7,
    CHARSPE71 = 0xA8,
    CHARSPE72 = 0xA9,
    CHARSPE73 = 0xAA,
    CHARSPE74 = 0xAB,
    CHARSPE75 = 0xAC,
    CHARSPE76 = 0xAD,
    CHARSPE77 = 0xAE,
    CHARSPE78 = 0xAF,
    CHARSPE79 = 0xB0,
    CHARSPE80 = 0xB1,
    CHARSPE81 = 0xB2,
    CHARSPE82 = 0xB3,
    CHARSPE83 = 0xB4,
    CHARSPE84 = 0xB5,
    CHARSPE85 = 0xB6,
    CHARSPE86 = 0xB7,
    CHARSPE87 = 0xB8,
    CHARSPE88 = 0xB9,
    CHARSPE89 = 0xBA,
    CHARSPE90 = 0xBB,
    CHARSPE91 = 0xBC,
    CHARSPE92 = 0xBD,
    CHARSPE93 = 0xBE,
    CHARSPE94 = 0xBF,
    CHARSPE95 = 0xC0,
    CHARSPE96 = 0xC1,
    CHARSPE97 = 0xC2,
    CHARSPE98 = 0xC3,
    CHARSPE99 = 0xC4,
    CHARSPE100 = 0xC5,
    CHARSPE101 = 0xC6,
    CHARSPE102 = 0xC7,
    CHARSPE103 = 0xC8,
    CHARSPE104 = 0xC9,
    CHARSPE105 = 0xCA,
    CHARSPE106 = 0xCB,
    CHARSPE107 = 0xCC,
    CHARSPE108 = 0xCD,
    CHARSPE109 = 0xCE,
    CHARSPE110 = 0xCF,
    CHARSPE111 = 0xD0,
    CHARSPE112 = 0xD1,
    CHARSPE113 = 0xD2,
    CHARSPE114 = 0xD3,
    CHARSPE115 = 0xD4,
    CHARSPE116 = 0xD5,
    CHARSPE117 = 0xD6,
    CHARSPE118 = 0xD7,
    CHARSPE119 = 0xD8,
    CHARSPE120 = 0xD9,
    CHARSPE121 = 0xDA,
    CHARSPE122 = 0xDB,
    CHARSPE123 = 0xDC,
    CHARSPE124 = 0xDD,
    CHARSPE125 = 0xDE,
    CHARSPE126 = 0xDF,
    CHARSPE127 = 0xE0,
    CHARSPE128 = 0xE1,
    CHARSPE129 = 0xE2,
    CHARSPE130 = 0xE3,
    CHARSPE131 = 0xE4,
    CHARSPE132 = 0xE5,
    CHARSPE133 = 0xE6,
    CHARSPE134 = 0xE7,
    CHARSPE135 = 0xE8,
    CHARSPE136 = 0xE9,
    CHARSPE137 = 0xEA,
    CHARSPE138 = 0xEB,
    CHARSPE139 = 0xEC,
    CHARSPE140 = 0xED,
    CHARSPE141 = 0xEE,
    CHARSPE142 = 0xEF,
    CHARSPE143 = 0xF0,
    CHARSPE144 = 0xF1,
    CHARSPE145 = 0xF2,
    CHARSPE146 = 0xF3,
    CHARSPE147 = 0xF4,
    SPECIAL = 0xF5,
    LABEL = 0xFF,
}

/// <summary>Base class for all opcodes.</summary>
public abstract class OpcodeBase
{
    public OpcodeKey Id { get; protected set; }
    protected OpcodeBase(OpcodeKey id) => Id = id;
    public virtual int Size => 1;
    public virtual byte[] ToByteArray() => [byte.Parse(Id.ToString())];
}

/// <summary>RET - Return from script.</summary>
public sealed class OpcodeRET : OpcodeBase
{
    public OpcodeRET() : base(OpcodeKey.RET) { }
}

/// <summary>NOP - No operation.</summary>
public sealed class OpcodeNOP : OpcodeBase
{
    public OpcodeNOP() : base(OpcodeKey.NOP) { }
}

/// <summary>STRAY - Display text.</summary>
public sealed class OpcodeSTRAY : OpcodeBase
{
    public byte GroupId { get; set; }
    public byte ScriptId { get; set; }
    public OpcodeSTRAY() : base(OpcodeKey.STRAY) { }
    public override int Size => 3;
}

/// <summary>REQ - Request group/script execution.</summary>
public sealed class OpcodeREQ : OpcodeBase
{
    public byte GroupId { get; set; }
    public byte ScriptId { get; set; }
    public OpcodeREQ() : base(OpcodeKey.REQ) { }
    public override int Size => 3;
}

/// <summary>WAIT - Wait for specified milliseconds.</summary>
public sealed class OpcodeWAIT : OpcodeBase
{
    public ushort Ms { get; set; }
    public OpcodeWAIT() : base(OpcodeKey.WAIT) { }
    public override int Size => 3;
}

/// <summary>JMPF - Short forward jump.</summary>
public sealed class OpcodeJMPF : OpcodeBase
{
    public byte Offset { get; set; }
    public string? Label { get; set; }
    public OpcodeJMPF() : base(OpcodeKey.JMPF) { }
    public override int Size => 2;
}

/// <summary>General Opcode wrapper that can hold any opcode variant.</summary>
public sealed class Opcode
{
    public OpcodeBase Inner { get; }
    public OpcodeKey Id => Inner.Id;

    public Opcode(OpcodeBase inner) => Inner = inner;

    // Factory methods
    public static Opcode Ret() => new(new OpcodeRET());
    public static Opcode Nop() => new(new OpcodeNOP());
    public static Opcode Stray(byte groupId, byte scriptId) => new(new OpcodeSTRAY { GroupId = groupId, ScriptId = scriptId });
    public static Opcode Req(byte groupId, byte scriptId) => new(new OpcodeREQ { GroupId = groupId, ScriptId = scriptId });
    public static Opcode Wait(ushort ms) => new(new OpcodeWAIT { Ms = ms });
    public static Opcode Jmpf(byte offset) => new(new OpcodeJMPF { Offset = offset });

    public override string ToString() => Inner.ToString() ?? Id.ToString();
}

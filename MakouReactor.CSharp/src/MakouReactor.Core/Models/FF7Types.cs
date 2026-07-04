namespace MakouReactor.Core.Models;

/// <summary>
/// 3D position used by FF7 field models.
/// Maps to <c>struct FF7Position</c> in the C++ codebase.
/// </summary>
public struct FF7Position
{
    public short X { get; set; }
    public short Y { get; set; }
    public short Z { get; set; }
    public ushort Id { get; set; }
    public bool HasZ { get; set; }
    public bool HasId { get; set; }

    public FF7Position(short x, short y, short z = 0, ushort id = 0, bool hasZ = false, bool hasId = false)
    {
        X = x;
        Y = y;
        Z = z;
        Id = id;
        HasZ = hasZ;
        HasId = hasId;
    }

    public override string ToString() => $"FF7Position(X={X}, Y={Y}, Z={Z}, Id={Id})";
    public override bool Equals(object? obj) => obj is FF7Position other && Equals(other);
    public bool Equals(FF7Position other) => X == other.X && Y == other.Y && Z == other.Z && Id == other.Id;
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, Id);
    public static bool operator ==(FF7Position left, FF7Position right) => left.Equals(right);
    public static bool operator !=(FF7Position left, FF7Position right) => !(left == right);
}

/// <summary>
/// Variable reference in FF7 script banks.
/// Maps to <c>struct FF7Var</c> in the C++ codebase.
/// </summary>
public readonly struct FF7Var
{
    public enum VarSize : byte
    {
        Byte = 0,
        Word = 1,
        SignedWord = 2,
        Bit = 3
    }

    [Flags]
    public enum Flag : byte
    {
        None = 0,
        Writable = 0x1
    }

    public byte Bank { get; }
    public byte Address { get; }
    public VarSize Size { get; }
    public Flag Flags { get; }

    public FF7Var(byte bank, byte address, VarSize size, Flag flags = Flag.None)
    {
        if (bank is <= 0 or > 0xF)
            throw new ArgumentOutOfRangeException(nameof(bank), "Bank must be 1-0xF.");

        Bank = bank;
        Address = address;
        Size = size;
        Flags = flags;
    }

    public override string ToString() => $"FF7Var(Bank=0x{Bank:X}, Addr=0x{Address:X}, Size={Size})";
    public override bool Equals(object? obj) => obj is FF7Var other && Equals(other);
    public bool Equals(FF7Var other) => Bank == other.Bank && Address == other.Address;
    public override int GetHashCode() => HashCode.Combine(Bank, Address);
    public static bool operator ==(FF7Var left, FF7Var right) => left.Equals(right);
    public static bool operator !=(FF7Var left, FF7Var right) => !(left == right);
    public static bool operator<(FF7Var left, FF7Var right)
    {
        if (left.Bank == right.Bank) return left.Address < right.Address;
        return left.Bank < right.Bank;
    }
    public static bool operator>(FF7Var left, FF7Var right) => right < left;
}

/// <summary>
/// Dialog window state used by FF7 field scripts.
/// Maps to <c>struct FF7Window</c> in the C++ codebase.
/// </summary>
public struct FF7Window
{
    public const byte NOWIN = 255;

    public short X { get; set; }
    public short Y { get; set; }
    public ushort W { get; set; }
    public ushort H { get; set; }
    public ushort AskFirst { get; set; }
    public ushort AskLast { get; set; }
    public byte Type { get; set; }
    public byte Mode { get; set; }
    public byte DisplayType { get; set; }
    public byte DisplayX { get; set; }
    public byte DisplayY { get; set; }
    public ushort GroupId { get; set; }
    public ushort ScriptId { get; set; }
    public ushort OpcodeId { get; set; }

    /// <summary>
    /// Compute the clamped real screen position respecting the 312x223 dialog area.
    /// </summary>
    public (int X, int Y) RealPos()
    {
        // MPNAM opcode key is 0x43 in the OpcodeKey enum
        if (Type == NOWIN || Type == 0x43)
            return (0, 0);

        int windowX = X, windowY = Y;

        if (windowX + W > 312) windowX = 312 - W;
        if (windowY + H > 223) windowY = 223 - H;
        if (windowX < 8) windowX = 8;
        if (windowY < 8) windowY = 8;

        return (windowX, windowY);
    }

    public override string ToString() => $"FF7Window({X},{Y} {W}x{H})";
    public override bool Equals(object? obj) => obj is FF7Window other && Equals(other);
    public bool Equals(FF7Window other) => X == other.X && Y == other.Y && W == other.W && H == other.H;
    public override int GetHashCode() => HashCode.Combine(X, Y, W, H);
    public static bool operator ==(FF7Window left, FF7Window right) => left.Equals(right);
    public static bool operator !=(FF7Window left, FF7Window right) => !(left == right);
}

/// <summary>
/// Wrapper around a string with length info, mirroring the C++ FF7String.
/// </summary>
public sealed class FF7String
{
    public string Value { get; }
    public byte[]? RawBytes { get; }
    public int Length => Value?.Length ?? 0;

    public FF7String(string? value = null, byte[]? rawBytes = null)
    {
        Value = value ?? string.Empty;
        RawBytes = rawBytes?.ToArray();
    }

    public static FF7String FromRaw(ReadOnlySpan<byte> rawBytes, bool japanese = false) =>
        new(FF7TextCodec.Decode(rawBytes, japanese), rawBytes.ToArray());

    public override string ToString() => Value;
    public static implicit operator string(FF7String s) => s.Value;
    public static implicit operator FF7String(string s) => new(s);
}

/// <summary>
/// Rectangle used for trigger zones and window bounds.
/// </summary>
public readonly struct Rectangle
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    public Rectangle(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public bool Contains(int x, int y) =>
        x >= X && x < X + Width && y >= Y && y < Y + Height;

    public override string ToString() => $"Rectangle({X},{Y} {Width}x{Height})";
    public override bool Equals(object? obj) => obj is Rectangle other && Equals(other);
    public bool Equals(Rectangle other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    public static bool operator ==(Rectangle left, Rectangle right) => left.Equals(right);
    public static bool operator !=(Rectangle left, Rectangle right) => !(left == right);
}

/// <summary>
/// 2D point used for actor positions and spawn points.
/// </summary>
public readonly struct Point
{
    public int X { get; }
    public int Y { get; }

    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }

    public override string ToString() => $"Point({X},{Y})";
    public override bool Equals(object? obj) => obj is Point other && Equals(other);
    public bool Equals(Point other) => X == other.X && Y == other.Y;
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public static bool operator ==(Point left, Point right) => left.Equals(right);
    public static bool operator !=(Point left, Point right) => !(left == right);
}

/// <summary>
/// Rect is an alias for Rectangle, used by the AI module.
/// Provides Contains overloads for Point and Rect.
/// </summary>
public readonly struct Rect
{
    public static Rect Empty => new(0, 0, 0, 0);

    public int X { get; }
    public int Y { get; }
    public int W { get; }
    public int H { get; }

    // Aliases for Width/Height (used by LayoutGenerator)
    public int Width => W;
    public int Height => H;

    public Rect(int x, int y, int w, int h)
    {
        X = x;
        Y = y;
        W = w;
        H = h;
    }

    public bool Contains(Point p) => p.X >= X && p.X < X + W && p.Y >= Y && p.Y < Y + H;
    public bool Contains(Rect r) => r.X >= X && r.Y >= Y && r.X + r.W < X + W && r.Y + r.H < Y + H;

    public override string ToString() => $"Rect({X},{Y} {W}x{H})";
    public override bool Equals(object? obj) => obj is Rect other && Equals(other);
    public bool Equals(Rect other) => X == other.X && Y == other.Y && W == other.W && H == other.H;
    public override int GetHashCode() => HashCode.Combine(X, Y, W, H);
    public static bool operator ==(Rect left, Rect right) => left.Equals(right);
    public static bool operator !=(Rect left, Rect right) => !(left == right);

    public static implicit operator Rectangle(Rect r) => new Rectangle(r.X, r.Y, r.W, r.H);
    public static implicit operator Rect(Rectangle r) => new Rect(r.X, r.Y, r.Width, r.Height);
}

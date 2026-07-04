using System.Collections.Generic;
using System.Buffers.Binary;
using System.IO;

namespace MakouReactor.Core.Models;

// ──────────────────────────────────────────────
// IdFile - Walkmesh data (from src/core/field/IdFile.h)
// ──────────────────────────────────────────────

public readonly struct VertexSR
{
    public short X { get; }
    public short Y { get; }
    public short Z { get; }
    public short Res { get; }
    public VertexSR(short x, short y, short z, short res)
        => (X, Y, Z, Res) = (x, y, z, res);
}

public readonly struct Triangle
{
    public VertexSR[] Vertices { get; }
    public Triangle(VertexSR v0, VertexSR v1, VertexSR v2) => Vertices = [v0, v1, v2];
}

public readonly struct Access
{
    public short[] A { get; }
    public Access(short a0, short a1, short a2) => A = [a0, a1, a2];
}

public sealed class IdFile
{
    private const int TriangleSize = 24;
    private const int AccessSize = 6;

    public List<Triangle> Triangles { get; } = [];
    public List<Access> Access { get; } = [];

    public int TriangleCount => Triangles.Count;
    public bool HasTriangle => Triangles.Count > 0;

    public static IdFile Open(ReadOnlySpan<byte> data)
    {
        var id = new IdFile();
        id.Load(data);
        return id;
    }

    public void Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
            throw new InvalidDataException("Walkmesh section is too short.");

        Triangles.Clear();
        Access.Clear();

        var triangleCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[..4]));
        var triangleBytes = checked(triangleCount * TriangleSize);
        var accessStart = sizeof(uint) + triangleBytes;
        var accessBytes = checked(triangleCount * AccessSize);
        var expectedSize = accessStart + accessBytes;

        if (data.Length != expectedSize && data.Length != expectedSize + 2)
            throw new InvalidDataException("Walkmesh section size does not match the triangle count.");

        for (var i = 0; i < triangleCount; i++)
        {
            var triangleOffset = sizeof(uint) + (i * TriangleSize);
            Triangles.Add(ReadTriangle(data.Slice(triangleOffset, TriangleSize)));
            var accessOffset = accessStart + (i * AccessSize);
            Access.Add(ReadAccess(data.Slice(accessOffset, AccessSize)));
        }
    }

    public Triangle? Triangle(int triangleId) =>
        triangleId >= 0 && triangleId < Triangles.Count ? Triangles[triangleId] : null;

    public void SetTriangle(int index, Triangle triangle)
    {
        if (index >= 0 && index < Triangles.Count)
            Triangles[index] = triangle;
    }

    public void InsertTriangle(int index, Triangle triangle)
    {
        if (index >= 0 && index <= Triangles.Count)
        {
            Triangles.Insert(index, triangle);
            Access.Insert(index, new Access(-1, -1, -1));
        }
    }

    public void RemoveTriangle(int index)
    {
        if (index >= 0 && index < Triangles.Count)
        {
            Triangles.RemoveAt(index);
            if (index < Access.Count)
                Access.RemoveAt(index);
        }
    }

    public Access? AccessPoint(int triangleId) =>
        triangleId >= 0 && triangleId < Access.Count ? Access[triangleId] : null;

    public void SetAccess(int triangleId, Access access)
    {
        while (Access.Count <= triangleId)
            Access.Add(new Access(0, 0, 0));
        Access[triangleId] = access;
    }

    public byte[] Save()
    {
        if (Access.Count != Triangles.Count)
            throw new InvalidDataException("Walkmesh access table must contain one row per triangle.");

        var output = new byte[sizeof(uint) + (Triangles.Count * TriangleSize) + (Access.Count * AccessSize)];
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0, sizeof(uint)), checked((uint)Triangles.Count));
        var triangleOffset = sizeof(uint);
        for (var index = 0; index < Triangles.Count; index++)
        {
            WriteTriangle(output.AsSpan(triangleOffset, TriangleSize), Triangles[index]);
            triangleOffset += TriangleSize;
        }

        var accessOffset = sizeof(uint) + (Triangles.Count * TriangleSize);
        for (var index = 0; index < Access.Count; index++)
        {
            WriteAccess(output.AsSpan(accessOffset, AccessSize), Access[index]);
            accessOffset += AccessSize;
        }

        return output;
    }

    private static Triangle ReadTriangle(ReadOnlySpan<byte> data) =>
        new(ReadVertex(data[..8]), ReadVertex(data.Slice(8, 8)), ReadVertex(data.Slice(16, 8)));

    private static VertexSR ReadVertex(ReadOnlySpan<byte> data) =>
        new(
            BinaryPrimitives.ReadInt16LittleEndian(data[..2]),
            BinaryPrimitives.ReadInt16LittleEndian(data.Slice(2, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(data.Slice(4, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(data.Slice(6, 2)));

    private static Access ReadAccess(ReadOnlySpan<byte> data) =>
        new(
            BinaryPrimitives.ReadInt16LittleEndian(data[..2]),
            BinaryPrimitives.ReadInt16LittleEndian(data.Slice(2, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(data.Slice(4, 2)));

    private static void WriteTriangle(Span<byte> data, Triangle triangle)
    {
        WriteVertex(data[..8], triangle.Vertices[0]);
        WriteVertex(data.Slice(8, 8), triangle.Vertices[1]);
        WriteVertex(data.Slice(16, 8), triangle.Vertices[2]);
    }

    private static void WriteVertex(Span<byte> data, VertexSR vertex)
    {
        BinaryPrimitives.WriteInt16LittleEndian(data[..2], vertex.X);
        BinaryPrimitives.WriteInt16LittleEndian(data.Slice(2, 2), vertex.Y);
        BinaryPrimitives.WriteInt16LittleEndian(data.Slice(4, 2), vertex.Z);
        BinaryPrimitives.WriteInt16LittleEndian(data.Slice(6, 2), vertex.Res);
    }

    private static void WriteAccess(Span<byte> data, Access access)
    {
        BinaryPrimitives.WriteInt16LittleEndian(data[..2], access.A[0]);
        BinaryPrimitives.WriteInt16LittleEndian(data.Slice(2, 2), access.A[1]);
        BinaryPrimitives.WriteInt16LittleEndian(data.Slice(4, 2), access.A[2]);
    }
}

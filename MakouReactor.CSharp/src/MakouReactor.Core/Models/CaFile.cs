using System.Buffers.Binary;

namespace MakouReactor.Core.Models;

/// <summary>A camera axis in 4.12 fixed point (<c>Vertex_s</c> in <c>src/core/field/CaFile.h</c>).</summary>
/// <param name="X">X component.</param>
/// <param name="Y">Y component.</param>
/// <param name="Z">Z component.</param>
public readonly record struct CameraAxis(short X, short Y, short Z);

/// <summary>
/// One field camera (the <c>Camera</c> struct of <c>src/core/field/CaFile.h</c>): three axes in
/// 4.12 fixed point (4096 = 1.0), a position and the zoom (the projection distance in pixels).
/// </summary>
/// <param name="AxisX">Camera X axis (<c>camera_axis[0]</c>).</param>
/// <param name="AxisY">Camera Y axis (<c>camera_axis[1]</c>).</param>
/// <param name="AxisZ">Camera Z axis (<c>camera_axis[2]</c>), the viewing direction.</param>
/// <param name="PositionX">First component of <c>camera_position</c>.</param>
/// <param name="PositionY">Second component of <c>camera_position</c>.</param>
/// <param name="PositionZ">Third component of <c>camera_position</c>.</param>
/// <param name="Zoom"><c>camera_zoom</c>.</param>
public sealed record FieldCamera(
    CameraAxis AxisX,
    CameraAxis AxisY,
    CameraAxis AxisZ,
    int PositionX,
    int PositionY,
    int PositionZ,
    ushort Zoom)
{
    /// <summary>
    /// The camera <c>CaFile::initEmpty()</c> (CaFile.cpp) gives a new field.
    /// </summary>
    public static FieldCamera Default { get; } = new(
        new CameraAxis(4094, -101, -1),
        new CameraAxis(-59, -2390, -3327),
        new CameraAxis(81, 3325, -2391),
        -42, -255, 1476,
        525);
}

/// <summary>
/// Read-only port of <c>CaFile::open</c> (<c>src/core/field/CaFile.cpp</c>): the camera section
/// (section 2 of a PC field). The section is not re-encoded; fields keep its original bytes.
/// </summary>
public sealed class CaFile
{
    private const int PcCameraSize = 38;
    private const int PsCameraSize = 40;
    private const int PsExtraCameraSize = 18;

    private CaFile(IReadOnlyList<FieldCamera> cameras) => Cameras = cameras;

    /// <summary>Cameras in file order; camera 0 is the one a field starts with.</summary>
    public IReadOnlyList<FieldCamera> Cameras { get; }

    /// <summary>True when the section holds at least one camera (<c>CaFile::hasCamera</c>).</summary>
    public bool HasCamera => Cameras.Count > 0;

    /// <summary>
    /// Parses a camera section with the size rules of <c>CaFile::open</c>: 40 bytes or
    /// 38 + n x 18 bytes is the PlayStation layout (extra cameras store only their axes), a
    /// multiple of 38 bytes is the PC layout. Like the C++ <c>memcpy</c> into a reused struct,
    /// a partially stored camera keeps the previous camera's remaining fields.
    /// </summary>
    /// <returns>The cameras, or null when the size matches neither layout.</returns>
    public static CaFile? Open(ReadOnlySpan<byte> data)
    {
        var size = data.Length;
        bool isPc;
        if (size == PsCameraSize || (size - PcCameraSize > 0 && (size - PcCameraSize) % PsExtraCameraSize == 0))
            isPc = false;
        else if (size > 0 && size % PcCameraSize == 0)
            isPc = true;
        else
            return null;

        var cameras = new List<FieldCamera>();
        var first = Read(data[..PcCameraSize], previous: null);
        cameras.Add(first);

        if (size > PsCameraSize)
        {
            var previous = first;
            if (isPc)
            {
                for (var i = 1; i < size / PcCameraSize; i++)
                {
                    previous = Read(data.Slice(i * PcCameraSize, PcCameraSize), previous);
                    cameras.Add(previous);
                }
            }
            else
            {
                for (var i = 0; i < (size - PcCameraSize) / PsExtraCameraSize; i++)
                {
                    previous = Read(data.Slice(PcCameraSize + (i * PsExtraCameraSize), PsExtraCameraSize), previous);
                    cameras.Add(previous);
                }
            }
        }

        return new CaFile(cameras);
    }

    private static FieldCamera Read(ReadOnlySpan<byte> data, FieldCamera? previous)
    {
        static CameraAxis Axis(ReadOnlySpan<byte> d, int offset) => new(
            BinaryPrimitives.ReadInt16LittleEndian(d[offset..]),
            BinaryPrimitives.ReadInt16LittleEndian(d[(offset + 2)..]),
            BinaryPrimitives.ReadInt16LittleEndian(d[(offset + 4)..]));

        var axisX = Axis(data, 0);
        var axisY = Axis(data, 6);
        var axisZ = Axis(data, 12);
        if (data.Length < PcCameraSize)
        {
            var p = previous ?? FieldCamera.Default;
            return new FieldCamera(axisX, axisY, axisZ, p.PositionX, p.PositionY, p.PositionZ, p.Zoom);
        }

        // Offsets 18-19 hold camera_axis2z (a padding copy of axis[2].z), 32-35 are blank.
        return new FieldCamera(
            axisX,
            axisY,
            axisZ,
            BinaryPrimitives.ReadInt32LittleEndian(data[20..]),
            BinaryPrimitives.ReadInt32LittleEndian(data[24..]),
            BinaryPrimitives.ReadInt32LittleEndian(data[28..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[36..]));
    }
}

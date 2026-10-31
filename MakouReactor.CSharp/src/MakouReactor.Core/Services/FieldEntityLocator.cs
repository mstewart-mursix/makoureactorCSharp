using System.Buffers.Binary;

using MakouReactor.Core.Models;

namespace MakouReactor.Core.Services;

/// <summary>Where a field group places its entity, from the first constant placement opcode found.</summary>
/// <param name="GroupIndex">Group (entity) index in section 1.</param>
/// <param name="GroupName">Group name.</param>
/// <param name="X">Field X.</param>
/// <param name="Y">Field Y.</param>
/// <param name="Opcode">Placement opcode name (XYZI, XYI or XYZ).</param>
/// <param name="IsModel">True when the group is a model (character/NPC) group.</param>
public sealed record FieldEntityPosition(int GroupIndex, string GroupName, int X, int Y, string Opcode, bool IsModel);

/// <summary>
/// Finds the initial positions of a field's entities by scanning each group's scripts, init first,
/// for the first placement opcode whose X and Y are constants. Layouts follow <c>OpcodeXYZI</c>,
/// <c>OpcodeXYI</c> and <c>OpcodeXYZ</c> in <c>src/core/field/Opcode.h</c>: id, <c>banks[2]</c>,
/// then <c>qint16 targetX</c> (bank <c>B1(banks[0])</c>) and <c>qint16 targetY</c> (bank <c>B2(banks[0])</c>).
/// Positions held in variables (non-zero bank) are skipped, since their value is only known at run time.
/// </summary>
public static class FieldEntityLocator
{
    private const byte Xyzi = 0xA5;
    private const byte Xyi = 0xA6;
    private const byte Xyz = 0xA7;

    /// <summary>One entry per group that has a constant placement.</summary>
    public static IReadOnlyList<FieldEntityPosition> Locate(Section1File? section)
    {
        var result = new List<FieldEntityPosition>();
        if (section == null)
            return result;

        for (var groupIndex = 0; groupIndex < section.GrpScripts.Count; groupIndex++)
        {
            var group = section.GrpScripts[groupIndex];
            foreach (var script in group.Scripts)
            {
                if (TryFindPlacement(script, out var x, out var y, out var name))
                {
                    result.Add(new FieldEntityPosition(groupIndex, group.Name, x, y, name, group.Type == GrpScriptType.Model));
                    break;
                }
            }
        }

        return result;
    }

    private static bool TryFindPlacement(Script script, out int x, out int y, out string name)
    {
        foreach (var op in script.RawOpcodes)
        {
            if (op.Id is not (Xyzi or Xyi or Xyz) || op.Bytes.Length < 7)
                continue;

            var banks0 = op.Bytes[1];
            if ((banks0 >> 4) != 0 || (banks0 & 0x0F) != 0)
                continue; // X or Y comes from a variable

            x = BinaryPrimitives.ReadInt16LittleEndian(op.Bytes.AsSpan(3, 2));
            y = BinaryPrimitives.ReadInt16LittleEndian(op.Bytes.AsSpan(5, 2));
            name = op.Id switch { Xyzi => "XYZI", Xyi => "XYI", _ => "XYZ" };
            return true;
        }

        x = y = 0;
        name = string.Empty;
        return false;
    }
}

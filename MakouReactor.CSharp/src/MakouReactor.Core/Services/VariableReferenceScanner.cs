using MakouReactor.Core.Models;

namespace MakouReactor.Core.Services;

public sealed record VariableReference(
    int GroupIndex,
    string GroupName,
    int ScriptIndex,
    int OpcodeOffset,
    string OpcodeName,
    byte Bank,
    byte Address,
    string Size,
    bool Writable,
    string Location)
{
    public string BankHex => $"0x{Bank:X}";
    public string AddressHex => $"0x{Address:X2}";
}

public sealed class VariableReferenceScanner
{
    public IReadOnlyList<VariableReference> Scan(Section1File section)
    {
        ArgumentNullException.ThrowIfNull(section);

        var references = new List<VariableReference>();
        for (var groupIndex = 0; groupIndex < section.GrpScripts.Count; groupIndex++)
        {
            var group = section.GrpScripts[groupIndex];
            for (var scriptIndex = 0; scriptIndex < group.Scripts.Count; scriptIndex++)
            {
                foreach (var opcode in group.Scripts[scriptIndex].RawOpcodes)
                    AddOpcodeReferences(references, groupIndex, group.Name, scriptIndex, opcode);
            }
        }

        return references;
    }

    private static void AddOpcodeReferences(
        List<VariableReference> references,
        int groupIndex,
        string groupName,
        int scriptIndex,
        RawOpcode opcode)
    {
        if (opcode.Bytes.Length < opcode.DeclaredSize || opcode.Bytes.Length < 2)
            return;

        var banks = opcode.Bytes[1];
        var bank1 = LowNibble(banks);
        var bank2 = HighNibble(banks);

        switch (opcode.Id)
        {
            case >= 0x80 and <= 0x85:
            case 0x87:
            case 0x89:
            case 0x8B:
            case 0x8D:
            case 0x8F:
            case 0x91:
            case 0x93:
            case 0x9A:
                AddBinary(references, groupIndex, groupName, scriptIndex, opcode, bank1, opcode.Bytes[2], "Byte", true);
                AddBinary(references, groupIndex, groupName, scriptIndex, opcode, bank2, opcode.Bytes[3], "Byte", false);
                break;

            case 0x86:
            case 0x88:
            case 0x8A:
            case 0x8C:
            case 0x8E:
            case 0x90:
            case 0x92:
            case 0x94:
            case 0x9B:
                AddBinary(references, groupIndex, groupName, scriptIndex, opcode, bank1, opcode.Bytes[2], "Word", true);
                AddBinary(references, groupIndex, groupName, scriptIndex, opcode, bank2, opcode.Bytes[3], "Word", false);
                break;

            case 0x95:
            case 0x97:
            case 0x99:
                AddUnary(references, groupIndex, groupName, scriptIndex, opcode, bank2, opcode.Bytes[2], "Byte", true);
                break;

            case 0x96:
            case 0x98:
                AddUnary(references, groupIndex, groupName, scriptIndex, opcode, bank2, opcode.Bytes[2], "Word", true);
                break;
        }
    }

    private static void AddBinary(
        List<VariableReference> references,
        int groupIndex,
        string groupName,
        int scriptIndex,
        RawOpcode opcode,
        byte bank,
        byte address,
        string size,
        bool writable)
    {
        if (bank == 0)
            return;

        references.Add(Create(groupIndex, groupName, scriptIndex, opcode, bank, address, size, writable));
    }

    private static void AddUnary(
        List<VariableReference> references,
        int groupIndex,
        string groupName,
        int scriptIndex,
        RawOpcode opcode,
        byte bank,
        byte address,
        string size,
        bool writable)
    {
        if (bank == 0)
            return;

        references.Add(Create(groupIndex, groupName, scriptIndex, opcode, bank, address, size, writable));
    }

    private static VariableReference Create(
        int groupIndex,
        string groupName,
        int scriptIndex,
        RawOpcode opcode,
        byte bank,
        byte address,
        string size,
        bool writable)
    {
        var location = $"Group {groupIndex}: {groupName} / {ScriptName(scriptIndex)} / {opcode.OffsetHex}";
        return new VariableReference(groupIndex, groupName, scriptIndex, opcode.Offset, opcode.Name,
            bank, address, size, writable, location);
    }

    private static byte LowNibble(byte value) => (byte)(value & 0x0F);
    private static byte HighNibble(byte value) => (byte)(value >> 4);

    private static string ScriptName(int index) => index switch
    {
        0 => "S0 - Init",
        1 => "S0 - Main",
        2 => "S1 - Talk",
        3 => "S2 - Contact",
        _ => $"Script {index - 1}",
    };
}

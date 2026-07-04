using System.Buffers.Binary;
using System.IO;

namespace MakouReactor.Core.Models;

public sealed class EncounterFile
{
    private const int TableSize = 24;
    private const int TableCount = 2;

    public EncounterTable Table1 { get; private set; } = EncounterTable.Empty;
    public EncounterTable Table2 { get; private set; } = EncounterTable.Empty;

    public IReadOnlyList<EncounterTable> Tables => [Table1, Table2];

    public static EncounterFile Open(ReadOnlySpan<byte> data)
    {
        var encounter = new EncounterFile();
        encounter.Load(data);
        return encounter;
    }

    public void Load(ReadOnlySpan<byte> data)
    {
        if (data.Length != TableSize * TableCount)
            throw new InvalidDataException($"Encounter section must be {TableSize * TableCount} bytes.");

        Table1 = ReadTable(data[..TableSize]);
        Table2 = ReadTable(data.Slice(TableSize, TableSize));
    }

    public EncounterTable GetTable(int index) => index switch
    {
        0 => Table1,
        1 => Table2,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public void SetTable(int index, EncounterTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        ValidateTable(table);
        if (index == 0)
            Table1 = table;
        else if (index == 1)
            Table2 = table;
        else
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    public byte[] Save()
    {
        var output = new byte[TableSize * TableCount];
        WriteTable(output.AsSpan(0, TableSize), Table1);
        WriteTable(output.AsSpan(TableSize, TableSize), Table2);
        return output;
    }

    private static EncounterTable ReadTable(ReadOnlySpan<byte> data)
    {
        var standard = new EncounterBattle[6];
        var special = new EncounterBattle[4];
        var offset = 2;

        for (var i = 0; i < standard.Length; i++)
        {
            standard[i] = ReadBattle(BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2)));
            offset += 2;
        }

        for (var i = 0; i < special.Length; i++)
        {
            special[i] = ReadBattle(BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2)));
            offset += 2;
        }

        return new EncounterTable((data[0] & 1) != 0, data[1], standard, special);
    }

    private static EncounterBattle ReadBattle(ushort value) =>
        new((ushort)(value & 0x03FF), (byte)(value >> 10));

    private static void WriteTable(Span<byte> data, EncounterTable table)
    {
        ValidateTable(table);
        data.Clear();
        data[0] = table.Enabled ? (byte)1 : (byte)0;
        data[1] = table.Rate;

        var offset = 2;
        foreach (var battle in table.StandardBattles)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset, 2), PackBattle(battle));
            offset += 2;
        }

        foreach (var battle in table.SpecialBattles)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset, 2), PackBattle(battle));
            offset += 2;
        }
    }

    private static ushort PackBattle(EncounterBattle battle)
    {
        if (battle.BattleId > 0x03FF)
            throw new InvalidDataException("Encounter battle id must fit in 10 bits.");
        if (battle.Probability > 0x3F)
            throw new InvalidDataException("Encounter probability must fit in 6 bits.");

        return (ushort)((battle.Probability << 10) | battle.BattleId);
    }

    private static void ValidateTable(EncounterTable table)
    {
        if (table.StandardBattles.Count != 6)
            throw new InvalidDataException("Encounter table must contain 6 standard battles.");
        if (table.SpecialBattles.Count != 4)
            throw new InvalidDataException("Encounter table must contain 4 special battles.");
    }
}

public sealed record EncounterTable(
    bool Enabled,
    byte Rate,
    IReadOnlyList<EncounterBattle> StandardBattles,
    IReadOnlyList<EncounterBattle> SpecialBattles)
{
    public static EncounterTable Empty { get; } = new(false, 0,
        Array.Empty<EncounterBattle>(),
        Array.Empty<EncounterBattle>());
}

public sealed record EncounterBattle(ushort BattleId, byte Probability);

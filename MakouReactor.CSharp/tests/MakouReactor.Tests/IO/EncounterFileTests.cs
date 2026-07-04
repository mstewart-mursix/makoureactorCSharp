using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public sealed class EncounterFileTests
{
    [Fact]
    public void open_reads_two_encounter_tables()
    {
        var encounters = EncounterFile.Open(BuildEncounters());

        encounters.Table1.Enabled.Should().BeTrue();
        encounters.Table1.Rate.Should().Be(32);
        encounters.Table1.StandardBattles[0].BattleId.Should().Be(0x123);
        encounters.Table1.StandardBattles[0].Probability.Should().Be(12);
        encounters.Table1.SpecialBattles[0].BattleId.Should().Be(0x020);
        encounters.Table1.SpecialBattles[0].Probability.Should().Be(4);
        encounters.Table2.Enabled.Should().BeFalse();
        encounters.Table2.Rate.Should().Be(64);
    }

    [Fact]
    public void open_rejects_invalid_size()
    {
        var open = () => EncounterFile.Open([0x00]);

        open.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void save_round_trips_edited_encounter_tables()
    {
        var encounters = EncounterFile.Open(BuildEncounters());
        encounters.SetTable(0, encounters.Table1 with
        {
            Enabled = false,
            Rate = 55,
            StandardBattles = encounters.Table1.StandardBattles
                .Select((battle, index) => index == 0
                    ? battle with { BattleId = 0x321, Probability = 21 }
                    : battle)
                .ToArray(),
        });

        var reopened = EncounterFile.Open(encounters.Save());

        reopened.Table1.Enabled.Should().BeFalse();
        reopened.Table1.Rate.Should().Be(55);
        reopened.Table1.StandardBattles[0].BattleId.Should().Be(0x321);
        reopened.Table1.StandardBattles[0].Probability.Should().Be(21);
        reopened.Table2.Rate.Should().Be(64);
    }

    [Fact]
    public void save_rejects_values_that_do_not_fit_packed_encounter_format()
    {
        var encounters = EncounterFile.Open(BuildEncounters());
        encounters.SetTable(0, encounters.Table1 with
        {
            StandardBattles = encounters.Table1.StandardBattles
                .Select((battle, index) => index == 0
                    ? battle with { BattleId = 0x400 }
                    : battle)
                .ToArray(),
        });

        var save = () => encounters.Save();

        save.Should().Throw<InvalidDataException>()
            .WithMessage("Encounter battle id must fit in 10 bits.");
    }

    internal static byte[] BuildEncounters()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteTable(writer, enabled: true, rate: 32, standardBase: 0x123, specialBase: 0x020);
        WriteTable(writer, enabled: false, rate: 64, standardBase: 0x200, specialBase: 0x040);

        return stream.ToArray();
    }

    private static void WriteTable(BinaryWriter writer, bool enabled, byte rate, int standardBase, int specialBase)
    {
        writer.Write((byte)(enabled ? 1 : 0));
        writer.Write(rate);

        for (var i = 0; i < 6; i++)
            writer.Write(PackBattle(standardBase + i, 12 + i));

        for (var i = 0; i < 4; i++)
            writer.Write(PackBattle(specialBase + i, 4 + i));

        writer.Write((ushort)0);
    }

    private static ushort PackBattle(int battleId, int probability) =>
        checked((ushort)((probability << 10) | (battleId & 0x03FF)));
}

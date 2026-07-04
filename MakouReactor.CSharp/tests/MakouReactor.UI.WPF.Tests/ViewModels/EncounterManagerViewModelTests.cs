using System.Buffers.Binary;

using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.ViewModels;

public sealed class EncounterManagerViewModelTests
{
    [Fact]
    public void view_model_exposes_encounter_tables()
    {
        var field = FieldPC.OpenDecompressed("md1stin", BuildField());
        var viewModel = new EncounterManagerViewModel("md1stin", field);

        viewModel.FieldName.Should().Be("md1stin");
        viewModel.Table1.Enabled.Should().BeTrue();
        viewModel.Table1.Rate.Should().Be(32);
        viewModel.Table1.Rows.Should().HaveCount(10);
        viewModel.Table1.Rows[0].BattleId.Should().Be(0x123);
        viewModel.Table2.Rate.Should().Be(64);
        viewModel.StatusText.Should().Be("Encounter tables loaded.");
    }

    [Fact]
    public void editing_row_marks_modified_and_apply_updates_field()
    {
        var field = FieldPC.OpenDecompressed("md1stin", BuildField());
        var viewModel = new EncounterManagerViewModel("md1stin", field);

        viewModel.Table2.Rate = 77;
        viewModel.Table2.Rows.Single(row => row.Type == "Special" && row.Slot == 0).BattleId = 0x155;
        viewModel.Table2.Rows.Single(row => row.Type == "Special" && row.Slot == 0).Probability = 17;
        viewModel.ApplyChanges();

        viewModel.IsModified.Should().BeFalse();
        field.IsModified.Should().BeTrue();
        var reopened = FieldPC.OpenDecompressed("md1stin", field.SaveDecompressed());
        reopened.Encounters!.Table2.Rate.Should().Be(77);
        reopened.Encounters.Table2.SpecialBattles[0].BattleId.Should().Be(0x155);
        reopened.Encounters.Table2.SpecialBattles[0].Probability.Should().Be(17);
    }

    private static byte[] BuildField()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[6] = BuildEncounters();

        var headerSize = 42;
        var footer = "FINAL FANTASY7"u8.ToArray();
        var size = headerSize + sections.Sum(static section => 4 + section.Length) + footer.Length;
        var data = new byte[size];
        data[0] = 0;
        data[1] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(2, 4), 9);
        var cursor = headerSize;
        for (var index = 0; index < sections.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(6 + (index * 4), 4), (uint)cursor);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(cursor, 4), (uint)sections[index].Length);
            sections[index].CopyTo(data.AsSpan(cursor + 4));
            cursor += 4 + sections[index].Length;
        }

        footer.CopyTo(data.AsSpan(cursor));
        return data;
    }

    private static byte[] BuildEncounters()
    {
        var data = new byte[48];
        WriteTable(data.AsSpan(0, 24), enabled: true, rate: 32, standardBase: 0x123, specialBase: 0x020);
        WriteTable(data.AsSpan(24, 24), enabled: false, rate: 64, standardBase: 0x200, specialBase: 0x040);
        return data;
    }

    private static void WriteTable(Span<byte> data, bool enabled, byte rate, int standardBase, int specialBase)
    {
        data[0] = enabled ? (byte)1 : (byte)0;
        data[1] = rate;
        var offset = 2;
        for (var index = 0; index < 6; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset, 2), PackBattle(standardBase + index, 12 + index));
            offset += 2;
        }

        for (var index = 0; index < 4; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset, 2), PackBattle(specialBase + index, 4 + index));
            offset += 2;
        }
    }

    private static ushort PackBattle(int battleId, int probability) =>
        checked((ushort)((probability << 10) | (battleId & 0x03FF)));
}

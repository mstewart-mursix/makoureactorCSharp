using System.Buffers.Binary;
using System.Text;

using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Fields;

/// <summary>
/// Audit of <see cref="Section1File"/> against <c>Section1File::open</c> / <c>Section1File::save</c>
/// and <c>GrpScript::toByteArray</c> in <c>src/core/field/</c>. The fixtures are built in code with
/// the same layout the C++ <c>save()</c> produces, so Open → Save must give identical bytes.
/// </summary>
public class Section1FileAuditTests
{
    private static readonly byte[] InitScript = [0x5F, 0x00];          // NOP, RET
    private static readonly byte[] MainScript = [0x24, 0x10, 0x00, 0x00]; // WAIT 16, RET
    private static readonly byte[] TalkScript = [0x5F, 0x5F, 0x00];    // NOP, NOP, RET

    [Fact]
    public void canonical_section_resaves_byte_identical_when_marked_modified()
    {
        var original = Canonical.Build(
            [
                Canonical.Group("dic", [[.. InitScript, .. MainScript]]),
                Canonical.Group("cloud", [[0xA1, 0x00, 0x00], TalkScript]),
            ],
            [Raw("Map"), Raw("Hello!")],
            [Akao(10), Tuto()],
            modelCount: 1);

        var section = Section1File.Open(original);
        section.MarkModified();

        section.Save().Should().Equal(original);
    }

    [Fact]
    public void script_zero_is_split_into_init_and_main_slots()
    {
        var data = Canonical.Build(
            [Canonical.Group("dic", [[.. InitScript, .. MainScript], TalkScript])],
            [Raw("Map")],
            []);

        var group = Section1File.Open(data).GrpScripts[0];

        group.Scripts[0].RawBytes.ToArray().Should().Equal(InitScript);
        group.Scripts[1].RawBytes.ToArray().Should().Equal(MainScript);
        group.Scripts[2].RawBytes.ToArray().Should().Equal(TalkScript);
        group.Scripts.Skip(3).Should().OnlyContain(script => script.IsEmpty);
    }

    [Fact]
    public void empty_script_slots_repeat_previous_pointer_and_belong_to_first_slot()
    {
        // File script 3 is used, 1, 2 and 4..31 are empty: the C++ save writes the previous
        // non-empty position for every empty slot, and open() assigns each block to the first
        // slot of the run of identical pointers.
        var fileScripts = new byte[32][];
        fileScripts[0] = [.. InitScript];
        fileScripts[3] = [.. TalkScript];
        var data = Canonical.Build([Canonical.Group("dic", fileScripts)], [Raw("Map")], []);

        var group = Section1File.Open(data).GrpScripts[0];

        group.Scripts[0].RawBytes.ToArray().Should().Equal(InitScript);
        group.Scripts[1].IsEmpty.Should().BeTrue();
        group.Scripts[4].RawBytes.ToArray().Should().Equal(TalkScript);
        group.Scripts.Where((_, index) => index != 0 && index != 4).Should().OnlyContain(script => script.IsEmpty);
    }

    [Fact]
    public void forward_jump_over_return_delays_init_main_split()
    {
        // Script::splitScriptAtReturn ignores a RET inside a pending forward jump.
        byte[] script0 =
        [
            0x10, 0x02,       // JMPF +2 -> offset 3 (target = pos + 1 + 2)
            0x00,             // RET (skipped by the jump)
            0x5F, 0x00,       // NOP, RET  <- split after this RET
            0x5F, 0x00,       // main
        ];
        var data = Canonical.Build([Canonical.Group("dic", [script0])], [], []);

        var group = Section1File.Open(data).GrpScripts[0];

        group.Scripts[0].RawBytes.ToArray().Should().Equal(script0[..5]);
        group.Scripts[1].RawBytes.ToArray().Should().Equal(script0[5..]);
    }

    [Fact]
    public void empty_group_between_groups_is_not_given_the_next_groups_script()
    {
        // Group 1 has no scripts: all of its pointers repeat group 0's last position.
        var data = Canonical.Build(
            [
                Canonical.Group("first", [[.. InitScript]]),
                Canonical.Group("empty", []),
                Canonical.Group("third", [[.. TalkScript]]),
            ],
            [Raw("Map")],
            []);

        var section = Section1File.Open(data);

        section.GrpScripts[0].Scripts[0].RawBytes.ToArray().Should().Equal(InitScript);
        section.GrpScripts[0].Scripts.Skip(1).Should().OnlyContain(script => script.IsEmpty);
        section.GrpScripts[1].Scripts.Should().OnlyContain(script => script.IsEmpty);
        section.GrpScripts[2].Scripts[0].RawBytes.ToArray().Should().Equal(TalkScript);

        section.MarkModified();
        section.Save().Should().Equal(data);
    }

    [Fact]
    public void legacy_point_to_next_layout_does_not_duplicate_scripts_across_groups()
    {
        // Pointer layout used by older fixtures: group 0 slots 1..31 point at group 1's script.
        // Section1File::open ends group 0 at group 1's first pointer, so nothing is duplicated.
        var data = PointToNextLayout();

        var section = Section1File.Open(data);

        section.GrpScripts[0].Scripts[0].RawBytes.ToArray().Should().Equal([0x00]);
        section.GrpScripts[0].Scripts.Skip(1).Should().OnlyContain(script => script.IsEmpty);
        section.GrpScripts[1].Scripts[0].RawBytes.ToArray().Should().Equal([0x5F, 0x00]);
    }

    [Fact]
    public void resized_script_is_written_and_offsets_are_rebuilt()
    {
        var data = Canonical.Build(
            [
                Canonical.Group("dic", [[.. InitScript, .. MainScript]]),
                Canonical.Group("cloud", [[.. InitScript], TalkScript]),
            ],
            [Raw("Map"), Raw("Hello")],
            [Akao(6)]);
        var section = Section1File.Open(data);
        byte[] longer = [0x5F, 0x5F, 0x5F, 0x5F, 0x5F, 0x00];

        section.GrpScripts[1].SetScript(2, new Script(longer));
        section.MarkModified();
        var saved = section.Save();

        var expected = Canonical.Build(
            [
                Canonical.Group("dic", [[.. InitScript, .. MainScript]]),
                Canonical.Group("cloud", [[.. InitScript], longer]),
            ],
            [Raw("Map"), Raw("Hello")],
            [Akao(6)]);
        saved.Should().Equal(expected);
        Section1File.Open(saved).GrpScripts[1].Scripts[2].RawBytes.ToArray().Should().Equal(longer);
    }

    [Fact]
    public void changed_text_realigns_akao_block_to_four_bytes()
    {
        var data = Canonical.Build(
            [Canonical.Group("dic", [[.. InitScript]])],
            [Raw("Map"), Raw("Hi")],
            [Akao(8)]);
        var section = Section1File.Open(data);

        section.SetText(1, FF7String.FromRaw(Raw("Hello")));
        var saved = section.Save();

        var expected = Canonical.Build(
            [Canonical.Group("dic", [[.. InitScript]])],
            [Raw("Map"), Raw("Hello")],
            [Akao(8)]);
        saved.Should().Equal(expected);
        var firstAkao = BinaryPrimitives.ReadUInt32LittleEndian(saved.AsSpan(32 + 8));
        (firstAkao % 4).Should().Be(0);
        saved.AsSpan((int)firstAkao, 4).ToArray().Should().Equal("AKAO"u8.ToArray());
    }

    [Fact]
    public void save_recomputes_model_count_from_init_scripts()
    {
        var data = Canonical.Build(
            [
                Canonical.Group("dic", [[0x43, 0x00, 0x00]]),        // MPNAM -> Director
                Canonical.Group("cloud", [[0xA1, 0x00, 0x00]]),      // CHAR -> Model
                Canonical.Group("tifa", [[0xA0, 0x02, 0xA1, 0x01, 0x00]]), // PC -> Model
            ],
            [Raw("Map")],
            [],
            modelCount: 0);
        var section = Section1File.Open(data);

        section.GrpScripts.Select(group => group.Type).Should().Equal(
            GrpScriptType.Director,
            GrpScriptType.Model,
            GrpScriptType.Model);
        section.MarkModified();

        section.Save()[3].Should().Be(2);
    }

    [Fact]
    public void serialize_parse_serialize_is_stable_after_edits()
    {
        var data = Canonical.Build(
            [
                Canonical.Group("dic", [[.. InitScript, .. MainScript]]),
                Canonical.Group("cloud", [[0xA1, 0x00, 0x00], TalkScript]),
            ],
            [Raw("Map"), Raw("Hello!")],
            [Akao(10), Tuto(), Akao(3)]);
        var section = Section1File.Open(data);
        section.InsertText(2, FF7String.FromRaw(Raw("New")));
        section.GrpScripts[0].SetScript(3, new Script([0x5F, 0x00]));

        var first = section.Save();
        var reopened = Section1File.Open(first);
        reopened.MarkModified();
        var second = reopened.Save();

        second.Should().Equal(first);
        reopened.GrpScripts[0].Scripts[3].RawBytes.ToArray().Should().Equal([0x5F, 0x00]);
        reopened.TutorialsAndSounds!.Entries.Should().HaveCount(3);
    }

    private static byte[] Raw(string ascii) =>
        Encoding.ASCII.GetBytes(ascii).Select(static b => (byte)(b - 0x20)).ToArray();

    private static byte[] Akao(int payloadLength)
    {
        var data = new byte[4 + payloadLength];
        "AKAO"u8.CopyTo(data);
        for (var i = 4; i < data.Length; i++)
            data[i] = (byte)i;
        return data;
    }

    private static byte[] Tuto() => [0x02, 0x03, 0x11];

    private static byte[] PointToNextLayout()
    {
        const int groupCount = 2;
        var scriptDataOffset = 32 + (groupCount * 8) + (groupCount * 64);
        byte[] script0 = [0x00];
        byte[] script1 = [0x5F, 0x00];
        var posTexts = scriptDataOffset + script0.Length + script1.Length;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        writer.Write((ushort)0x0502);
        writer.Write((byte)groupCount);
        writer.Write((byte)0);
        writer.Write((ushort)posTexts);
        writer.Write((ushort)0);
        writer.Write((ushort)512);
        writer.Write(new byte[6]);
        writer.Write(Fixed("makou"));
        writer.Write(Fixed("test"));
        writer.Write(Fixed("dic"));
        writer.Write(Fixed("cloud"));
        var second = (ushort)(scriptDataOffset + script0.Length);
        writer.Write((ushort)scriptDataOffset);
        for (var i = 1; i < 32; i++)
            writer.Write(second);
        writer.Write(second);
        for (var i = 1; i < 32; i++)
            writer.Write((ushort)posTexts);
        writer.Write(script0);
        writer.Write(script1);
        writer.Write((ushort)0);
        return stream.ToArray();
    }

    private static byte[] Fixed(string value)
    {
        var bytes = new byte[8];
        Encoding.Latin1.GetBytes(value).CopyTo(bytes, 0);
        return bytes;
    }

    /// <summary>Builds section 1 bytes exactly as <c>Section1File::save()</c> lays them out.</summary>
    private static class Canonical
    {
        public static (string Name, byte[][] FileScripts) Group(string name, byte[][] scripts)
        {
            var fileScripts = new byte[32][];
            for (var i = 0; i < 32; i++)
                fileScripts[i] = i < scripts.Length && scripts[i] != null ? scripts[i] : [];
            return (name, fileScripts);
        }

        public static byte[] Build(
            IReadOnlyList<(string Name, byte[][] FileScripts)> groups,
            IReadOnlyList<byte[]> texts,
            IReadOnlyList<byte[]> akaos,
            byte modelCount = 0)
        {
            var newPosScripts = 32 + (groups.Count * 72) + (akaos.Count * 4);
            var positions = new List<byte>();
            var allScripts = new List<byte>();
            uint pos32 = (uint)newPosScripts;
            foreach (var (_, fileScripts) in groups)
            {
                foreach (var script in fileScripts)
                {
                    var bytes = script ?? [];
                    if (bytes.Length > 0)
                        pos32 = (uint)(newPosScripts + allScripts.Count);
                    positions.AddRange(BitConverter.GetBytes((ushort)pos32));
                    allScripts.AddRange(bytes);
                }
            }

            var posTexts = newPosScripts + allScripts.Count;
            var textPositions = new List<byte>();
            var allTexts = new List<byte>();
            foreach (var text in texts)
            {
                textPositions.AddRange(BitConverter.GetBytes((ushort)(2 + (texts.Count * 2) + allTexts.Count)));
                allTexts.AddRange(text);
                allTexts.Add(0xFF);
            }

            var size = positions.Count + allScripts.Count + 2 + textPositions.Count + allTexts.Count;
            if (size % 4 != 0 && akaos.Count > 0)
                allTexts.AddRange(new byte[4 - (size % 4)]);

            var posAkaos = (uint)(posTexts + 2 + (texts.Count * 2) + allTexts.Count);
            var akaoPositions = new List<byte>();
            var allAkaos = new List<byte>();
            foreach (var akao in akaos)
            {
                akaoPositions.AddRange(BitConverter.GetBytes((uint)(posAkaos + allAkaos.Count)));
                allAkaos.AddRange(akao);
                if (akao.AsSpan().StartsWith("AKAO"u8) && akao.Length % 4 != 0)
                    allAkaos.AddRange(new byte[4 - (akao.Length % 4)]);
            }

            var output = new List<byte>();
            output.AddRange(BitConverter.GetBytes((ushort)0x0502));
            output.Add((byte)groups.Count);
            output.Add(modelCount);
            output.AddRange(BitConverter.GetBytes((ushort)posTexts));
            output.AddRange(BitConverter.GetBytes((ushort)akaos.Count));
            output.AddRange(BitConverter.GetBytes((ushort)512));
            output.AddRange(new byte[6]);
            output.AddRange(Fixed("makou"));
            output.AddRange(Fixed("md1stin"));
            foreach (var (name, _) in groups)
                output.AddRange(Fixed(name));
            output.AddRange(akaoPositions);
            output.AddRange(positions);
            output.AddRange(allScripts);
            output.AddRange(BitConverter.GetBytes((ushort)texts.Count));
            output.AddRange(textPositions);
            output.AddRange(allTexts);
            output.AddRange(allAkaos);
            return output.ToArray();
        }
    }
}

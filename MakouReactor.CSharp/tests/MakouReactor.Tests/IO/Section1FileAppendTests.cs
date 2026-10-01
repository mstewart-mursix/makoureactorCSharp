using System.Buffers.Binary;
using System.Text;

using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public class Section1FileAppendTests
{
    private static readonly byte[] CloudScript = [0x01, 0x02, 0x03, 0x00, 0x04, 0x05, 0x00];
    private static readonly byte[] BarretScript = [0x0A, 0x0B, 0x00];

    [Fact]
    public void append_group_round_trips_and_leaves_existing_scripts_untouched()
    {
        var data = Build([("cloud", CloudScript), ("barret", BarretScript)], ["Map name", "Hello"], akao: false);
        var section = Section1File.Open(data);
        var newScript = new GrpScript("ai_0001");
        newScript.SetScript(0, new Script([0x00, 0x24, 0x1E, 0x00, 0x00]));

        section.AppendGrpScript(newScript).Should().BeTrue();
        var saved = section.Save();
        var reopened = Section1File.Open(saved);

        reopened.GrpScripts.Select(g => g.Name).Should().Equal("cloud", "barret", "ai_0001");
        reopened.GrpScripts[0].Scripts[0].Compile().Should().Equal(CloudScript);
        reopened.GrpScripts[1].Scripts[0].Compile().Should().Equal(BarretScript);
        reopened.GrpScripts[2].Scripts[0].Compile().Should().Equal(0x00, 0x24, 0x1E, 0x00, 0x00);
        reopened.GrpScripts[2].Scripts.Skip(1).Should().OnlyContain(s => s.IsEmpty);
        reopened.Texts.Select(t => t.Value).Should().Equal("Map name", "Hello");
        reopened.Author.Should().Be(section.Author);
        reopened.MapName.Should().Be(section.MapName);
        saved[2].Should().Be(3, "the header group count is updated");
    }

    [Fact]
    public void append_group_and_new_text_together_keep_everything_consistent()
    {
        var data = Build([("cloud", CloudScript)], ["Map name"], akao: false);
        var section = Section1File.Open(data);
        section.InsertText(1, new FF7String("Brand new line"));
        var group = new GrpScript("ai_0001");
        group.SetScript(0, new Script([0x00, 0x40, 0x00, 0x01, 0x00]));
        section.AppendGrpScript(group);

        var reopened = Section1File.Open(section.Save());

        reopened.Texts.Select(t => t.Value).Should().Equal("Map name", "Brand new line");
        reopened.GrpScripts[1].Scripts[0].Compile().Should().Equal(0x00, 0x40, 0x00, 0x01, 0x00);
        reopened.GrpScripts[0].Scripts[0].Compile().Should().Equal(CloudScript);
    }

    [Fact]
    public void append_group_shifts_akao_offsets_to_the_moved_block()
    {
        var data = Build([("cloud", CloudScript)], ["Map name", "Hello"], akao: true);
        var section = Section1File.Open(data);
        var group = new GrpScript("ai_0001");
        group.SetScript(0, new Script([0x00, 0x24, 0x1E, 0x00, 0x00]));
        section.AppendGrpScript(group);

        var saved = section.Save();

        var reopened = Section1File.Open(saved);
        reopened.AkaoCount.Should().Be(1);
        // AKAO table sits after the (now two) group names.
        var akaoOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(saved.AsSpan(32 + (2 * 8), 4));
        saved.AsSpan(akaoOffset, 4).ToArray().Should().Equal([0x41, 0x4B, 0x41, 0x4F]);
        akaoOffset.Should().Be(saved.Length - 4, "the AKAO block stays at the end");
        akaoOffset.Should().BeGreaterThan((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(32 + 8, 4)));
    }

    [Fact]
    public void append_multiple_groups_in_one_save()
    {
        var data = Build([("cloud", CloudScript)], ["Map name"], akao: false);
        var section = Section1File.Open(data);
        foreach (var (name, script) in new[] { ("ai_0001", new byte[] { 0x00, 0x24, 0x01, 0x00, 0x00 }), ("ai_0002", new byte[] { 0x00, 0x00 }) })
        {
            var group = new GrpScript(name);
            group.SetScript(0, new Script(script));
            section.AppendGrpScript(group);
        }

        var reopened = Section1File.Open(section.Save());

        reopened.GrpScripts.Select(g => g.Name).Should().Equal("cloud", "ai_0001", "ai_0002");
        reopened.GrpScripts[1].Scripts[0].Compile().Should().Equal(0x00, 0x24, 0x01, 0x00, 0x00);
        reopened.GrpScripts[2].Scripts[0].Compile().Should().Equal(0x00, 0x00);
    }

    [Fact]
    public void saved_section_is_stable_when_saved_again_after_reload()
    {
        var data = Build([("cloud", CloudScript)], ["Map name", "Hello"], akao: true);
        var section = Section1File.Open(data);
        var group = new GrpScript("ai_0001");
        group.SetScript(0, new Script([0x00, 0x24, 0x1E, 0x00, 0x00]));
        section.AppendGrpScript(group);
        var first = section.Save();

        var second = Section1File.Open(first).Save();

        second.Should().Equal(first);
    }

    [Fact]
    public void group_names_are_truncated_to_eight_characters()
    {
        var section = Section1File.Open(Build([("cloud", CloudScript)], ["Map name"], akao: false));
        var group = new GrpScript("LLM_Generated_20261001");
        group.SetScript(0, new Script([0x00, 0x00]));
        section.AppendGrpScript(group);

        Section1File.Open(section.Save()).GrpScripts[1].Name.Should().Be("LLM_Gene");
    }

    [Fact]
    public void edits_inside_loaded_groups_still_apply_alongside_an_appended_group()
    {
        var data = Build([("cloud", CloudScript)], ["Map name"], akao: false);
        var section = Section1File.Open(data);
        section.GrpScripts[0].Scripts[0].ReplaceRawOpcodeBytes(0, [0x01, 0xAA, 0xBB]).Should().BeTrue();
        var group = new GrpScript("ai_0001");
        group.SetScript(0, new Script([0x00, 0x00]));
        section.AppendGrpScript(group);

        var reopened = Section1File.Open(section.Save());

        reopened.GrpScripts[0].Scripts[0].Compile().Take(3).Should().Equal(0x01, 0xAA, 0xBB);
    }

    [Fact]
    public void removing_a_loaded_group_is_refused_instead_of_silently_corrupting()
    {
        var section = Section1File.Open(Build([("cloud", CloudScript), ("barret", BarretScript)], ["Map name"], akao: false));
        section.RemoveGrpScript(0);

        var act = () => section.Save();

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void a_script_in_a_slot_the_field_does_not_have_is_refused()
    {
        var section = Section1File.Open(Build([("cloud", CloudScript)], ["Map name"], akao: false));
        var group = new GrpScript("ai_0001");
        group.SetScript(32, new Script([0x00]));
        section.AppendGrpScript(group);

        var act = () => section.Save();

        act.Should().Throw<InvalidDataException>().WithMessage("*slot 32*");
    }

    [Fact]
    public void appending_beyond_the_group_limit_is_refused()
    {
        var section = Section1File.Open(Build([("cloud", CloudScript)], ["Map name"], akao: false));
        while (section.GrpScripts.Count < Section1File.MaxGrpScriptCount)
            section.AppendGrpScript(new GrpScript("g"));

        section.AppendGrpScript(new GrpScript("one_more")).Should().BeFalse();
    }

    [Fact]
    public void pc_field_save_with_appended_group_survives_a_full_field_round_trip()
    {
        var field = FieldPC.CreateEmpty("md1stin");
        var group = new GrpScript("ai_0001");
        group.SetScript(0, new Script([0x00, 0x24, 0x1E, 0x00, 0x00]));
        field.ScriptsAndTexts!.AppendGrpScript(group);
        field.SetModified();

        var reopened = FieldPC.OpenCompressed("md1stin", field.SaveCompressed());

        reopened.ScriptsAndTexts!.GrpScripts.Select(g => g.Name).Should().Contain("ai_0001");
        reopened.ScriptsAndTexts.GrpScripts.Last().Scripts[0].Compile().Should().Equal(0x00, 0x24, 0x1E, 0x00, 0x00);
    }

    // -----------------------------------------------------------------------
    // Builder
    // -----------------------------------------------------------------------

    /// <summary>Builds a Section 1 the way the Qt writer lays it out (empty slots point at the next script / the end).</summary>
    private static byte[] Build(IReadOnlyList<(string Name, byte[] Script)> groups, IReadOnlyList<string> texts, bool akao)
    {
        const int scriptCount = 32;
        const int headerSize = 32;
        var akaoCount = akao ? 1 : 0;
        var scriptsStart = headerSize + (groups.Count * 8) + (akaoCount * 4) + (groups.Count * scriptCount * 2);
        var scriptsLength = groups.Sum(g => g.Script.Length);
        var textStart = scriptsStart + scriptsLength;
        var textSection = TextSection(texts);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)0x0502);
        writer.Write((byte)groups.Count);
        writer.Write((byte)1);
        writer.Write((ushort)textStart);
        writer.Write((ushort)akaoCount);
        writer.Write((ushort)512);
        writer.Write(new byte[6]);
        Fixed(writer, "makou", 8);
        Fixed(writer, "md1stin", 8);
        foreach (var (name, _) in groups)
            Fixed(writer, name, 8);
        if (akao)
            writer.Write((uint)(textStart + textSection.Length));

        var cursor = scriptsStart;
        foreach (var (_, script) in groups)
        {
            var end = cursor + script.Length;
            writer.Write((ushort)cursor);
            for (var slot = 1; slot < scriptCount; slot++)
                writer.Write((ushort)end);
            cursor = end;
        }

        foreach (var (_, script) in groups)
            writer.Write(script);

        writer.Write(textSection);
        if (akao)
            writer.Write(new byte[] { 0x41, 0x4B, 0x41, 0x4F });

        return stream.ToArray();
    }

    private static byte[] TextSection(IReadOnlyList<string> texts)
    {
        var encoded = texts.Select(t => Encoding.Latin1.GetBytes(t).Concat(new byte[] { 0xFF }).ToArray()).ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)texts.Count);
        var offset = 2 + (texts.Count * 2);
        foreach (var text in encoded)
        {
            writer.Write((ushort)offset);
            offset += text.Length;
        }

        foreach (var text in encoded)
            writer.Write(text);
        return stream.ToArray();
    }

    private static void Fixed(BinaryWriter writer, string value, int length)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        writer.Write(bytes.AsSpan(0, Math.Min(bytes.Length, length)));
        writer.Write(new byte[length - Math.Min(bytes.Length, length)]);
    }
}

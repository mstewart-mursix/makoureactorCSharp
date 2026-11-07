using System.Text;

using FluentAssertions;

using MakouReactor.AI.Mapping;
using MakouReactor.Core.Models;
using MakouReactor.Core.Text;

using Xunit;

namespace MakouReactor.Tests.Mapping;

/// <summary>
/// Phase 1e: the mapper emits real opcodes into a synthetic in-memory field (no game data).
/// </summary>
public sealed class ScenePlanMapperEmissionTests
{
    private const ushort TifaTrack = 35; // GameTables "tifa"
    private const byte FlagBankHigh = 8 << 4; // BANK(8, 0)

    [Fact]
    public void apply_emits_every_step_type_as_decodable_opcodes()
    {
        var field = BuildField();
        var result = ScenePlanMapper.ApplyToField(
            BuildFullPlan(), field, new ApplyOptions { PreviewOnly = false, GroupNameOverride = "LLM_Test_Long" });

        result.Ok.Should().BeTrue(result.Error);
        result.PreviewOnly.Should().BeFalse();
        result.GroupName.Should().Be("LLM_Test");
        field.IsModified.Should().BeTrue();

        var section = field.ScriptsAndTexts!;
        section.GrpScripts.Select(g => g.Name).Should().Equal("dic", "LLM_Test", "guard", "dic2", "merchant");
        result.Groups.Select(g => (g.GroupIndex, g.Name, g.Role)).Should().Equal(
            (1, "LLM_Test", "director"), (2, "guard", "guard"), (3, "dic2", "dic"), (4, "merchant", "merchant_long_name"));

        // Texts: dialog line first, then say texts in emission order (deduplicated).
        section.Texts.Select(t => t.Value).Should().Equal(
            "Map name", "Hello world!", "Welcome to the shop.", "Halt!", "Take these.", "Go away.");
        result.TextsAdded.Select(t => t.TextId).Should().Equal(2, 3, 4, 5);

        AssertDirector(section.GrpScripts[1]);
        AssertGuard(section.GrpScripts[2]);
        AssertDicActor(section.GrpScripts[3]);
        AssertMerchant(section.GrpScripts[4]);

        result.OpcodeCounts["MESSAGE"].Should().Be(4);
        result.OpcodeCounts["REQEW"].Should().Be(3);
        result.Notes.Should().ContainSingle().Which.Should().Contain("guard remembers the player");
        result.Warnings.Should().Contain(w => w.Contains("'potion'") && w.Contains("STITM not emitted"));
        result.Warnings.Should().Contain(w => w.Contains("off the walkmesh"));
        result.Warnings.Should().Contain(w => w.Contains("speed") && w.Contains("ignored"));
        result.Warnings.Should().Contain(w => w.Contains("Model Loader"));
        result.Summary.Should().Contain("Group 1 \"LLM_Test\" (director)")
            .And.Contain("S1 - Talk")
            .And.Contain("Added 4 dialogue text entries starting at text 2.")
            .And.Contain("Opcode counts:")
            .And.Contain("Warnings:");
    }

    [Fact]
    public void applied_groups_scripts_and_texts_survive_save_and_reopen()
    {
        var field = BuildField();
        var result = ScenePlanMapper.ApplyToField(
            BuildFullPlan(), field, new ApplyOptions { PreviewOnly = false, GroupNameOverride = "LLM_Test" });
        result.Ok.Should().BeTrue(result.Error);

        var reopened = FieldPC.OpenDecompressed("llmtest", field.SaveDecompressed());

        var section = reopened.ScriptsAndTexts!;
        section.GrpScripts.Select(g => g.Name).Should().Equal("dic", "LLM_Test", "guard", "dic2", "merchant");
        section.Texts.Select(t => t.Value).Should().Equal(field.ScriptsAndTexts!.Texts.Select(t => t.Value));
        foreach (var group in result.Groups)
        {
            var loaded = section.GrpScripts[group.GroupIndex];
            foreach (var script in group.Scripts)
                loaded.Scripts[script.Slot].RawBytes.ToArray().Should().Equal(script.Bytes, $"{group.Name} {script.Label}");

            loaded.Scripts.Select((s, slot) => (s, slot))
                .Where(x => !x.s.IsEmpty)
                .Select(x => x.slot)
                .Should().Equal(group.Scripts.Select(s => s.Slot), group.Name);
        }

        section.GrpScripts.Skip(2).Should().OnlyContain(g => g.Type == GrpScriptType.Model);
        section.TutorialsAndSounds!.Entries.Should().ContainSingle(e => e.MusicId == TifaTrack);

        // A second save of the reopened field is stable.
        reopened.ScriptsAndTexts!.MarkModified();
        reopened.SetModified();
        FieldPC.OpenDecompressed("llmtest", reopened.SaveDecompressed()).ScriptsAndTexts!.Save()
            .Should().Equal(section.Save());
    }

    [Fact]
    public void preview_returns_the_full_result_and_leaves_the_field_byte_identical()
    {
        var field = BuildField();
        var before = field.SaveDecompressed();
        var sectionBefore = field.ScriptsAndTexts!.Save();

        var preview = ScenePlanMapper.ApplyToField(
            BuildFullPlan(), field, new ApplyOptions { PreviewOnly = true, GroupNameOverride = "LLM_Test" });

        preview.Ok.Should().BeTrue(preview.Error);
        preview.PreviewOnly.Should().BeTrue();
        field.IsModified.Should().BeFalse();
        field.ScriptsAndTexts!.IsModified.Should().BeFalse();
        field.ScriptsAndTexts.GrpScripts.Should().HaveCount(1);
        field.ScriptsAndTexts.Texts.Should().HaveCount(2);
        field.SaveDecompressed().Should().Equal(before);
        field.ScriptsAndTexts.Save().Should().Equal(sectionBefore);
        preview.Summary.Should().Contain("preview only, the field was not modified");

        var applied = ScenePlanMapper.ApplyToField(
            BuildFullPlan(), field, new ApplyOptions { PreviewOnly = false, GroupNameOverride = "LLM_Test" });
        preview.Groups.Select(g => g.Name).Should().Equal(applied.Groups.Select(g => g.Name));
        preview.Groups.SelectMany(g => g.Scripts).Select(s => s.Bytes)
            .Should().BeEquivalentTo(applied.Groups.SelectMany(g => g.Scripts).Select(s => s.Bytes), o => o.WithStrictOrdering());
        preview.TextsAdded.Should().Equal(applied.TextsAdded);
        preview.OpcodeCounts.Should().Equal(applied.OpcodeCounts);
        preview.Warnings.Should().Equal(applied.Warnings);
    }

    [Fact]
    public void empty_field_without_music_or_walkmesh_warns_instead_of_guessing()
    {
        var field = FieldPC.CreateEmpty("llmtest");
        var plan = new ScenePlan
        {
            Actors = { new Actor("guard", "Guard", x: 12, y: 34) },
            Events =
            {
                new EventDef("intro", "on_enter", steps:
                [
                    new EventStep { Type = EventStepType.PlayMusic, Track = "tifa" },
                    new EventStep { Type = EventStepType.PlayMusic, Track = "not a track" },
                    new EventStep { Type = EventStepType.Battle, EncounterId = "boss" },
                    new EventStep { Type = EventStepType.GiveItem, ItemId = "item:999", Qty = 1 },
                    new EventStep { Type = EventStepType.Move, ActorId = "ghost", To = new Point(1, 2) },
                ]),
                new EventDef("zone", "zone", new Rectangle(10, 20, 100, 40), steps: [new EventStep { Type = EventStepType.Wait, Ms = 500 }]),
            },
        };

        var result = ScenePlanMapper.ApplyToField(plan, field, new ApplyOptions { PreviewOnly = false });

        result.Ok.Should().BeTrue(result.Error);
        result.GroupName.Should().Be("LLMScene");
        result.OpcodeCounts.Should().NotContainKey("MUSIC").And.NotContainKey("BATTLE").And.NotContainKey("STITM").And.NotContainKey("MOVE");
        result.Warnings.Should().Contain(w => w.Contains("not in this field's music list"));
        result.Warnings.Should().Contain(w => w.Contains("unknown music track"));
        result.Warnings.Should().Contain(w => w.Contains("'boss'") && w.Contains("BATTLE not emitted"));
        result.Warnings.Should().Contain(w => w.Contains("'item:999'"));
        result.Warnings.Should().Contain(w => w.Contains("'ghost' is not a plan actor"));
        result.Warnings.Should().Contain(w => w.Contains("trigger line uses the zone's coordinates unchanged"));
        result.Warnings.Should().Contain(w => w.Contains("no walkmesh"));

        var guardInit = RawOpcodeReader.Read(field.ScriptsAndTexts!.GrpScripts[2].Scripts[0].RawBytes.Span);
        guardInit.Select(op => op.Name).Should().Equal("CHAR", "XYZI", "RET");
        guardInit[1].Bytes.Should().Equal(0xA5, 0, 0, 12, 0, 34, 0, 0, 0, 0, 0);

        var main = RawOpcodeReader.Read(field.ScriptsAndTexts.GrpScripts[1].Scripts[1].RawBytes.Span);
        main.Select(op => op.Name).Should().Equal("IFUB", "BITON", "RET");

        // The zone event is its own Location group: LINE across the zone's middle (wider than tall,
        // so horizontal at y = 20 + 40/2), and the event on "S5 - Go 1x" (33-slot index 6).
        var zone = field.ScriptsAndTexts.GrpScripts[3];
        zone.Type.Should().Be(GrpScriptType.Location);
        var zoneInit = RawOpcodeReader.Read(zone.Scripts[0].RawBytes.Span);
        zoneInit.Select(op => op.Name).Should().Equal("LINE", "RET");
        zoneInit[0].Bytes.Should().Equal(0xD0, 10, 0, 40, 0, 0, 0, 110, 0, 40, 0, 0, 0);
        var goOnce = RawOpcodeReader.Read(zone.Scripts[SceneScriptEmitterSlots.GoOnce].RawBytes.Span);
        goOnce.Select(op => op.Name).Should().Equal("WAIT", "RET");
        goOnce[0].Bytes.Should().Equal(0x24, 15, 0); // 500 ms = 15 frames at 30 fps
        result.Groups.Should().Contain(g => g.Name == zone.Name && g.Scripts.Any(sc => sc.Label == "S5 - Go 1x"));
    }

    /// <summary>Mirrors <c>SceneScriptEmitter.GoOnceSlot</c> (internal to the AI assembly).</summary>
    private static class SceneScriptEmitterSlots
    {
        public const int GoOnce = 6;
    }

    [Fact]
    public void long_say_text_is_wrapped_at_word_boundaries_and_paged()
    {
        var field = FieldPC.CreateEmpty("llmtest");
        var words = string.Join(" ", Enumerable.Range(1, 60).Select(i => $"word{i}"));
        var plan = new ScenePlan
        {
            Events = { new EventDef("intro", "on_enter", steps: [new EventStep { Type = EventStepType.Say, ActorId = "narrator", Text = words }]) },
        };

        var result = ScenePlanMapper.ApplyToField(plan, field, new ApplyOptions { PreviewOnly = false });

        result.Ok.Should().BeTrue(result.Error);
        var text = field.ScriptsAndTexts!.Texts[2];
        var raw = text.RawBytes!;
        raw.Should().Contain(0xE7).And.Contain(0xE8);
        var encodedWords = FF7TextCodec.Encode(words, includeTerminator: false);
        raw.Where(b => b is not (0xE7 or 0xE8 or 0xFF or 0x00))
            .Should().Equal(encodedWords.Where(b => b != 0x00), "only spaces (0x00) are replaced by breaks");

        var lines = SplitLines(raw);
        lines.Should().HaveCountGreaterThan(4);
        lines.Select(line => FF7TextMetrics.CalcUncappedSize(line, FF7TextMetrics.DefaultMarginRight).Width + 3)
            .Should().OnlyContain(width => width <= 304);

        var window = RawOpcodeReader.Read(field.ScriptsAndTexts.GrpScripts[1].Scripts[1].RawBytes.Span)
            .Single(op => op.Name == "WINDOW");
        var (width, height) = FF7TextMetrics.CalcWindowSize(raw);
        window.Bytes[6..10].Should().Equal((byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8));
        width.Should().BeLessThanOrEqualTo(304);
    }

    [Fact]
    public void group_names_are_truncated_and_unique()
    {
        var field = FieldPC.CreateEmpty("llmtest");
        var plan = new ScenePlan
        {
            Actors =
            {
                new Actor("merchant_a", "A"),
                new Actor("merchant_b", "B"),
                new Actor("LLMScene", "C"),
                new Actor("!!!", "D"),
            },
        };

        var result = ScenePlanMapper.ApplyToField(plan, field, new ApplyOptions { PreviewOnly = false });

        result.Ok.Should().BeTrue(result.Error);
        var names = field.ScriptsAndTexts!.GrpScripts.Select(g => g.Name).ToList();
        names.Should().Equal("dic", "LLMScene", "merchant", "merchan2", "LLMScen2", "actor");
        names.Should().OnlyContain(n => n.Length <= 8).And.OnlyHaveUniqueItems();
    }

    private static void AssertDirector(GrpScript group)
    {
        RawOpcodeReader.Read(group.Scripts[0].RawBytes.Span).Select(op => op.Name).Should().Equal("RET");

        var main = RawOpcodeReader.Read(group.Scripts[1].RawBytes.Span);
        main.Select(op => op.Name).Should().Equal(
            "IFUB", "BITON", "MUSIC", "WINDOW", "MESSAGE", "REQEW", "REQEW", "WAIT", "BITON", "BATTLE", "RET");

        // Run-once guard: flag "met" was allocated first (0xFF bit 0), the guard bit second.
        main[0].Bytes[..5].Should().Equal(0x14, FlagBankHigh, 0xFF, 1, 10); // bit 1 OFF
        (main[0].Offset + 5 + main[0].Bytes[5]).Should().Be(main[^1].Offset);
        main[1].Bytes.Should().Equal(0x82, FlagBankHigh, 0xFF, 1);
        main[2].Bytes.Should().Equal(0xF0, 0); // AKAO entry 0 holds track 35
        main[4].Bytes.Should().Equal(0x40, 0, 2);
        AssertWindowFits(main[3], "Welcome to the shop.");
        main[5].Bytes.Should().Equal(0x03, 2, 3 | (1 << 5)); // guard, file script 3 (move)
        main[6].Bytes.Should().Equal(0x03, 2, 4 | (1 << 5)); // guard, file script 4 (face)
        main[7].Bytes.Should().Equal(0x24, 30, 0); // 1000 ms
        main[8].Bytes.Should().Equal(0x82, FlagBankHigh, 0xFF, 0); // set_flag met = true
        main[9].Bytes.Should().Equal(0x70, 0, 42, 0);
    }

    private static void AssertGuard(GrpScript group)
    {
        var init = RawOpcodeReader.Read(group.Scripts[0].RawBytes.Span);
        init.Select(op => op.Name).Should().Equal("CHAR", "XYZI", "DIR", "RET");
        init[0].Bytes.Should().Equal(0xA1, 0);
        // (-50, 50) lies in triangle 1, whose plane gives Z = 20 there.
        init[1].Bytes.Should().Equal(0xA5, 0, 0, 0xCE, 0xFF, 50, 0, 20, 0, 1, 0);
        init[2].Bytes.Should().Equal(0xB3, 0, 0); // facing S

        RawOpcodeReader.Read(group.Scripts[1].RawBytes.Span).Select(op => op.Name).Should().Equal("RET");

        var talk = RawOpcodeReader.Read(group.Scripts[2].RawBytes.Span);
        talk.Select(op => op.Name).Should().Equal(
            "WINDOW", "MESSAGE", "IFUB", "STITM", "WINDOW", "MESSAGE", "JMPF", "WINDOW", "MESSAGE", "MOVE", "REQEW", "RET");
        talk[1].Bytes.Should().Equal(0x40, 0, 3);
        talk[2].Bytes[..5].Should().Equal(0x14, FlagBankHigh, 0xFF, 0, 9); // if met (bit 0 ON)
        (talk[2].Offset + 5 + talk[2].Bytes[5]).Should().Be(talk[7].Offset, "else branch");
        talk[3].Bytes.Should().Equal(0x58, 0, 12, 0, 3);
        talk[5].Bytes.Should().Equal(0x40, 0, 4);
        (talk[6].Offset + 1 + talk[6].Bytes[1]).Should().Be(talk[9].Offset, "end of if");
        talk[8].Bytes.Should().Equal(0x40, 0, 5);
        talk[9].Bytes.Should().Equal(0xA8, 0, 0xEC, 0xFF, 40, 0); // own move inline
        talk[10].Bytes.Should().Equal(0x03, 4, 3 | (1 << 5)); // merchant, file script 3 (face)

        RawOpcodeReader.Read(group.Scripts[4].RawBytes.Span).Select(op => op.RawBytesHex)
            .Should().Equal("A8 00 0A 00 0A 00", "00");
        RawOpcodeReader.Read(group.Scripts[5].RawBytes.Span).Select(op => op.RawBytesHex)
            .Should().Equal("B3 00 80", "00"); // N = 128
    }

    private static void AssertDicActor(GrpScript group)
    {
        var init = RawOpcodeReader.Read(group.Scripts[0].RawBytes.Span);
        init.Select(op => op.Name).Should().Equal("CHAR", "XYZI", "RET");
        init[0].Bytes.Should().Equal(0xA1, 1);
        // (500, 0) is snapped to (100, 0) on triangle 0 (flat, Z = 0).
        init[1].Bytes.Should().Equal(0xA5, 0, 0, 100, 0, 0, 0, 0, 0, 0, 0);
        group.Scripts[2].IsEmpty.Should().BeTrue();
    }

    private static void AssertMerchant(GrpScript group)
    {
        var init = RawOpcodeReader.Read(group.Scripts[0].RawBytes.Span);
        init.Select(op => op.Name).Should().Equal("CHAR", "XYZI", "RET");
        init[0].Bytes.Should().Equal(0xA1, 2);
        RawOpcodeReader.Read(group.Scripts[4].RawBytes.Span).Select(op => op.RawBytesHex)
            .Should().Equal("B3 00 C0", "00"); // E = 192
    }

    private static void AssertWindowFits(RawOpcode window, string text)
    {
        var (width, height) = FF7TextMetrics.CalcWindowSize(FF7TextCodec.Encode(text));
        window.Bytes[1].Should().Be(0);
        BitConverter.ToUInt16(window.Bytes, 6).Should().Be((ushort)width);
        BitConverter.ToUInt16(window.Bytes, 8).Should().Be((ushort)height);
        BitConverter.ToInt16(window.Bytes, 2).Should().Be((short)((320 - width) / 2));
        BitConverter.ToInt16(window.Bytes, 4).Should().Be(8);
    }

    private static ScenePlan BuildFullPlan() => new()
    {
        Meta = new ScenePlanMeta { Title = "Shop", Model = "test", Version = "1.0" },
        Actors =
        {
            new Actor("guard", "Guard", x: -50, y: 50, facing: 'S'),
            new Actor("dic", "Clashing name", x: 500, y: 0),
            new Actor("merchant_long_name", "Merchant", x: 50, y: -50),
        },
        Dialog = { new DialogLine("narrator", "Welcome to the shop.") },
        Events =
        {
            new EventDef("intro", "on_enter", steps:
            [
                new EventStep { Type = EventStepType.PlayMusic, Track = "tifa" },
                new EventStep { Type = EventStepType.Say, ActorId = "narrator", Text = "Welcome to the shop." },
                new EventStep { Type = EventStepType.Move, ActorId = "guard", To = new Point(10, 10), Speed = 2 },
                new EventStep { Type = EventStepType.Face, ActorId = "guard", Dir = 'N' },
                new EventStep { Type = EventStepType.Wait, Ms = 1000 },
                new EventStep { Type = EventStepType.SetFlag, Key = "met", Value = true },
                new EventStep { Type = EventStepType.Battle, EncounterId = "battle:42" },
            ]),
            new EventDef("talk", "on_interact", steps:
            [
                new EventStep { Type = EventStepType.Say, ActorId = "guard", Text = "Halt!" },
                new EventStep
                {
                    Type = EventStepType.IfFlag,
                    Key = "met",
                    ThenSteps =
                    {
                        new EventStep { Type = EventStepType.GiveItem, ItemId = "item:12", Qty = 3 },
                        new EventStep { Type = EventStepType.Say, ActorId = "guard", Text = "Take these." },
                    },
                    ElseSteps =
                    {
                        new EventStep { Type = EventStepType.Say, ActorId = "guard", Text = "Go away." },
                        new EventStep { Type = EventStepType.CustomNote, Text = "guard remembers the player" },
                    },
                },
                new EventStep { Type = EventStepType.GiveItem, ItemId = "potion", Qty = 1 },
                new EventStep { Type = EventStepType.Move, ActorId = "guard", To = new Point(-20, 40) },
                new EventStep { Type = EventStepType.Face, ActorId = "merchant_long_name", Dir = 'E' },
            ]),
        },
    };

    private static List<byte[]> SplitLines(byte[] raw)
    {
        var lines = new List<byte[]>();
        var current = new List<byte>();
        foreach (var b in raw)
        {
            if (b is 0xE7 or 0xE8 or 0xFF)
            {
                lines.Add([.. current]);
                current.Clear();
                if (b == 0xFF)
                    break;
                continue;
            }

            current.Add(b);
        }

        return lines;
    }

    /// <summary>
    /// <see cref="FieldPC.CreateEmpty"/> with section 1 replaced by one that also lists an AKAO
    /// block for track 35 and a two-triangle walkmesh over (-100,-100)-(100,100).
    /// </summary>
    private static FieldPC BuildField()
    {
        var empty = FieldPC.CreateEmpty("llmtest");
        var sections = empty.Sections
            .OrderBy(s => s.Index)
            .Select(s => s.Section switch
            {
                FieldSection.Scripts => BuildSection1WithMusic(TifaTrack),
                FieldSection.Walkmesh => BuildWalkmesh(),
                _ => empty.GetSectionData(s.Section),
            })
            .ToArray();
        return FieldPC.OpenDecompressed("llmtest", BuildPcField(sections));
    }

    private static byte[] BuildWalkmesh()
    {
        var walkmesh = new IdFile();
        walkmesh.InsertTriangle(0, new Triangle(
            new VertexSR(-100, -100, 0, 0), new VertexSR(100, -100, 0, 0), new VertexSR(100, 100, 0, 0)));
        walkmesh.InsertTriangle(1, new Triangle(
            new VertexSR(-100, -100, 0, 0), new VertexSR(100, 100, 0, 0), new VertexSR(-100, 100, 40, 0)));
        return walkmesh.Save();
    }

    private static byte[] BuildSection1WithMusic(ushort musicId)
    {
        var texts = BuildTextSection(["Map name", "Hello world!"]);
        const int groupCount = 1;
        const int akaoCount = 1;
        var akaoTable = 32 + (groupCount * 8);
        var scriptOffsets = akaoTable + (akaoCount * 4);
        var scriptStart = scriptOffsets + (groupCount * 32 * 2);
        var textOffset = scriptStart + 1;
        var afterTexts = textOffset + texts.Length;
        var padding = (4 - (afterTexts % 4)) % 4;
        var akaoOffset = afterTexts + padding;
        byte[] akao = [.. "AKAO"u8, (byte)musicId, (byte)(musicId >> 8), 16, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        writer.Write((ushort)0x0502);
        writer.Write((byte)groupCount);
        writer.Write((byte)0);
        writer.Write((ushort)textOffset);
        writer.Write((ushort)akaoCount);
        writer.Write((ushort)512);
        writer.Write(new byte[6]);
        WriteFixed(writer, "makou", 8);
        WriteFixed(writer, "llmtest", 8);
        WriteFixed(writer, "dic", 8);
        writer.Write((uint)akaoOffset);
        for (var i = 0; i < 32; i++)
            writer.Write((ushort)(i == 0 ? scriptStart : textOffset));
        writer.Write((byte)0x00); // RET
        writer.Write(texts);
        writer.Write(new byte[padding]);
        writer.Write(akao);
        return stream.ToArray();
    }

    private static byte[] BuildTextSection(IReadOnlyList<string> texts)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)texts.Count);
        var offset = checked((ushort)(2 + (texts.Count * 2)));
        foreach (var text in texts)
        {
            writer.Write(offset);
            offset += checked((ushort)FF7TextCodec.Encode(text).Length);
        }

        foreach (var text in texts)
            writer.Write(FF7TextCodec.Encode(text));

        return stream.ToArray();
    }

    private static byte[] BuildPcField(IReadOnlyList<byte[]> sections)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write((ushort)0);
        writer.Write((uint)sections.Count);
        for (var i = 0; i < sections.Count; i++)
            writer.Write(0u);

        var offsets = new uint[sections.Count];
        for (var i = 0; i < sections.Count; i++)
        {
            offsets[i] = checked((uint)stream.Position);
            writer.Write((uint)sections[i].Length);
            writer.Write(sections[i]);
        }

        writer.Write("FINAL FANTASY7"u8);
        stream.Position = 6;
        foreach (var offset in offsets)
            writer.Write(offset);

        return stream.ToArray();
    }

    private static void WriteFixed(BinaryWriter writer, string value, int length)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        writer.Write(bytes.AsSpan(0, Math.Min(bytes.Length, length)));
        if (bytes.Length < length)
            writer.Write(new byte[length - bytes.Length]);
    }
}

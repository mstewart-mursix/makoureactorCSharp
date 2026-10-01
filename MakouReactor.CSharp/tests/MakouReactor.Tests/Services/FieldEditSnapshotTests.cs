using System.Text;

using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.Core.Services;

using Xunit;

namespace MakouReactor.Tests.Services;

public sealed class FieldEditSnapshotTests
{
    [Fact]
    public void restore_returns_field_to_captured_text_state()
    {
        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField(BuildSection1("Before")));
        var snapshot = FieldEditSnapshot.Capture(field);

        field.SetText(0, new FF7String("After"));

        var restored = snapshot.Restore();

        restored.ScriptsAndTexts!.Texts[0].Value.Should().Be("Before");
        restored.IsModified.Should().BeFalse();
    }

    [Fact]
    public void restore_preserves_prior_dirty_state()
    {
        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField(BuildSection1("Before")));
        field.SetText(0, new FF7String("Unsaved before LLM"));
        var snapshot = FieldEditSnapshot.Capture(field);

        field.SetText(0, new FF7String("LLM change"));

        var restored = snapshot.Restore();

        restored.ScriptsAndTexts!.Texts[0].Value.Should().Be("Unsaved before LLM");
        restored.IsModified.Should().BeTrue();
    }

    [Fact]
    public void restore_undoes_a_walkmesh_apply()
    {
        var field = FieldPC.CreateEmpty("md1stin");
        var snapshot = FieldEditSnapshot.Capture(field);
        var plan = new MakouReactor.Core.Models.ScenePlan();
        plan.Layout.Walkmesh = new WalkmeshPlan
        {
            Regions =
            {
                new WalkmeshRegion
                {
                    Id = "r",
                    Polygon = [new Point(0, 0), new Point(100, 0), new Point(100, 100), new Point(0, 100)],
                },
            },
        };

        MakouReactor.AI.Mapping.ScenePlanMapper.ApplyToField(plan, field,
            new ApplyOptions { PreviewOnly = false, WalkmeshMode = WalkmeshMode.Replace });
        field.Walkmesh!.TriangleCount.Should().Be(2);

        var restored = snapshot.Restore();

        restored.Walkmesh!.TriangleCount.Should().Be(0);
        restored.IsModified.Should().BeFalse();
    }

    private static byte[] BuildPcField(byte[] section1)
    {
        var sections = new byte[9][];
        sections[0] = section1;
        for (var index = 1; index < sections.Length; index++)
            sections[index] = [0x00];

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write((ushort)0);
        writer.Write(9u);
        for (var i = 0; i < 9; i++)
            writer.Write(0u);

        var offsets = new uint[9];
        for (var i = 0; i < 9; i++)
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

    private static byte[] BuildSection1(string text)
    {
        const int posText = 32;
        var texts = BuildTextSection([text]);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)0x0502);
        writer.Write((byte)0);
        writer.Write((byte)1);
        writer.Write((ushort)posText);
        writer.Write((ushort)0);
        writer.Write((ushort)512);
        writer.Write(new byte[6]);
        WriteFixed(writer, "makou", 8);
        WriteFixed(writer, "md1stin", 8);
        writer.Write(texts);

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
            offset += checked((ushort)(Encoding.ASCII.GetByteCount(text) + 1));
        }

        foreach (var text in texts)
        {
            writer.Write(Encoding.ASCII.GetBytes(text));
            writer.Write((byte)0xFF);
        }

        return stream.ToArray();
    }

    private static void WriteFixed(BinaryWriter writer, string value, int length)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        writer.Write(bytes);
        writer.Write(new byte[length - bytes.Length]);
    }
}

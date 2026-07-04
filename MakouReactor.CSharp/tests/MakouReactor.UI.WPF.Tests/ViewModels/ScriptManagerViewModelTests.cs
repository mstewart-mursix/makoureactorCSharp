using System.Buffers.Binary;
using System.IO;
using System.Text;

using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.ViewModels;

public sealed class ScriptManagerViewModelTests
{
    [Fact]
    public void dirty_state_tracks_section1_script_changes()
    {
        var viewModel = new ScriptManagerViewModel();

        viewModel.IsModified.Should().BeFalse();
        viewModel.DirtySummary.Should().BeEmpty();

        viewModel.SetDirtyState(true, "md1stin");

        viewModel.IsModified.Should().BeTrue();
        viewModel.DirtySummary.Should().Be("md1stin script data modified");

        viewModel.SetDirtyState(false, "md1stin");

        viewModel.IsModified.Should().BeFalse();
        viewModel.DirtySummary.Should().BeEmpty();
    }

    [Fact]
    public void loads_groups_scripts_and_opcodes_from_synthetic_section1_fixture()
    {
        var section = Section1File.Open(BuildSection1WithScripts());
        var viewModel = new ScriptManagerViewModel();

        viewModel.LoadSection(section);

        viewModel.Groups.Should().Equal(
            new GroupScriptViewModel(0, "dic", "Unknown"),
            new GroupScriptViewModel(1, "cloud", "Unknown"));
        viewModel.Scripts.Should().BeEmpty();
        viewModel.Opcodes.Should().BeEmpty();

        viewModel.SelectGroup(1);

        viewModel.SelectedGroup.Should().Be(new GroupScriptViewModel(1, "cloud", "Unknown"));
        viewModel.Scripts.Should().HaveCount(33);
        viewModel.Scripts[0].Should().Be(new ScriptEntryViewModel(0, "S0 - Init", 4));
        viewModel.Scripts[1].Should().Be(new ScriptEntryViewModel(1, "S0 - Main", 0));

        viewModel.SelectScript(0);

        viewModel.SelectedScript.Should().Be(new ScriptEntryViewModel(0, "S0 - Init", 4));
        viewModel.Opcodes.Should().Equal(
            new OpcodeEntryViewModel(0, "MESSAGE", "window=1, text=2", string.Empty),
            new OpcodeEntryViewModel(3, "RET", string.Empty, string.Empty));
    }

    [Fact]
    public void invalid_group_or_script_selection_clears_dependent_lists()
    {
        var section = Section1File.Open(BuildSection1WithScripts());
        var viewModel = new ScriptManagerViewModel();
        viewModel.LoadSection(section);
        viewModel.SelectGroup(1);
        viewModel.SelectScript(0);

        viewModel.SelectGroup(99);

        viewModel.SelectedGroup.Should().BeNull();
        viewModel.SelectedScript.Should().BeNull();
        viewModel.SelectedOpcode.Should().BeNull();
        viewModel.Scripts.Should().BeEmpty();
        viewModel.Opcodes.Should().BeEmpty();

        viewModel.SelectGroup(0);
        viewModel.SelectScript(99);

        viewModel.SelectedScript.Should().BeNull();
        viewModel.Opcodes.Should().BeEmpty();
    }

    private static byte[] BuildSection1WithScripts()
    {
        const int groupCount = 2;
        const int scriptCount = 32;
        var headerSize = 32;
        var scriptOffsetsOffset = headerSize + (groupCount * 8);
        var scriptDataOffset = scriptOffsetsOffset + (groupCount * scriptCount * 2);
        var script0 = new byte[] { 0x24, 0x34, 0x12, 0x00 };
        var script1 = new byte[] { 0x40, 0x01, 0x02, 0x00 };
        var posText = scriptDataOffset + script0.Length + script1.Length;
        var texts = BuildTextSection(["Map name", "Hello"]);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)0x0502);
        writer.Write((byte)groupCount);
        writer.Write((byte)1);
        writer.Write((ushort)posText);
        writer.Write((ushort)0);
        writer.Write((ushort)512);
        writer.Write(new byte[6]);
        WriteFixed(writer, "makou", 8);
        WriteFixed(writer, "md1stin", 8);
        WriteFixed(writer, "dic", 8);
        WriteFixed(writer, "cloud", 8);

        WriteScriptOffsets(writer, scriptDataOffset, script0.Length, script1.Length, posText);
        writer.Write(script0);
        writer.Write(script1);
        writer.Write(texts);

        return stream.ToArray();
    }

    private static void WriteScriptOffsets(BinaryWriter writer, int scriptDataOffset, int firstLength, int secondLength, int posText)
    {
        var first = checked((ushort)scriptDataOffset);
        var second = checked((ushort)(scriptDataOffset + firstLength));
        var text = checked((ushort)posText);

        writer.Write(first);
        for (var i = 1; i < 32; i++)
            writer.Write(second);

        writer.Write(second);
        for (var i = 1; i < 32; i++)
            writer.Write(text);
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

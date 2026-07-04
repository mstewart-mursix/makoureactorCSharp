using System.Buffers.Binary;
using System.Text;

using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public sealed class InfFileTests
{
    [Fact]
    public void open_reads_international_inf_metadata()
    {
        var inf = InfFile.Open(BuildInf());

        inf.IsJapanese.Should().BeFalse();
        inf.MapName.Should().Be("md1stin");
        inf.Control.Should().Be(0x80);
        inf.CameraFocusHeight.Should().Be(120);
        inf.CameraRange.Should().Be(new InfRange(-256, -128, 256, 384));
        inf.BackgroundLayers.Layer1Flag.Should().Be(1);
        inf.BackgroundLayers.Layer3Width.Should().Be(1024);
        inf.ExitLines[0].FieldId.Should().Be(0x1234);
        inf.ExitLines[0].Destination.Display.Should().Be("7, 8, 9");
        inf.Triggers[0].BackgroundParameter.Should().Be(0xFE);
        inf.Triggers[0].SoundId.Should().Be(9);
        inf.Arrows[0].Displayed.Should().BeTrue();
        inf.Arrows[0].Position.Should().Be("100, 200, 300");
        inf.Arrows[0].Type.Should().Be(2);
    }

    [Fact]
    public void open_accepts_japanese_inf_without_arrows()
    {
        var data = BuildInf()[..536];

        var inf = InfFile.Open(data);

        inf.IsJapanese.Should().BeTrue();
        inf.Arrows.Should().BeEmpty();
        inf.Triggers.Should().HaveCount(12);
    }

    [Fact]
    public void open_rejects_invalid_size()
    {
        var open = () => InfFile.Open([0x00]);

        open.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void save_round_trips_edited_general_metadata()
    {
        var inf = InfFile.Open(BuildInf());

        inf.SetGeneralMetadata("newmap", 0x44, 321, new InfRange(-1, -2, 3, 4));
        var reopened = InfFile.Open(inf.Save());

        reopened.MapName.Should().Be("newmap");
        reopened.Control.Should().Be(0x44);
        reopened.CameraFocusHeight.Should().Be(321);
        reopened.CameraRange.Should().Be(new InfRange(-1, -2, 3, 4));
        reopened.ExitLines[0].FieldId.Should().Be(0x1234);
    }

    [Fact]
    public void set_general_metadata_rejects_overlong_map_name()
    {
        var inf = InfFile.Open(BuildInf());

        var set = () => inf.SetGeneralMetadata("0123456789", 0, 0, new InfRange(0, 0, 0, 0));

        set.Should().Throw<InvalidDataException>()
            .WithMessage("INF map name must be 9 Latin-1 bytes or fewer.");
    }

    internal static byte[] BuildInf()
    {
        var data = new byte[740];
        Encoding.Latin1.GetBytes("md1stin").CopyTo(data, 0);
        data[9] = 0x80;
        WriteInt16(data, 10, 120);
        WriteInt16(data, 12, -256);
        WriteInt16(data, 14, -128);
        WriteInt16(data, 16, 256);
        WriteInt16(data, 18, 384);
        data[20] = 1;
        data[21] = 2;
        data[22] = 3;
        data[23] = 4;
        WriteInt16(data, 24, 1024);
        WriteInt16(data, 26, 768);
        WriteInt16(data, 28, 640);
        WriteInt16(data, 30, 480);

        var doorOffset = 56;
        WriteVertex(data, doorOffset, 1, 2, 3);
        WriteVertex(data, doorOffset + 6, 4, 5, 6);
        WriteVertex(data, doorOffset + 12, 7, 8, 9);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(doorOffset + 18, 2), 0x1234);
        data[doorOffset + 20] = 1;
        data[doorOffset + 21] = 2;
        data[doorOffset + 22] = 3;
        data[doorOffset + 23] = 4;

        var triggerOffset = 344;
        WriteVertex(data, triggerOffset, 10, 11, 12);
        WriteVertex(data, triggerOffset + 6, 13, 14, 15);
        data[triggerOffset + 12] = 0xFE;
        data[triggerOffset + 13] = 2;
        data[triggerOffset + 14] = 3;
        data[triggerOffset + 15] = 9;

        data[536] = 1;
        var arrowOffset = 548;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(arrowOffset, 4), 100);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(arrowOffset + 4, 4), 200);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(arrowOffset + 8, 4), 300);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(arrowOffset + 12, 4), 2);

        return data;
    }

    private static void WriteVertex(byte[] data, int offset, short x, short y, short z)
    {
        WriteInt16(data, offset, x);
        WriteInt16(data, offset + 2, y);
        WriteInt16(data, offset + 4, z);
    }

    private static void WriteInt16(byte[] data, int offset, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset, 2), value);
}

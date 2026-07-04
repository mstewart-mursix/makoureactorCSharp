using System.Buffers.Binary;

using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public sealed class AFileTests
{
    [Fact]
    public void write_emits_pc_animation_header_and_frame_records()
    {
        var animation = new FieldModelAnimation(new ModelCoordinate(1.5f, 2.5f, 3.5f));
        animation.AddFrame(
        [
            new ModelCoordinate(10, 20, 30),
            new ModelCoordinate(40, 50, 60),
        ],
        new ModelCoordinate(4, 5, 6));

        var data = AFile.Write(animation);

        data.Length.Should().Be(36 + 12 + 12 + (2 * 12));
        ReadUInt32(data, 0).Should().Be(1);
        ReadUInt32(data, 4).Should().Be(1);
        ReadUInt32(data, 8).Should().Be(2);
        data[12].Should().Be(1);
        data[13].Should().Be(0);
        data[14].Should().Be(2);

        ReadSingle(data, 36).Should().Be(1.5f);
        ReadSingle(data, 40).Should().Be(2.5f);
        ReadSingle(data, 44).Should().Be(3.5f);
        ReadSingle(data, 48).Should().Be(528);
        ReadSingle(data, 52).Should().Be(660);
        ReadSingle(data, 56).Should().Be(792);
        ReadSingle(data, 60).Should().Be(10);
        ReadSingle(data, 72).Should().Be(40);
    }

    [Fact]
    public void add_frame_rejects_inconsistent_bone_counts()
    {
        var animation = new FieldModelAnimation(new ModelCoordinate());
        animation.AddFrame([new ModelCoordinate()], new ModelCoordinate());

        var add = () => animation.AddFrame(
            [new ModelCoordinate(), new ModelCoordinate()],
            new ModelCoordinate());

        add.Should().Throw<InvalidDataException>()
            .WithMessage("All animation frames must contain the same bone rotation count.");
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

    private static float ReadSingle(byte[] data, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset, 4));
}

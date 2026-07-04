using System.Buffers.Binary;

using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public sealed class BsxAnimationFileTests
{
    [Fact]
    public void read_animation_decodes_dynamic_rotation_and_translation_tables()
    {
        var header = new BsxAnimationHeader(
            NumFrames: 2,
            NumBones: 2,
            NumFramesTranslation: 1,
            NumStaticTranslation: 0,
            NumFramesRotation: 1,
            OffsetFramesTranslation: 22,
            OffsetStaticTranslation: 26,
            OffsetFramesRotation: 20,
            OffsetData: 0);
        var data = BuildAnimationData();

        var animation = BsxAnimationFile.ReadAnimation(data, header);

        animation.FrameCount.Should().Be(2);
        animation.BoneCount.Should().Be(2);
        animation.Frames[0].Rotations[0].X.Should().BeApproximately(14.0625f, 0.0001f);
        animation.Frames[1].Rotations[0].X.Should().BeApproximately(28.125f, 0.0001f);
        animation.Frames[0].Translation.X.Should().Be(-1);
        animation.Frames[1].Translation.X.Should().Be(-2);
        animation.Frames[0].Rotations[1].X.Should().Be(90);
        animation.Frames[0].Rotations[1].Y.Should().Be(180);
        animation.Frames[0].Rotations[1].Z.Should().Be(270);
    }

    [Fact]
    public void to_pc_animation_removes_playstation_root_bone_and_uses_it_as_initial_rotation()
    {
        var animation = BsxAnimationFile.ReadAnimation(
            BuildAnimationData(),
            new BsxAnimationHeader(2, 2, 1, 0, 1, 22, 26, 20, 0));

        var pc = BsxAnimationFile.ToPcAnimation(animation);

        pc.FrameCount.Should().Be(2);
        pc.BoneCount.Should().Be(1);
        pc.InitialRotation.X.Should().BeApproximately(14.0625f, 0.0001f);
        pc.Frames[0].Rotations[0].X.Should().Be(90);
        pc.Frames[0].Translation.X.Should().Be(-1);
    }

    [Fact]
    public void model_catalog_exports_selected_model_animation_as_pc_a_file()
    {
        var bsx = BuildBsxWithOneModel();
        var catalog = BsxModelCatalog.Open(bsx);

        var exported = catalog.ExportPcAnimation(0, 0);

        catalog.Models.Should().ContainSingle();
        catalog.Models[0].ModelId.Should().Be(0x1234);
        catalog.Models[0].AnimationCount.Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(exported.AsSpan(0, 4)).Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(exported.AsSpan(4, 4)).Should().Be(2);
        BinaryPrimitives.ReadUInt32LittleEndian(exported.AsSpan(8, 4)).Should().Be(1);
        ReadSingle(exported, 36).Should().BeApproximately(14.0625f, 0.0001f);
        ReadSingle(exported, 48).Should().Be(-132);
    }

    private static byte[] BuildAnimationData()
    {
        var data = new byte[26];
        // offset 0..3 is the opaque animation block prefix skipped by BsxFile.
        data[4] = 0x11;
        data[5] = 0;
        data[6] = 0;
        data[7] = 0;
        data[8] = 0;
        data[9] = 0xFF;
        data[10] = 0xFF;

        data[12] = 0;
        data[13] = 64;
        data[14] = 128;
        data[15] = 192;
        data[16] = 0xFF;
        data[17] = 0xFF;
        data[18] = 0xFF;

        data[20] = 10;
        data[21] = 20;
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(22, 2), 4096);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(24, 2), 8192);
        return data;
    }

    private static byte[] BuildBsxWithOneModel()
    {
        var data = new byte[160];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24, 4), 120);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28, 4), 0);

        var modelOffset = 32;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(modelOffset, 2), 0x1234);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(modelOffset + 4, 4), 48);
        data[modelOffset + 27] = 2;
        data[modelOffset + 39] = 0;
        data[modelOffset + 47] = 1;

        var skeletonOffset = modelOffset + 48;
        var animationHeaderOffset = skeletonOffset + 8;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(animationHeaderOffset, 2), 2);
        data[animationHeaderOffset + 2] = 2;
        data[animationHeaderOffset + 3] = 1;
        data[animationHeaderOffset + 4] = 0;
        data[animationHeaderOffset + 5] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(animationHeaderOffset + 6, 2), 22);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(animationHeaderOffset + 8, 2), 26);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(animationHeaderOffset + 10, 2), 20);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(animationHeaderOffset + 12, 4), 112);

        BuildAnimationData().CopyTo(data.AsSpan(112));
        return data;
    }

    private static float ReadSingle(byte[] data, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset, 4));
}

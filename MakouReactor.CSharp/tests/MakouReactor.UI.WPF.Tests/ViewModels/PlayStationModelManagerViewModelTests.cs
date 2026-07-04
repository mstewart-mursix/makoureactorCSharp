using System.Buffers.Binary;

using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.ViewModels;

public sealed class PlayStationModelManagerViewModelTests
{
    [Fact]
    public void view_model_exposes_models_status_and_preview()
    {
        var viewModel = new PlayStationModelManagerViewModel("MD1STIN", BuildLoader());

        viewModel.FieldName.Should().Be("MD1STIN");
        viewModel.Models.Should().HaveCount(2);
        viewModel.SelectedModel.Should().Be(viewModel.Models[0]);
        viewModel.StatusText.Should().Be("2 PlayStation models loaded.");
        viewModel.PreviewText.Should().Contain("Model 0");
        viewModel.PreviewText.Should().Contain("Animations: 4");
    }

    [Fact]
    public void preview_updates_when_selected_model_changes()
    {
        var viewModel = new PlayStationModelManagerViewModel("MD1STIN", BuildLoader());

        viewModel.SelectedModel = viewModel.Models[1];

        viewModel.PreviewText.Should().Contain("Model 1");
        viewModel.PreviewText.Should().Contain("Model id: 16");
        viewModel.PreviewText.Should().Contain("Animations: 12");
    }

    [Fact]
    public void editing_model_row_marks_view_model_modified_and_updates_preview()
    {
        var viewModel = new PlayStationModelManagerViewModel("MD1STIN", BuildLoader());

        viewModel.SelectedModel!.AnimationCount = 9;

        viewModel.IsModified.Should().BeTrue();
        viewModel.StatusText.Should().Be("2 PlayStation models loaded; unapplied model-loader changes.");
        viewModel.PreviewText.Should().Contain("Animations: 9");
    }

    [Fact]
    public void apply_changes_writes_model_loader_section_to_playstation_field()
    {
        var field = FieldPS.OpenDecompressed("MD1STIN", BuildPsField(BuildLoader().Save()));
        var viewModel = new PlayStationModelManagerViewModel("MD1STIN", field.ModelLoaderPS!, field);

        viewModel.Models[1].ModelId = 0x42;
        viewModel.Models[1].FaceId = 0x24;
        viewModel.ApplyChanges();
        var reopenedLoader = FieldModelLoaderPS.Open(field.GetSectionData(FieldSection.ModelLoader));

        field.IsModified.Should().BeTrue();
        viewModel.IsModified.Should().BeFalse();
        reopenedLoader.Models[1].ModelId.Should().Be(0x42);
        reopenedLoader.Models[1].FaceId.Should().Be(0x24);
    }

    [Fact]
    public void animation_export_is_available_when_bsx_model_data_is_loaded()
    {
        var viewModel = new PlayStationModelManagerViewModel(
            "MD1STIN",
            BuildLoader(),
            bsxData: BuildBsxWithOneModel());

        var exported = viewModel.ExportSelectedAnimation();

        viewModel.CanExportAnimation.Should().BeTrue();
        viewModel.AvailableAnimationIndices.Should().Equal(0);
        viewModel.SelectedAnimationIndex.Should().Be(0);
        viewModel.AnimationExportStatusText.Should().Contain("can be exported");
        BinaryPrimitives.ReadUInt32LittleEndian(exported.AsSpan(0, 4)).Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(exported.AsSpan(8, 4)).Should().Be(1);
    }

    private static FieldModelLoaderPS BuildLoader() =>
        FieldModelLoaderPS.Open(
        [
            20, 0, 2, 0,
            1, 2, 3, 4, 5, 6, 7, 8,
            9, 10, 11, 12, 13, 14, 15, 16,
        ]);

    private static byte[] BuildPsField(byte[] modelLoader)
    {
        byte[][] sections =
        [
            [1, 2, 3, 4],
            [5, 6, 7, 8],
            [9, 10, 11, 12],
            [13, 14, 15, 16],
            [17, 18, 19, 20],
            [21, 22, 23, 24],
            modelLoader,
        ];

        var length = 28 + sections.Sum(static section => section.Length);
        var data = new byte[length];
        var offset = 28;
        for (var index = 0; index < sections.Length; index++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(index * 4, 4), (uint)offset);
            sections[index].CopyTo(data.AsSpan(offset));
            offset += sections[index].Length;
        }

        return data;
    }

    private static byte[] BuildBsxWithOneModel()
    {
        var data = new byte[160];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20, 4), 1);

        var modelOffset = 32;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(modelOffset, 2), 0x1234);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(modelOffset + 4, 4), 48);
        data[modelOffset + 27] = 2;
        data[modelOffset + 47] = 1;

        var animationHeaderOffset = modelOffset + 48 + 8;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(animationHeaderOffset, 2), 1);
        data[animationHeaderOffset + 2] = 2;
        data[animationHeaderOffset + 3] = 0;
        data[animationHeaderOffset + 4] = 0;
        data[animationHeaderOffset + 5] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(animationHeaderOffset + 6, 2), 20);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(animationHeaderOffset + 8, 2), 20);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(animationHeaderOffset + 10, 2), 20);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(animationHeaderOffset + 12, 4), 112);

        data[116] = 0;
        data[117] = 0;
        data[118] = 0;
        data[119] = 0;
        data[120] = 0xFF;
        data[121] = 0xFF;
        data[122] = 0xFF;
        data[124] = 64;
        data[125] = 128;
        data[126] = 192;
        data[127] = 0xFF;
        data[128] = 0xFF;
        data[129] = 0xFF;
        return data;
    }
}

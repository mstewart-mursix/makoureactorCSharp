using System.Buffers.Binary;

using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.ViewModels;

public sealed class PlayStationSectionManagerViewModelTests
{
    [Fact]
    public void view_model_exposes_ps_sections_and_preview()
    {
        var field = FieldPS.OpenDecompressed("MD1STIN", BuildPsField());
        var viewModel = new PlayStationSectionManagerViewModel(field);

        viewModel.FieldName.Should().Be("MD1STIN");
        viewModel.Sections.Should().HaveCount(7);
        viewModel.StatusText.Should().Be("7 PlayStation DAT sections loaded.");
        viewModel.SelectedSection.Should().Be(viewModel.Sections[0]);
        viewModel.PreviewText.Should().Contain("Scripts section");
        viewModel.PreviewText.Should().Contain("Size: 4 bytes");
        viewModel.PreviewText.Should().Contain("01 02 03 04");
    }

    [Fact]
    public void preview_updates_when_selected_section_changes()
    {
        var field = FieldPS.OpenDecompressed("MD1STIN", BuildPsField());
        var viewModel = new PlayStationSectionManagerViewModel(field);

        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Name == "ModelLoader");

        viewModel.PreviewText.Should().Contain("ModelLoader section");
        viewModel.PreviewText.Should().Contain("Parser: Parsed model loader");
        viewModel.PreviewText.Should().Contain("14 00 02 00");
    }

    [Fact]
    public void export_selected_section_returns_raw_section_bytes()
    {
        var field = FieldPS.OpenDecompressed("MD1STIN", BuildPsField());
        var viewModel = new PlayStationSectionManagerViewModel(field);

        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Name == "Walkmesh");

        viewModel.ExportSelectedSection().Should().Equal(5, 6, 7, 8);
    }

    [Fact]
    public void replace_selected_section_updates_field_dirty_state_and_preview()
    {
        var field = FieldPS.OpenDecompressed("MD1STIN", BuildPsField());
        var viewModel = new PlayStationSectionManagerViewModel(field);

        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Name == "Inf");
        viewModel.ReplaceSelectedSection([0xAA, 0xBB, 0xCC]);

        field.IsModified.Should().BeTrue();
        field.GetSectionData(FieldSection.Inf).Should().Equal(0xAA, 0xBB, 0xCC);
        viewModel.IsModified.Should().BeTrue();
        viewModel.StatusText.Should().Be("7 PlayStation DAT sections loaded; unsaved section changes.");
        viewModel.SelectedSection!.Size.Should().Be(3);
        viewModel.PreviewText.Should().Contain("AA BB CC");
    }

    private static byte[] BuildPsField()
    {
        byte[][] sections =
        [
            [1, 2, 3, 4],
            [5, 6, 7, 8],
            [9, 10, 11, 12],
            [13, 14, 15, 16],
            [17, 18, 19, 20],
            [21, 22, 23, 24],
            BuildModelLoader(),
        ];

        var length = 28 + sections.Sum(static section => section.Length);
        var data = new byte[length];
        var offset = 28;
        for (var index = 0; index < sections.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(index * 4, 4), (uint)offset);
            sections[index].CopyTo(data.AsSpan(offset));
            offset += sections[index].Length;
        }

        return data;
    }

    private static byte[] BuildModelLoader() =>
    [
        20, 0, 2, 0,
        1, 2, 3, 4, 5, 6, 7, 8,
        9, 10, 11, 12, 13, 14, 15, 16,
    ];
}

using FluentAssertions;

using MakouReactor.UI.WPF.Services;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Services;

public sealed class ShellCommandStatePolicyTests
{
    [Fact]
    public void archive_open_enables_archive_level_commands()
    {
        var state = ShellCommandStatePolicy.ForArchiveOpen(
            isOpen: true,
            canRunFf7: true,
            hasLlmSnapshot: true);

        state.Save.Should().BeFalse();
        state.SaveAs.Should().BeTrue();
        state.MassExport.Should().BeTrue();
        state.CloseArchive.Should().BeTrue();
        state.ArchiveAdd.Should().BeTrue();
        state.Batch.Should().BeTrue();
        state.RunFf7.Should().BeTrue();
        state.Texts.Should().BeFalse();
        state.LlmGenerate.Should().BeFalse();
        state.LlmRevert.Should().BeTrue();
    }

    [Fact]
    public void archive_closed_keeps_only_external_ff7_and_revert_commands_available()
    {
        var state = ShellCommandStatePolicy.ForArchiveOpen(
            isOpen: false,
            canRunFf7: true,
            hasLlmSnapshot: true);

        state.SaveAs.Should().BeFalse();
        state.CloseArchive.Should().BeFalse();
        state.ArchiveAdd.Should().BeFalse();
        state.Batch.Should().BeFalse();
        state.RunFf7.Should().BeTrue();
        state.LlmRevert.Should().BeTrue();
    }

    [Fact]
    public void archive_open_can_disable_batch_when_archive_type_is_not_supported()
    {
        var state = ShellCommandStatePolicy.ForArchiveOpen(
            isOpen: true,
            canRunFf7: false,
            hasLlmSnapshot: false,
            canBatch: false);

        state.SaveAs.Should().BeTrue();
        state.MassExport.Should().BeTrue();
        state.CloseArchive.Should().BeTrue();
        state.Batch.Should().BeFalse();
    }

    [Fact]
    public void pc_field_selection_enables_pc_managers_and_map_commands()
    {
        var state = ShellCommandStatePolicy.ForPcFieldSelection(
            isSelected: true,
            hasArchive: true,
            canRunFf7: true,
            hasLlmSnapshot: false);

        state.Save.Should().BeTrue();
        state.SaveAs.Should().BeTrue();
        state.ExportCurrentMap.Should().BeTrue();
        state.ExportChunks.Should().BeTrue();
        state.MassExport.Should().BeTrue();
        state.ImportCurrentMap.Should().BeTrue();
        state.ArchiveAdd.Should().BeTrue();
        state.Batch.Should().BeTrue();
        state.RunFf7.Should().BeTrue();
        state.Texts.Should().BeTrue();
        state.Models.Should().BeTrue();
        state.Encounters.Should().BeTrue();
        state.Tutorials.Should().BeTrue();
        state.Walkmesh.Should().BeTrue();
        state.Background.Should().BeTrue();
        state.Misc.Should().BeTrue();
        state.TextsTool.Should().BeTrue();
        state.ModelsTool.Should().BeTrue();
        state.WalkmeshTool.Should().BeTrue();
        state.LlmGenerate.Should().BeTrue();
        state.PlayStationSections.Should().BeFalse();
    }

    [Fact]
    public void pc_field_without_archive_can_save_but_cannot_import_export_or_batch()
    {
        var state = ShellCommandStatePolicy.ForPcFieldSelection(
            isSelected: true,
            hasArchive: false,
            canRunFf7: false,
            hasLlmSnapshot: true);

        state.Save.Should().BeTrue();
        state.ExportCurrentMap.Should().BeFalse();
        state.ImportCurrentMap.Should().BeFalse();
        state.ExportChunks.Should().BeTrue();
        state.CloseArchive.Should().BeTrue();
        state.SaveAs.Should().BeFalse();
        state.MassExport.Should().BeFalse();
        state.Batch.Should().BeFalse();
        state.LlmRevert.Should().BeTrue();
    }

    [Fact]
    public void playstation_field_enables_ps_sections_and_optional_model_manager()
    {
        var withoutModels = ShellCommandStatePolicy.ForPlayStationField(
            hasArchive: true,
            canMutateArchive: true,
            canRunFf7: true,
            hasModelLoader: false,
            hasLlmSnapshot: true);
        var withModels = ShellCommandStatePolicy.ForPlayStationField(
            hasArchive: true,
            canMutateArchive: true,
            canRunFf7: true,
            hasModelLoader: true,
            hasLlmSnapshot: true);

        withoutModels.PlayStationSections.Should().BeTrue();
        withoutModels.Models.Should().BeFalse();
        withoutModels.Save.Should().BeFalse();
        withoutModels.SaveAs.Should().BeTrue();
        withoutModels.CloseArchive.Should().BeTrue();
        withoutModels.Batch.Should().BeTrue();
        withoutModels.RunFf7.Should().BeTrue();
        withoutModels.LlmGenerate.Should().BeFalse();
        withoutModels.LlmRevert.Should().BeTrue();

        withModels.PlayStationSections.Should().BeTrue();
        withModels.Models.Should().BeTrue();
        withModels.ModelsTool.Should().BeTrue();
    }

    [Fact]
    public void playstation_disc_image_keeps_mutating_archive_commands_disabled()
    {
        var state = ShellCommandStatePolicy.ForPlayStationField(
            hasArchive: true,
            canMutateArchive: false,
            canRunFf7: false,
            hasModelLoader: true,
            hasLlmSnapshot: false);

        state.PlayStationSections.Should().BeTrue();
        state.Models.Should().BeTrue();
        state.SaveAs.Should().BeFalse();
        state.ArchiveAdd.Should().BeFalse();
        state.Batch.Should().BeFalse();
        state.CloseArchive.Should().BeTrue();
    }

    [Fact]
    public void dirty_state_allows_save_only_when_modified_field_has_save_target()
    {
        ShellCommandStatePolicy.ForDirtyState(isModified: true, hasSaveTarget: true)
            .Save.Should().BeTrue();

        ShellCommandStatePolicy.ForDirtyState(isModified: true, hasSaveTarget: false)
            .Save.Should().BeFalse();

        ShellCommandStatePolicy.ForDirtyState(isModified: false, hasSaveTarget: true)
            .Save.Should().BeFalse();
    }
}

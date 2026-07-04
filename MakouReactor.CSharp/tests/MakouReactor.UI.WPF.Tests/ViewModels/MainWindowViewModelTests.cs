using FluentAssertions;

using MakouReactor.Core.Services;
using MakouReactor.UI.WPF.ViewModels;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void no_archive_mode_disables_archive_and_field_commands()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.SaveCommand.CanExecute(null).Should().BeFalse();
        viewModel.SaveAsCommand.CanExecute(null).Should().BeFalse();
        viewModel.CloseCommand.CanExecute(null).Should().BeFalse();
        viewModel.ExportCurrentMapCommand.CanExecute(null).Should().BeFalse();
        viewModel.ImportCurrentMapCommand.CanExecute(null).Should().BeFalse();
        viewModel.MassExportCommand.CanExecute(null).Should().BeFalse();
        viewModel.FieldToolsCommand.CanExecute(null).Should().BeFalse();
        viewModel.LlmGenerateCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void archive_mode_enables_archive_commands_but_not_field_commands()
    {
        var viewModel = new MainWindowViewModel
        {
            IsArchiveOpen = true,
        };

        viewModel.SaveCommand.CanExecute(null).Should().BeFalse();
        viewModel.SaveAsCommand.CanExecute(null).Should().BeTrue();
        viewModel.CloseCommand.CanExecute(null).Should().BeTrue();
        viewModel.ExportCurrentMapCommand.CanExecute(null).Should().BeFalse();
        viewModel.ImportCurrentMapCommand.CanExecute(null).Should().BeFalse();
        viewModel.MassExportCommand.CanExecute(null).Should().BeTrue();
        viewModel.FieldToolsCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void selected_modified_field_enables_save_and_field_tools()
    {
        var viewModel = new MainWindowViewModel
        {
            IsArchiveOpen = true,
            IsFieldSelected = true,
            IsCurrentFieldModified = true,
        };

        viewModel.SaveCommand.CanExecute(null).Should().BeTrue();
        viewModel.ExportCurrentMapCommand.CanExecute(null).Should().BeTrue();
        viewModel.ImportCurrentMapCommand.CanExecute(null).Should().BeTrue();
        viewModel.FieldToolsCommand.CanExecute(null).Should().BeTrue();
        viewModel.LlmGenerateCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void save_command_tracks_dirty_state_changes()
    {
        var viewModel = new MainWindowViewModel
        {
            IsArchiveOpen = true,
            IsFieldSelected = true,
        };

        viewModel.SaveCommand.CanExecute(null).Should().BeFalse();

        viewModel.IsCurrentFieldModified = true;

        viewModel.SaveCommand.CanExecute(null).Should().BeTrue();

        viewModel.IsCurrentFieldModified = false;

        viewModel.SaveCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void run_ff7_command_follows_configuration_state()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.RunFf7Command.CanExecute(null).Should().BeFalse();

        viewModel.IsFf7Configured = true;

        viewModel.RunFf7Command.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void shell_state_tracks_archive_metadata_busy_state_and_script_selection()
    {
        var viewModel = new MainWindowViewModel
        {
            ArchivePath = @"C:\ff7\data\field\flevel.lgp",
            ArchiveType = ShellArchiveType.Lgp,
            ArchiveName = "flevel.lgp",
            IsBusy = true,
            BusyMessage = "Opening archive...",
            SelectedGroupIndex = 2,
            SelectedGroupName = "Cloud",
            SelectedScriptIndex = 1,
            SelectedScriptName = "Talk",
            SelectedOpcodeOffset = 42,
            SelectedOpcodeName = "MESSAGE",
        };

        viewModel.ArchivePath.Should().Be(@"C:\ff7\data\field\flevel.lgp");
        viewModel.ArchiveType.Should().Be(ShellArchiveType.Lgp);
        viewModel.ArchiveName.Should().Be("flevel.lgp");
        viewModel.IsBusy.Should().BeTrue();
        viewModel.BusyMessage.Should().Be("Opening archive...");
        viewModel.SelectedGroupIndex.Should().Be(2);
        viewModel.SelectedGroupName.Should().Be("Cloud");
        viewModel.SelectedScriptIndex.Should().Be(1);
        viewModel.SelectedScriptName.Should().Be("Talk");
        viewModel.SelectedOpcodeOffset.Should().Be(42);
        viewModel.SelectedOpcodeName.Should().Be("MESSAGE");
    }

    [Fact]
    public void archive_metadata_api_sets_name_path_and_type()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.SetArchiveMetadata(@"C:\ff7\data\field\flevel.lgp", ShellArchiveType.Lgp);

        viewModel.ArchivePath.Should().Be(@"C:\ff7\data\field\flevel.lgp");
        viewModel.ArchiveType.Should().Be(ShellArchiveType.Lgp);
        viewModel.ArchiveName.Should().Be("flevel.lgp");

        viewModel.SetArchiveMetadata(null, ShellArchiveType.None);

        viewModel.ArchivePath.Should().BeEmpty();
        viewModel.ArchiveType.Should().Be(ShellArchiveType.None);
        viewModel.ArchiveName.Should().Be("No archive open");
    }

    [Fact]
    public void clear_script_selection_resets_all_script_navigation_state()
    {
        var viewModel = new MainWindowViewModel
        {
            SelectedGroupIndex = 2,
            SelectedGroupName = "Cloud",
            SelectedScriptIndex = 1,
            SelectedScriptName = "Talk",
            SelectedOpcodeOffset = 42,
            SelectedOpcodeName = "MESSAGE",
        };

        viewModel.ClearScriptSelection();

        viewModel.SelectedGroupIndex.Should().BeNull();
        viewModel.SelectedGroupName.Should().BeEmpty();
        viewModel.SelectedScriptIndex.Should().BeNull();
        viewModel.SelectedScriptName.Should().BeEmpty();
        viewModel.SelectedOpcodeOffset.Should().BeNull();
        viewModel.SelectedOpcodeName.Should().BeEmpty();
    }

    [Fact]
    public void navigation_api_tracks_field_script_opcode_and_text_targets()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.NavigateToField("md1stin");

        viewModel.PendingNavigationTarget.Should().Be(new ShellNavigationTarget(
            "md1stin",
            null,
            null,
            null,
            null));

        viewModel.NavigateToScript("md1stin", 2, 1, 42);

        viewModel.PendingNavigationTarget.Should().Be(new ShellNavigationTarget(
            "md1stin",
            2,
            1,
            42,
            null));

        viewModel.NavigateToText("md1stin", 7);

        viewModel.PendingNavigationTarget.Should().Be(new ShellNavigationTarget(
            "md1stin",
            null,
            null,
            null,
            7));
    }

    [Fact]
    public void navigation_api_maps_search_results_to_targets()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.NavigateTo(new FieldSearchResult(
            "Opcode",
            "md1stin",
            1,
            "Cloud",
            2,
            128,
            null,
            "REQ",
            "Group 1 / Script 2 / 0x80"));

        viewModel.PendingNavigationTarget.Should().Be(new ShellNavigationTarget(
            "md1stin",
            1,
            2,
            128,
            null));

        viewModel.NavigateTo(new FieldSearchResult(
            "Text",
            "md1stin",
            null,
            null,
            null,
            null,
            3,
            "hello",
            "Text 3"));

        viewModel.PendingNavigationTarget.Should().Be(new ShellNavigationTarget(
            "md1stin",
            null,
            null,
            null,
            3));
    }

    [Fact]
    public void design_data_populates_shell_regions()
    {
        var viewModel = ShellDesignData.Create();

        viewModel.ArchiveName.Should().Be("flevel.lgp");
        viewModel.FieldList.Fields.Should().NotBeEmpty();
        viewModel.ScriptManager.Groups.Should().NotBeEmpty();
        viewModel.ScriptManager.Scripts.Should().NotBeEmpty();
        viewModel.ScriptManager.Opcodes.Should().NotBeEmpty();
        viewModel.ArchiveManager.Entries.Should().NotBeEmpty();
        viewModel.Preview.Summary.Should().Contain("Background preview");
    }
}

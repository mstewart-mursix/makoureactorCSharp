using FluentAssertions;

using MakouReactor.UI.WPF.Shared;

using Xunit;

namespace MakouReactor.UI.WPF.Tests;

public sealed class FileDialogFiltersTests
{
    [Fact]
    public void open_filter_matches_original_top_level_file_types()
    {
        FileDialogFilters.OpenFieldArchiveOrFile.Should().Contain("Compatible Files");
        FileDialogFilters.OpenFieldArchiveOrFile.Should().Contain("*.lgp");
        FileDialogFilters.OpenFieldArchiveOrFile.Should().Contain("*.DAT");
        FileDialogFilters.OpenFieldArchiveOrFile.Should().Contain("*.bin");
        FileDialogFilters.OpenFieldArchiveOrFile.Should().Contain("*.iso");
        FileDialogFilters.OpenFieldArchiveOrFile.Should().Contain("*.img");
        FileDialogFilters.OpenFieldArchiveOrFile.Should().Contain("*.lzs");
        FileDialogFilters.OpenFieldArchiveOrFile.Should().Contain("*.dec");
        FileDialogFilters.OpenFieldArchiveOrFile.Should().Contain("Disc Image");
    }

    [Fact]
    public void save_and_map_filters_keep_original_labels()
    {
        FileDialogFilters.SaveLgpArchive.Should().Contain("Lgp File (*.lgp)");
        FileDialogFilters.ExportPcField.Should().Contain("PC Field Map");
        FileDialogFilters.ExportPcField.Should().Contain("Uncompressed PC Field Map (*.dec)");
        FileDialogFilters.ImportField.Should().Contain("Data DAT File (*.DAT)");
        FileDialogFilters.ImportField.Should().Contain("Field chunk (*.chunk*.?)");
    }
}

using System.ComponentModel;
using System.IO;

using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.UI.WPF.Fields;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Fields;

public sealed class ShellFieldListServiceTests
{
    private static readonly FieldArchiveEntry[] Fields =
    [
        new(2, "zz1", 40),
        new(10, "md1stin", 20),
        new(1, "empty", 0),
    ];

    [Fact]
    public void build_items_filters_sorts_and_projects_status()
    {
        var unavailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "md1stin",
        };

        var items = ShellFieldListService.BuildItems(
            Fields,
            search: null,
            sortProperty: "Id",
            sortDirection: ListSortDirection.Ascending,
            unavailable);

        items.Select(item => item.Name).Should().Equal("empty", "zz1", "md1stin");
        items.Select(item => item.Status).Should().Equal("Empty", "Ready", "Unavailable");
        items[2].Id.Should().Be(10);
        items[2].Size.Should().Be(20);
    }

    [Fact]
    public void remember_selected_field_updates_normalized_archive_path()
    {
        var selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var archivePath = Path.Combine(Path.GetTempPath(), "ff7", "..", "ff7", "flevel.lgp");

        var changed = ShellFieldListService.RememberSelectedField(selections, archivePath, "md1stin");

        changed.Should().BeTrue();
        selections.Should().ContainKey(Path.GetFullPath(archivePath))
            .WhoseValue.Should().Be("md1stin");
        ShellFieldListService.LastSelectedFieldForArchive(selections, archivePath)
            .Should().Be("md1stin");
    }

    [Fact]
    public void remember_selected_field_ignores_duplicate_and_missing_archive()
    {
        var archivePath = Path.Combine(Path.GetTempPath(), "flevel.lgp");
        var selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFullPath(archivePath)] = "md1stin",
        };

        ShellFieldListService.RememberSelectedField(selections, archivePath, "MD1STIN")
            .Should().BeFalse();
        ShellFieldListService.RememberSelectedField(selections, null, "other")
            .Should().BeFalse();

        selections.Should().ContainSingle()
            .Which.Value.Should().Be("md1stin");
    }
}

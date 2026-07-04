using System.ComponentModel;

using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.Core.Services;

using Xunit;

namespace MakouReactor.Tests.Fields;

public sealed class FieldListServiceTests
{
    private static readonly FieldArchiveEntry[] Fields =
    [
        new(2, "zz1", 40),
        new(10, "md1stin", 20),
        new(1, "blin67_2", 80),
    ];

    [Fact]
    public void filter_matches_field_name_case_insensitively()
    {
        var fields = FieldListService.FilterAndSort(Fields, "MD1", null, ListSortDirection.Ascending);

        fields.Should().ContainSingle()
            .Which.Name.Should().Be("md1stin");
    }

    [Fact]
    public void filter_matches_field_id_text()
    {
        var fields = FieldListService.FilterAndSort(Fields, "10", null, ListSortDirection.Ascending);

        fields.Should().ContainSingle()
            .Which.Id.Should().Be(10);
    }

    [Fact]
    public void sort_by_name_ascending()
    {
        var fields = FieldListService.FilterAndSort(Fields, null, "Name", ListSortDirection.Ascending);

        fields.Select(field => field.Name).Should().Equal("blin67_2", "md1stin", "zz1");
    }

    [Fact]
    public void sort_by_id_descending()
    {
        var fields = FieldListService.FilterAndSort(Fields, null, "Id", ListSortDirection.Descending);

        fields.Select(field => field.Id).Should().Equal(10, 2, 1);
    }
}

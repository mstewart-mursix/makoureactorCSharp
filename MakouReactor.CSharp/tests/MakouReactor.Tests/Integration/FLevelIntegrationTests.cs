using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Integration;

public sealed class FLevelIntegrationTests
{
    [Fact]
    public void mr_test_flevel_opens_archive_and_first_field_when_configured()
    {
        var archivePath = TestArchivePath();
        if (archivePath == null)
            return;

        var archive = FieldArchive.Open(archivePath);

        archive.FieldEntries.Should().NotBeEmpty();
        archive.ArchiveEntries.Should().NotBeEmpty();

        var firstField = archive.OpenField(archive.FieldEntries[0].Name);

        firstField.IsOpen.Should().BeTrue();
        firstField.IsPC().Should().BeTrue();
        firstField.Sections.Should().HaveCount(9);
    }

    [Fact]
    public void mr_test_flevel_unmodified_first_field_round_trips_when_configured()
    {
        var archivePath = TestArchivePath();
        if (archivePath == null)
            return;

        var archive = FieldArchive.Open(archivePath);
        archive.FieldEntries.Should().NotBeEmpty();
        var firstFieldName = archive.FieldEntries[0].Name;
        var field = archive.OpenField(firstFieldName);

        field.IsModified.Should().BeFalse();
        field.SaveCompressed().Should().Equal(archive.ReadRawFile(firstFieldName));
    }

    private static string? TestArchivePath()
    {
        var path = Environment.GetEnvironmentVariable("MR_TEST_FLEVEL");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        File.Exists(path).Should().BeTrue("MR_TEST_FLEVEL must point to a readable flevel.lgp");
        Path.GetExtension(path).Should().Be(".lgp");
        return path;
    }
}

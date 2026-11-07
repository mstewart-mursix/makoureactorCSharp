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

    [Fact]
    public void mr_test_flevel_modified_fields_rewrite_every_section_losslessly_when_configured()
    {
        // A modified field re-serializes Section 1, the model loader, walkmesh, encounters and INF
        // even when only one of them changed, so each writer must reproduce the shipped bytes.
        var archivePath = TestArchivePath();
        if (archivePath == null)
            return;

        var archive = FieldArchive.Open(archivePath);
        var mismatches = new List<string>();
        foreach (var entry in archive.FieldEntries)
        {
            var field = archive.OpenField(entry.Name);
            if (field.SectionErrors.Count > 0)
                continue;

            var originalSections = field.Sections.ToDictionary(s => s.Section, s => field.GetSectionData(s.Section));
            field.SetModified();

            var reopened = FieldPC.OpenDecompressed(entry.Name, field.SaveDecompressed());
            foreach (var (section, bytes) in originalSections)
            {
                if (!reopened.GetSectionData(section).AsSpan().SequenceEqual(bytes))
                    mismatches.Add($"{entry.Name}: {section}");
            }
        }

        mismatches.Should().BeEmpty();
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

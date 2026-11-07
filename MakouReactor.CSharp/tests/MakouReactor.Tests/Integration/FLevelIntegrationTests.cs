using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Integration;

// Opening the real archive loads the game font process-wide, so run apart from parallel tests.
[Collection(MakouReactor.Tests.Text.GlobalFontCollection.Name)]
public sealed class FLevelIntegrationTests : IDisposable
{
    public void Dispose() => MakouReactor.Core.Text.FF7TextMetrics.UseBuiltInFont();

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

    [Fact]
    public void mr_test_flevel_new_actors_get_model_loader_entries_in_every_field_when_configured()
    {
        var archivePath = TestArchivePath();
        if (archivePath == null)
            return;

        var archive = FieldArchive.Open(archivePath);
        var problems = new List<string>();
        foreach (var entry in archive.FieldEntries)
        {
            var field = archive.OpenField(entry.Name);
            if (field.ScriptsAndTexts == null || field.ModelLoader == null)
                continue;

            var before = field.ModelLoader.Models.ToList();
            var plan = new ScenePlan();
            plan.Actors.Add(new Actor("cloud", "Cloud"));
            plan.Actors.Add(new Actor("npc_a", "Guard"));
            plan.Actors.Add(new Actor("npc_b", "Guard", x: 30, y: 30));
            var result = MakouReactor.AI.Mapping.ScenePlanMapper.ApplyToField(plan, field, new ApplyOptions { PreviewOnly = false });
            if (!result.Ok)
            {
                problems.Add($"{entry.Name}: {result.Error}");
                continue;
            }

            var reopened = FieldPC.OpenDecompressed(entry.Name, field.SaveDecompressed());
            var section = reopened.ScriptsAndTexts!;
            var models = reopened.ModelLoader!.Models;
            var modelGroups = section.GrpScripts.Count(g => g.Type == GrpScriptType.Model);
            if (modelGroups != models.Count)
                problems.Add($"{entry.Name}: {modelGroups} model groups but {models.Count} Model Loader entries");

            // Existing entries are untouched and every new one is a byte-identical copy of an old one.
            if (!models.Take(before.Count).Select(m => m.HrcName).SequenceEqual(before.Select(m => m.HrcName)))
                problems.Add($"{entry.Name}: existing Model Loader entries changed");
            foreach (var added in models.Skip(before.Count))
            {
                if (!before.Any(b => b.HrcName == added.HrcName && b.Animations.Select(a => a.Name).SequenceEqual(added.Animations.Select(a => a.Name))))
                    problems.Add($"{entry.Name}: new entry {added.HrcName} is not a copy of an existing model");
            }
        }

        problems.Should().BeEmpty();
    }

    [Fact]
    public void mr_test_flevel_layout_keeps_new_actors_out_of_every_fields_traffic_when_configured()
    {
        var archivePath = TestArchivePath();
        if (archivePath == null)
            return;

        var archive = FieldArchive.Open(archivePath);
        var problems = new List<string>();
        int fields = 0, moved = 0, nonSolid = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var entry in archive.FieldEntries)
        {
            var field = archive.OpenField(entry.Name);
            var walkmesh = MakouReactor.AI.Layout.LayoutGenerator.WalkmeshOf(field);
            if (walkmesh == null || field.ScriptsAndTexts == null)
                continue;

            fields++;
            var centre = MakouReactor.AI.Layout.WalkmeshGeometry.Centroid(walkmesh)!.Value;
            var plan = new ScenePlan();
            for (var i = 0; i < 3; i++)
                plan.Actors.Add(new Actor($"npc{i}", "Npc", x: centre.X, y: centre.Y));

            var layout = MakouReactor.AI.Layout.LayoutGenerator.Adjust(plan, field);
            moved += layout.Notes.Count(n => n.Contains("scripted paths, triggers and exits clear"));
            var blocked = layout.Notes.Count(n => n.Contains("non-solid"));
            nonSolid += blocked;

            var traffic = MakouReactor.AI.Layout.FieldTraffic.Of(field, walkmesh);
            var placed = new List<Point>();
            foreach (var actor in plan.Actors)
            {
                if (!MakouReactor.AI.Layout.WalkmeshGeometry.Contains(walkmesh, actor.Position))
                    problems.Add($"{entry.Name}: {actor.Id} off the walkmesh at {actor.Position}");
                if (!traffic.IsClear(actor.Position, placed) && blocked == 0)
                    problems.Add($"{entry.Name}: {actor.Id} at {actor.Position} is in the way and was not flagged");
                placed.Add(actor.Position);
            }
        }

        Console.WriteLine($"layout over {fields} fields: {moved} actor(s) moved off traffic, {nonSolid} with no clear spot, {clock.ElapsedMilliseconds} ms");
        problems.Should().BeEmpty();
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

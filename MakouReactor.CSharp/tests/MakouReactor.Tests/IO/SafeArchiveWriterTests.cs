using System.IO;
using System.Linq;

using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public sealed class SafeArchiveWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mr_safe_" + Guid.NewGuid().ToString("N"));
    private readonly string _archive;
    private DateTime _now = new(2026, 10, 31, 9, 0, 0);

    public SafeArchiveWriterTests()
    {
        Directory.CreateDirectory(_dir);
        _archive = Path.Combine(_dir, "flevel.lgp");
        LgpArchive.Create(_archive, new[]
        {
            new LgpArchiveFile("testmap", FieldPC.CreateEmpty("testmap").SaveCompressed()),
            new LgpArchiveFile("othermap", FieldPC.CreateEmpty("othermap").SaveCompressed()),
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private SafeArchiveWriter Writer(int keep = SafeArchiveWriter.DefaultBackupsToKeep) =>
        new(_archive, keep, () => _now);

    private FieldPC ModifiedField(string text = "Hello from the plan")
    {
        var field = FieldArchive.Open(_archive).OpenField("testmap");
        field.InsertText(field.ScriptsAndTexts!.TextCount, new FF7String(text));
        return field;
    }

    private static string? LastText(FieldPC field) =>
        field.ScriptsAndTexts!.TextCount == 0 ? null : field.ScriptsAndTexts.Texts[^1].Value;

    [Fact]
    public void apply_backs_up_original_updates_field_and_stays_reopenable()
    {
        var originalBytes = File.ReadAllBytes(_archive);

        var result = Writer().ApplyField(ModifiedField());

        result.Ok.Should().BeTrue(result.Error);
        result.Backup.Should().NotBeNull();
        Path.GetFileName(result.Backup!.Path).Should().Be("flevel.lgp.mr-backup-20261031-090000");
        File.ReadAllBytes(result.Backup.Path).Should().Equal(originalBytes);

        var reopened = FieldArchive.Open(_archive);
        LastText(reopened.OpenField("testmap")).Should().Be("Hello from the plan");
        reopened.OpenField("othermap").Should().NotBeNull("other fields survive the rewrite");
        File.Exists(Writer().TempPath).Should().BeFalse();
    }

    [Fact]
    public void failure_before_replace_leaves_original_untouched_and_cleans_up()
    {
        var originalBytes = File.ReadAllBytes(_archive);
        var writer = Writer();
        writer.BeforeReplace = _ => throw new IOException("disk full (simulated)");

        var result = writer.ApplyField(ModifiedField());

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("disk full");
        File.ReadAllBytes(_archive).Should().Equal(originalBytes);
        File.Exists(writer.TempPath).Should().BeFalse();
        writer.ListBackups().Should().BeEmpty("the redundant backup is removed when nothing changed");
    }

    [Fact]
    public void missing_archive_is_reported_not_thrown()
    {
        var result = new SafeArchiveWriter(Path.Combine(_dir, "nope.lgp")).ApplyField(FieldPC.CreateEmpty("testmap"));

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("Archive not found");
    }

    [Fact]
    public void prune_keeps_newest_five_and_the_pristine_original()
    {
        var originalBytes = File.ReadAllBytes(_archive);
        var writer = Writer();
        for (var i = 0; i < 7; i++)
        {
            _now = _now.AddMinutes(1);
            writer.ApplyField(ModifiedField($"apply {i}")).Ok.Should().BeTrue();
        }

        var backups = writer.ListBackups();
        backups.Should().HaveCount(6);
        backups[0].CreatedAt.Should().Be(new DateTime(2026, 10, 31, 9, 7, 0));
        backups[4].CreatedAt.Should().Be(new DateTime(2026, 10, 31, 9, 3, 0));
        backups[^1].CreatedAt.Should().Be(new DateTime(2026, 10, 31, 9, 1, 0));
        File.ReadAllBytes(backups[^1].Path).Should().Equal(originalBytes, "the first backup is never pruned");
    }

    [Fact]
    public void apply_leaves_no_staging_files_behind()
    {
        Writer().ApplyField(ModifiedField()).Ok.Should().BeTrue();

        Directory.GetFiles(_dir).Select(Path.GetFileName).Should().BeEquivalentTo(
            new[] { "flevel.lgp", "flevel.lgp.mr-backup-20261031-090000" });
    }

    [Fact]
    public void apply_marks_the_field_saved_only_on_success()
    {
        var failing = Writer();
        failing.BeforeReplace = _ => throw new IOException("disk full (simulated)");
        var field = ModifiedField();

        failing.ApplyField(field).Ok.Should().BeFalse();
        field.IsModified.Should().BeTrue("the edit was not written, so it must stay dirty");

        Writer().ApplyField(field).Ok.Should().BeTrue();
        field.IsModified.Should().BeFalse();
        LastText(FieldArchive.Open(_archive).OpenField("testmap")).Should().Be("Hello from the plan");
    }

    [Fact]
    public void same_second_backups_get_unique_names()
    {
        var writer = Writer();

        writer.ApplyField(ModifiedField("a")).Ok.Should().BeTrue();
        var second = writer.ApplyField(ModifiedField("b"));

        second.Ok.Should().BeTrue(second.Error);
        Path.GetFileName(second.Backup!.Path).Should().Be("flevel.lgp.mr-backup-20261031-090000-2");
        writer.ListBackups().Should().HaveCount(2);
        writer.ListBackups()[0].Path.Should().Be(second.Backup.Path, "the suffixed backup is the newer one");
    }

    [Fact]
    public void restore_newest_backup_reverts_last_apply()
    {
        var originalBytes = File.ReadAllBytes(_archive);
        var writer = Writer();
        writer.ApplyField(ModifiedField()).Ok.Should().BeTrue();

        var restored = writer.RestoreNewestBackup();

        restored.Should().NotBeNull();
        File.ReadAllBytes(_archive).Should().Equal(originalBytes);
        LastText(FieldArchive.Open(_archive).OpenField("testmap")).Should().NotBe("Hello from the plan");
    }

    [Fact]
    public void restore_without_backups_returns_null()
    {
        Writer().RestoreNewestBackup().Should().BeNull();
    }

    [Fact]
    public void unrelated_files_with_similar_names_are_not_backups()
    {
        File.WriteAllText(_archive + ".mr-backup-notadate", "x");
        File.WriteAllText(_archive + ".mr-backup-20261031-090000-x", "x");

        Writer().ListBackups().Should().BeEmpty();
    }
}

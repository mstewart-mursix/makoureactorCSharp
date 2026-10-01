using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public class SafeArchiveWriterTests
{
    private static string CreateArchive(TempDirectory temp)
    {
        var path = Path.Combine(temp.Path, "flevel.lgp");
        LgpArchive.Create(path,
        [
            new LgpArchiveFile("maplist", [0x01, 0x02]),
            new LgpArchiveFile("md1stin", FieldPC.CreateEmpty("md1stin").SaveCompressed()),
            new LgpArchiveFile("blackbga", FieldPC.CreateEmpty("blackbga").SaveCompressed()),
        ]);
        return path;
    }

    private static byte[] ModifiedField()
    {
        var field = FieldPC.CreateEmpty("md1stin");
        field.InsertText(0, new FF7String("Edited by the safe writer"));
        return field.SaveCompressed();
    }

    [Fact]
    public void write_field_makes_backup_updates_field_and_keeps_other_entries()
    {
        using var temp = new TempDirectory();
        var path = CreateArchive(temp);
        var originalBytes = File.ReadAllBytes(path);
        var blackbga = LgpArchive.Open(path).ReadFile("blackbga");
        var newField = ModifiedField();

        var result = SafeArchiveWriter.WriteField(path, "md1stin", newField);

        File.Exists(result.BackupPath).Should().BeTrue();
        File.ReadAllBytes(result.BackupPath).Should().Equal(originalBytes);
        var reopened = LgpArchive.Open(path);
        reopened.ReadFile("md1stin").Should().Equal(newField);
        reopened.ReadFile("blackbga").Should().Equal(blackbga);
        reopened.ReadFile("maplist").Should().Equal(0x01, 0x02);
        FieldPC.OpenCompressed("md1stin", reopened.ReadFile("md1stin"))
            .ScriptsAndTexts!.Texts.Select(t => t.Value).Should().Contain("Edited by the safe writer");
        Directory.GetFiles(temp.Path, "*.tmp-*").Should().BeEmpty();
    }

    [Fact]
    public void write_field_failure_leaves_original_untouched_and_cleans_up()
    {
        using var temp = new TempDirectory();
        var path = CreateArchive(temp);
        var originalBytes = File.ReadAllBytes(path);
        var notAField = LzsCompression.CompressWithHeader(new byte[100]);

        var act = () => SafeArchiveWriter.WriteField(path, "md1stin", notAField);

        act.Should().Throw<InvalidDataException>().WithMessage("*Verification*");
        File.ReadAllBytes(path).Should().Equal(originalBytes);
        Directory.GetFiles(temp.Path).Select(Path.GetFileName).Should().Equal("flevel.lgp");
    }

    [Fact]
    public void write_field_to_missing_archive_throws()
    {
        using var temp = new TempDirectory();

        var act = () => SafeArchiveWriter.WriteField(Path.Combine(temp.Path, "nope.lgp"), "md1stin", [1]);

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void backups_are_pruned_to_the_newest_five()
    {
        using var temp = new TempDirectory();
        var path = CreateArchive(temp);
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 8; i++)
            SafeArchiveWriter.WriteField(path, "md1stin", ModifiedField(), nowUtc: start.AddMinutes(i));

        var backups = SafeArchiveWriter.ListBackups(path);
        backups.Should().HaveCount(5);
        backups.Select(b => b.CreatedUtc).Should().BeInDescendingOrder();
        backups[0].CreatedUtc.Should().Be(start.AddMinutes(7));
        backups[^1].CreatedUtc.Should().Be(start.AddMinutes(3));
    }

    [Fact]
    public void two_writes_in_the_same_second_do_not_collide()
    {
        using var temp = new TempDirectory();
        var path = CreateArchive(temp);
        var at = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        SafeArchiveWriter.WriteField(path, "md1stin", ModifiedField(), nowUtc: at);
        SafeArchiveWriter.WriteField(path, "md1stin", ModifiedField(), nowUtc: at);

        SafeArchiveWriter.ListBackups(path).Should().HaveCount(2);
    }

    [Fact]
    public void restore_latest_backup_reverts_the_archive_and_keeps_an_undo_point()
    {
        using var temp = new TempDirectory();
        var path = CreateArchive(temp);
        var originalBytes = File.ReadAllBytes(path);
        SafeArchiveWriter.WriteField(path, "md1stin", ModifiedField());
        var modifiedBytes = File.ReadAllBytes(path);
        modifiedBytes.Should().NotEqual(originalBytes);

        var restored = SafeArchiveWriter.RestoreLatestBackup(path);

        File.ReadAllBytes(path).Should().Equal(originalBytes);
        restored.Path.Should().Contain(".mr-backup-");
        SafeArchiveWriter.ListBackups(path).Select(b => File.ReadAllBytes(b.Path))
            .Should().Contain(b => b.SequenceEqual(modifiedBytes), "the pre-restore state is kept");
    }

    [Fact]
    public void restore_without_backups_throws()
    {
        using var temp = new TempDirectory();
        var path = CreateArchive(temp);

        var act = () => SafeArchiveWriter.RestoreLatestBackup(path);

        act.Should().Throw<FileNotFoundException>();
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "MakouReactor.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}

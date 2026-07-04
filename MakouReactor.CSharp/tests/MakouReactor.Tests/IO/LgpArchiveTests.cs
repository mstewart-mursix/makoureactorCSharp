using FluentAssertions;

using MakouReactor.Core.IO;

using Xunit;

namespace MakouReactor.Tests.IO;

public class LgpArchiveTests
{
    [Fact]
    public void create_open_and_read_synthetic_archive()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "synthetic.lgp");

        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("maplist", [0x01, 0x02]),
            new LgpArchiveFile("md1stin", [0x10, 0x20, 0x30]),
            new LgpArchiveFile("dir/shared", [0xAA]),
            new LgpArchiveFile("other/shared", [0xBB, 0xCC]),
        ]);

        var archive = LgpArchive.Open(archivePath);

        archive.Entries.Select(entry => entry.FullPath).Should().Equal(
            "maplist",
            "md1stin",
            "dir/shared",
            "other/shared");
        archive.Entries.Select(entry => entry.Directory).Should().Equal("", "", "dir", "other");
        archive.ReadFile("md1stin").Should().Equal([0x10, 0x20, 0x30]);
        archive.ReadFile("dir/shared").Should().Equal([0xAA]);
        archive.ReadFile("other/shared").Should().Equal([0xBB, 0xCC]);
    }

    [Fact]
    public void ambiguous_file_name_requires_full_path()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "synthetic.lgp");

        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("dir/shared", [0xAA]),
            new LgpArchiveFile("other/shared", [0xBB]),
        ]);

        var archive = LgpArchive.Open(archivePath);

        var read = () => archive.ReadFile("shared");
        read.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void write_file_replaces_existing_entry_and_preserves_others()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "synthetic.lgp");

        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("maplist", [0x01, 0x02]),
            new LgpArchiveFile("md1stin", [0x10]),
        ]);

        var archive = LgpArchive.Open(archivePath);
        archive.WriteFile("md1stin", [0x44, 0x55, 0x66]);

        var reopened = LgpArchive.Open(archivePath);
        reopened.ReadFile("maplist").Should().Equal([0x01, 0x02]);
        reopened.ReadFile("md1stin").Should().Equal([0x44, 0x55, 0x66]);
        Directory.EnumerateFiles(temp.Path, "*.tmp-*").Should().BeEmpty();
        File.Exists($"{archivePath}.bak").Should().BeTrue();
    }

    [Fact]
    public void write_file_adds_new_entry()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "synthetic.lgp");

        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("maplist", [0x01]),
        ]);

        var archive = LgpArchive.Open(archivePath);
        archive.WriteFile("md1stin", [0x99]);

        var reopened = LgpArchive.Open(archivePath);
        reopened.Entries.Select(entry => entry.FullPath).Should().Equal("maplist", "md1stin");
        reopened.ReadFile("md1stin").Should().Equal([0x99]);
    }

    [Fact]
    public void remove_file_removes_entry_and_preserves_others()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "synthetic.lgp");

        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("maplist", [0x01]),
            new LgpArchiveFile("md1stin", [0x10]),
            new LgpArchiveFile("blackbga", [0x20]),
        ]);

        var archive = LgpArchive.Open(archivePath);
        archive.RemoveFile("md1stin");

        var reopened = LgpArchive.Open(archivePath);
        reopened.Entries.Select(entry => entry.FullPath).Should().Equal("maplist", "blackbga");
        reopened.ReadFile("blackbga").Should().Equal([0x20]);
        var readRemoved = () => reopened.ReadFile("md1stin");
        readRemoved.Should().Throw<FileNotFoundException>();
        File.Exists($"{archivePath}.bak").Should().BeTrue();
    }

    [Fact]
    public void rename_file_moves_entry_and_preserves_data()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "synthetic.lgp");

        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("maplist", [0x01]),
            new LgpArchiveFile("dir/md1stin", [0x10, 0x20]),
            new LgpArchiveFile("other/md1stin", [0x30]),
        ]);

        var archive = LgpArchive.Open(archivePath);
        archive.RenameFile("dir/md1stin", "renamed/md1stin");

        var reopened = LgpArchive.Open(archivePath);
        reopened.Entries.Select(entry => entry.FullPath).Should().Equal(
            "maplist",
            "renamed/md1stin",
            "other/md1stin");
        reopened.Entries.Select(entry => entry.FullPath).Should().NotContain("dir/md1stin");
        reopened.ReadFile("renamed/md1stin").Should().Equal([0x10, 0x20]);
        reopened.ReadFile("other/md1stin").Should().Equal([0x30]);
        File.Exists($"{archivePath}.bak").Should().BeTrue();
    }

    [Fact]
    public void rename_file_rejects_existing_destination()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "synthetic.lgp");

        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("one", [0x01]),
            new LgpArchiveFile("two", [0x02]),
        ]);

        var archive = LgpArchive.Open(archivePath);

        var rename = () => archive.RenameFile("one", "two");
        rename.Should().Throw<InvalidOperationException>();
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MakouReactor.Tests",
                Guid.NewGuid().ToString("N"));
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

using System.Buffers.Binary;
using System.Text;

using FluentAssertions;

using MakouReactor.Core.IO;

using Xunit;

namespace MakouReactor.Tests.IO;

/// <summary>
/// Byte-level audit of <see cref="LgpArchive"/> writing against the LGP layout used by the
/// ff7tk <c>Lgp</c> class referenced by <c>FieldArchiveIOPC.cpp</c>: 12-byte right-justified
/// "SQUARESOFT" header, file count, 27-byte TOC entries sorted by lookup value, a 30x30 lookup
/// table of (1-based TOC index, count), the conflict table, file blocks, and the product name.
/// </summary>
public class LgpArchiveAuditTests
{
    private const int TocEntrySize = 27;
    private const int LookupTableSize = 30 * 30 * 4;

    [Fact]
    public void written_archive_matches_lgp_layout_and_reads_back()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "audit.lgp");
        var files = new[]
        {
            new LgpArchiveFile("zz", [0x01]),
            new LgpArchiveFile("abc.txt", [0x02, 0x03]),
            new LgpArchiveFile("a.dat", [0x04]),
            new LgpArchiveFile("0zz", [0x05, 0x06, 0x07]),
            new LgpArchiveFile("_x1", []),
            new LgpArchiveFile("dir1/same.bin", [0x08]),
            new LgpArchiveFile("dir2/same.bin", [0x09, 0x0A]),
            new LgpArchiveFile("md1stin", Enumerable.Range(0, 300).Select(i => (byte)i).ToArray()),
        };

        LgpArchive.Create(archivePath, files);
        var bytes = File.ReadAllBytes(archivePath);

        bytes.AsSpan(0, 12).ToArray().Should().Equal(Encoding.Latin1.GetBytes("\0\0SQUARESOFT"));
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12)).Should().Be(files.Length);
        Encoding.Latin1.GetString(bytes, bytes.Length - 14, 14).Should().Be("FINAL FANTASY7");

        var toc = ReadToc(bytes, files.Length);
        toc.Select(entry => entry.Lookup).Should().BeInAscendingOrder();
        toc.Should().OnlyContain(entry => entry.Type == 0x0E);

        // Lookup values follow the FF7 formula: c1 * 30 + c2 + 1, with '.' as second char -> 0.
        toc.Single(entry => entry.Name == "a.dat").Lookup.Should().Be(0);
        toc.Single(entry => entry.Name == "abc.txt").Lookup.Should().Be(0 * 30 + 1 + 1);
        toc.Single(entry => entry.Name == "0zz").Lookup.Should().Be(0 * 30 + 25 + 1);
        toc.Single(entry => entry.Name == "_x1").Lookup.Should().Be(10 * 30 + 23 + 1);
        toc.Single(entry => entry.Name == "zz").Lookup.Should().Be(25 * 30 + 25 + 1);

        var lookupOffset = 16 + (files.Length * TocEntrySize);
        for (var lookup = 0; lookup < 900; lookup++)
        {
            var position = lookupOffset + (lookup * 4);
            var tocOffset = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position));
            var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 2));
            var matching = toc.Select((entry, index) => (entry, index))
                .Where(item => item.entry.Lookup == lookup)
                .ToArray();

            if (matching.Length == 0)
            {
                tocOffset.Should().Be(0);
                count.Should().Be(0);
            }
            else
            {
                tocOffset.Should().Be((ushort)(matching[0].index + 1), $"lookup {lookup}");
                count.Should().Be((ushort)matching.Length, $"lookup {lookup}");
            }
        }

        var conflictOffset = lookupOffset + LookupTableSize;
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(conflictOffset)).Should().Be(1);
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(conflictOffset + 2)).Should().Be(2);
        var conflictDirs = new List<(string Directory, int TocIndex)>();
        for (var i = 0; i < 2; i++)
        {
            var position = conflictOffset + 4 + (i * 130);
            conflictDirs.Add((
                ReadFixed(bytes, position, 128),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 128))));
        }

        conflictDirs.Select(item => item.Directory).Should().BeEquivalentTo(["dir1", "dir2"]);
        foreach (var (_, tocIndex) in conflictDirs)
        {
            toc[tocIndex].Name.Should().Be("same.bin");
            toc[tocIndex].Conflict.Should().Be(1);
        }

        toc.Where(entry => entry.Name != "same.bin").Should().OnlyContain(entry => entry.Conflict == 0);

        var firstDataOffset = conflictOffset + 2 + 2 + (2 * 130);
        toc.Min(entry => entry.Offset).Should().Be((uint)firstDataOffset);
        foreach (var entry in toc)
        {
            ReadFixed(bytes, (int)entry.Offset, 20).Should().Be(entry.Name);
        }

        var archive = LgpArchive.Open(archivePath);
        archive.Entries.Should().HaveCount(files.Length);
        foreach (var file in files)
            archive.ReadFile(file.FullPath).Should().Equal(file.Data, file.FullPath);
    }

    [Fact]
    public void rewriting_unchanged_archive_is_byte_stable()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "stable.lgp");
        LgpArchive.Create(archivePath,
        [
            new LgpArchiveFile("md1stin", [0x10, 0x20]),
            new LgpArchiveFile("md1_1", [0x30]),
            new LgpArchiveFile("a/dup", [0x40]),
            new LgpArchiveFile("b/dup", [0x50]),
        ]);
        var original = File.ReadAllBytes(archivePath);

        var archive = LgpArchive.Open(archivePath);
        archive.WriteFile("md1stin", [0x10, 0x20]);

        File.ReadAllBytes(archivePath).Should().Equal(original);
    }

    [Fact]
    public void empty_archive_round_trips()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "empty.lgp");

        LgpArchive.Create(archivePath, []);
        var archive = LgpArchive.Open(archivePath);

        archive.Entries.Should().BeEmpty();
        archive.ProductName.Should().Be("FINAL FANTASY7");
    }

    private static List<TocEntry> ReadToc(byte[] bytes, int count)
    {
        var entries = new List<TocEntry>();
        for (var i = 0; i < count; i++)
        {
            var position = 16 + (i * TocEntrySize);
            var name = ReadFixed(bytes, position, 20);
            entries.Add(new TocEntry(
                name,
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 20)),
                bytes[position + 24],
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 25)),
                Lookup(name)));
        }

        return entries;
    }

    private static int Lookup(string name)
    {
        static int Value(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            '_' => 10,
            '-' => 11,
            _ => char.ToLowerInvariant(c) - 'a',
        };

        return (Value(name[0]) * 30) + (name[1] == '.' ? 0 : Value(name[1]) + 1);
    }

    private static string ReadFixed(byte[] bytes, int offset, int size)
    {
        var span = bytes.AsSpan(offset, size);
        var end = span.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? span : span[..end]);
    }

    private sealed record TocEntry(string Name, uint Offset, byte Type, ushort Conflict, int Lookup);

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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MakouReactor.Core.IO;

/// <summary>
/// A single entry in an LGP archive table of contents.
/// </summary>
/// <param name="Name">The archive-local file name without a directory prefix.</param>
/// <param name="FullPath">The archive-local path, including any directory prefix.</param>
/// <param name="Directory">The archive-local directory, or an empty string for root entries.</param>
/// <param name="Size">The uncompressed file size stored in the archive.</param>
/// <param name="Offset">The byte offset of the file block in the archive.</param>
public sealed record LgpArchiveEntry(string Name, string FullPath, string Directory, int Size, uint Offset);

/// <summary>
/// Input file data used when creating a synthetic LGP archive.
/// </summary>
/// <param name="FullPath">The archive-local path to write.</param>
/// <param name="Data">The file bytes to store.</param>
public readonly record struct LgpArchiveFile(string FullPath, byte[] Data);

/// <summary>
/// Reads and rewrites Final Fantasy VII LGP archives.
/// </summary>
public sealed class LgpArchive
{
    private const string DefaultCompanyName = "SQUARESOFT";
    private const string DefaultProductName = "FINAL FANTASY7";
    private const int CompanyNameSize = 12;
    private const int ProductNameSize = 14;
    private const int TocEntrySize = 27;
    private const int FileNameSize = 20;
    private const int DirectoryNameSize = 128;
    private const int LookupValueMax = 30;
    private const int LookupTableEntries = LookupValueMax * LookupValueMax;
    private const int LookupTableSize = LookupTableEntries * 4;

    private readonly List<Entry> _entries = [];

    private LgpArchive(string archivePath, CancellationToken cancellationToken = default)
    {
        ArchivePath = archivePath;
        Load(cancellationToken);
    }

    /// <summary>
    /// Gets the path to the archive on disk.
    /// </summary>
    public string ArchivePath { get; }

    /// <summary>
    /// Gets the company header stored in the archive.
    /// </summary>
    public string CompanyName { get; private set; } = DefaultCompanyName;

    /// <summary>
    /// Gets the trailing product name stored in the archive.
    /// </summary>
    public string ProductName { get; private set; } = DefaultProductName;

    /// <summary>
    /// Gets archive entries sorted by file-block position.
    /// </summary>
    public IReadOnlyList<LgpArchiveEntry> Entries =>
        _entries
            .OrderBy(static entry => entry.Offset)
            .Select(static entry => new LgpArchiveEntry(
                entry.FileName,
                entry.FullPath,
                entry.Directory,
                entry.Size,
                entry.Offset))
            .ToArray();

    /// <summary>
    /// Opens an existing LGP archive from disk.
    /// </summary>
    /// <param name="archivePath">The archive path.</param>
    /// <returns>The opened archive.</returns>
    public static LgpArchive Open(string archivePath) => new(archivePath);

    public static LgpArchive Open(string archivePath, CancellationToken cancellationToken) =>
        new(archivePath, cancellationToken);

    /// <summary>
    /// Creates a new LGP archive from the supplied files.
    /// </summary>
    /// <param name="archivePath">The destination path.</param>
    /// <param name="files">The archive-local paths and file bytes to write.</param>
    public static void Create(string archivePath, IEnumerable<LgpArchiveFile> files) =>
        WriteArchive(archivePath, files, DefaultCompanyName, DefaultProductName);

    /// <summary>
    /// Reads a file from the archive.
    /// </summary>
    /// <param name="name">The archive-local path or an unambiguous file name.</param>
    /// <returns>The stored file bytes.</returns>
    public byte[] ReadFile(string name)
    {
        var entry = ResolveEntry(name);

        using var stream = File.OpenRead(ArchivePath);
        using var reader = new BinaryReader(stream, Encoding.Latin1, leaveOpen: false);
        stream.Position = entry.Offset;

        var blockName = ReadFixedString(ReadExact(reader, FileNameSize));
        if (!blockName.Equals(entry.FileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"LGP file block name '{blockName}' does not match '{entry.FileName}'.");

        var size = reader.ReadInt32();
        if (size < 0 || stream.Position + size > stream.Length)
            throw new InvalidDataException($"LGP file block '{entry.FullPath}' has an invalid size.");

        return ReadExact(reader, size);
    }

    /// <summary>
    /// Adds or replaces a file, writing through a temporary archive before replacing the original.
    /// </summary>
    /// <param name="name">The archive-local path or an unambiguous existing file name.</param>
    /// <param name="data">The bytes to store.</param>
    public void WriteFile(string name, byte[] data)
    {
        WriteFile(name, data, CancellationToken.None);
    }

    public void WriteFile(string name, byte[] data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedName = NormalizePath(name);
        var files = _entries
            .OrderBy(static entry => entry.Offset)
            .Select(entry =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new LgpArchiveFile(entry.FullPath, ReadFile(entry.FullPath));
            })
            .ToList();

        cancellationToken.ThrowIfCancellationRequested();
        var existingIndex = FindFileIndex(files, normalizedName);
        if (existingIndex >= 0)
        {
            var existing = files[existingIndex];
            files[existingIndex] = new LgpArchiveFile(existing.FullPath, data.ToArray());
        }
        else
        {
            files.Add(new LgpArchiveFile(normalizedName, data.ToArray()));
        }

        var tempPath = $"{ArchivePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            WriteArchive(tempPath, files, CompanyName, ProductName, cancellationToken);
            _ = Open(tempPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ReplaceArchive(tempPath, ArchivePath);
            Load(cancellationToken);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Removes a file, writing through a temporary archive before replacing the original.
    /// </summary>
    /// <param name="name">The archive-local path or an unambiguous existing file name.</param>
    public void RemoveFile(string name)
    {
        var entry = ResolveEntry(name);
        var files = _entries
            .OrderBy(static item => item.Offset)
            .Where(item => !item.FullPath.Equals(entry.FullPath, StringComparison.OrdinalIgnoreCase))
            .Select(item => new LgpArchiveFile(item.FullPath, ReadFile(item.FullPath)))
            .ToList();

        var tempPath = $"{ArchivePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            WriteArchive(tempPath, files, CompanyName, ProductName);
            _ = Open(tempPath);
            ReplaceArchive(tempPath, ArchivePath);
            Load();
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Renames or moves a file inside the archive, preserving its bytes.
    /// </summary>
    /// <param name="name">The archive-local path or an unambiguous existing file name.</param>
    /// <param name="newName">The new archive-local path.</param>
    public void RenameFile(string name, string newName)
    {
        var entry = ResolveEntry(name);
        var normalizedNewName = NormalizePath(newName);
        if (string.IsNullOrWhiteSpace(normalizedNewName))
            throw new ArgumentException("New archive entry name cannot be empty.", nameof(newName));
        if (FindEntry(normalizedNewName) != null)
            throw new InvalidOperationException($"Archive entry already exists: {normalizedNewName}");

        var files = _entries
            .OrderBy(static item => item.Offset)
            .Select(item => new LgpArchiveFile(
                item.FullPath.Equals(entry.FullPath, StringComparison.OrdinalIgnoreCase)
                    ? normalizedNewName
                    : item.FullPath,
                ReadFile(item.FullPath)))
            .ToList();

        var tempPath = $"{ArchivePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            WriteArchive(tempPath, files, CompanyName, ProductName);
            _ = Open(tempPath);
            ReplaceArchive(tempPath, ArchivePath);
            Load();
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private void Load(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries.Clear();

        using var stream = File.OpenRead(ArchivePath);
        using var reader = new BinaryReader(stream, Encoding.Latin1, leaveOpen: false);

        CompanyName = ReadHeaderString(ReadExact(reader, CompanyNameSize));
        if (!CompanyName.Equals(DefaultCompanyName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsupported LGP company header '{CompanyName}'.");

        var fileCount = reader.ReadInt32();
        if (fileCount < 0)
            throw new InvalidDataException("LGP file count cannot be negative.");

        for (var i = 0; i < fileCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = ReadFixedString(ReadExact(reader, FileNameSize));
            var offset = reader.ReadUInt32();
            var type = reader.ReadByte();
            var conflict = reader.ReadUInt16();

            _entries.Add(new Entry
            {
                TocIndex = i,
                FileName = name,
                FullPath = name,
                Offset = offset,
                Type = type,
                Conflict = conflict,
            });
        }

        if (stream.Length >= ProductNameSize)
        {
            stream.Position = stream.Length - ProductNameSize;
            ProductName = ReadFixedString(ReadExact(reader, ProductNameSize));
        }

        if (fileCount == 0)
            return;

        ResolveConflictDirectories(stream, reader, cancellationToken);
        ReadEntrySizes(stream, reader, cancellationToken);
    }

    private void ResolveConflictDirectories(Stream stream, BinaryReader reader, CancellationToken cancellationToken)
    {
        var minimumDataOffset = _entries.Min(static entry => entry.Offset);
        var conflictPosition = CompanyNameSize + 4 + (_entries.Count * TocEntrySize) + LookupTableSize;
        if (conflictPosition + 2 > minimumDataOffset || conflictPosition + 2 > stream.Length)
            return;

        stream.Position = conflictPosition;
        var conflictCount = reader.ReadUInt16();
        var conflicts = new List<List<ConflictRecord>>(conflictCount);

        for (var i = 0; i < conflictCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stream.Position + 2 > minimumDataOffset)
                throw new InvalidDataException("LGP conflict table exceeds the first file offset.");

            var entryCount = reader.ReadUInt16();
            var records = new List<ConflictRecord>(entryCount);
            for (var j = 0; j < entryCount; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (stream.Position + DirectoryNameSize + 2 > minimumDataOffset)
                    throw new InvalidDataException("LGP conflict entry exceeds the first file offset.");

                var directory = ReadFixedString(ReadExact(reader, DirectoryNameSize));
                var tocIndex = reader.ReadUInt16();
                records.Add(new ConflictRecord(directory, tocIndex));
            }

            conflicts.Add(records);
        }

        foreach (var entry in _entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Conflict == 0)
                continue;

            var conflictIndex = entry.Conflict - 1;
            if (conflictIndex >= conflicts.Count)
                throw new InvalidDataException($"LGP conflict index {entry.Conflict} is out of range.");

            var record = conflicts[conflictIndex]
                .FirstOrDefault(item => item.TocIndex == entry.TocIndex);
            if (record == null)
                throw new InvalidDataException($"LGP conflict for '{entry.FileName}' could not be resolved.");

            entry.Directory = record.Directory;
            entry.FullPath = string.IsNullOrEmpty(record.Directory)
                ? entry.FileName
                : $"{record.Directory}/{entry.FileName}";
        }
    }

    private void ReadEntrySizes(Stream stream, BinaryReader reader, CancellationToken cancellationToken)
    {
        foreach (var entry in _entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Offset > stream.Length - FileNameSize - 4)
                throw new InvalidDataException($"LGP entry '{entry.FullPath}' has an invalid offset.");

            stream.Position = entry.Offset;
            var blockName = ReadFixedString(ReadExact(reader, FileNameSize));
            if (!blockName.Equals(entry.FileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"LGP file block name '{blockName}' does not match '{entry.FileName}'.");

            var size = reader.ReadInt32();
            if (size < 0 || stream.Position + size > stream.Length)
                throw new InvalidDataException($"LGP file block '{entry.FullPath}' has an invalid size.");

            entry.Size = size;
        }
    }

    private Entry ResolveEntry(string name)
    {
        var normalizedName = NormalizePath(name);
        var fullPathMatch = FindEntry(normalizedName);
        if (fullPathMatch != null)
            return fullPathMatch;

        var fileName = FileNamePart(normalizedName);
        var fileNameMatches = _entries
            .Where(entry => entry.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return fileNameMatches.Length switch
        {
            0 => throw new FileNotFoundException($"LGP entry '{name}' was not found.", name),
            1 => fileNameMatches[0],
            _ => throw new InvalidOperationException($"LGP entry name '{name}' is ambiguous; use the full archive path."),
        };
    }

    private Entry? FindEntry(string normalizedName) =>
        _entries.FirstOrDefault(entry =>
            entry.FullPath.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));

    private static void WriteArchive(
        string archivePath,
        IEnumerable<LgpArchiveFile> files,
        string companyName,
        string productName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var writeEntries = files
            .Select((file, index) => WriteEntry.Create(file, index))
            .ToList();

        ValidateEntries(writeEntries);

        var tocEntries = writeEntries
            .OrderBy(static entry => entry.Lookup)
            .ThenBy(static entry => entry.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < tocEntries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tocEntries[i].TocIndex = i;
        }

        var conflictData = BuildConflictData(tocEntries);

        var directory = Path.GetDirectoryName(Path.GetFullPath(archivePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var stream = File.Open(archivePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: false);

        WriteFixedString(writer, companyName, CompanyNameSize, rightJustified: true);
        writer.Write(writeEntries.Count);

        var tocPosition = stream.Position;
        writer.Write(new byte[writeEntries.Count * TocEntrySize]);
        WriteLookupTable(writer, tocEntries);
        writer.Write(conflictData);

        foreach (var entry in writeEntries.OrderBy(static item => item.OriginalIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            entry.Offset = checked((uint)stream.Position);
            WriteFixedString(writer, entry.FileName, FileNameSize);
            writer.Write(entry.Data.Length);
            writer.Write(entry.Data);
        }

        WriteFixedString(writer, productName, ProductNameSize);

        stream.Position = tocPosition;
        foreach (var entry in tocEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteFixedString(writer, entry.FileName.ToLowerInvariant(), FileNameSize);
            writer.Write(entry.Offset);
            writer.Write(entry.Type);
            writer.Write(entry.Conflict);
        }
    }

    private static byte[] BuildConflictData(List<WriteEntry> tocEntries)
    {
        var conflicts = new List<List<ConflictRecord>>();

        foreach (var group in tocEntries.GroupBy(
                     static entry => (entry.Lookup, Name: entry.FileName.ToLowerInvariant())))
        {
            var entries = group.ToArray();
            if (entries.Length <= 1)
                continue;

            var conflictIndex = checked((ushort)(conflicts.Count + 1));
            var records = new List<ConflictRecord>(entries.Length);
            foreach (var entry in entries)
            {
                entry.Conflict = conflictIndex;
                records.Add(new ConflictRecord(entry.Directory, checked((ushort)entry.TocIndex)));
            }

            conflicts.Add(records);
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: false);

        writer.Write(checked((ushort)conflicts.Count));
        foreach (var conflict in conflicts)
        {
            writer.Write(checked((ushort)conflict.Count));
            foreach (var record in conflict)
            {
                WriteFixedString(writer, record.Directory, DirectoryNameSize);
                writer.Write(record.TocIndex);
            }
        }

        return stream.ToArray();
    }

    private static void WriteLookupTable(BinaryWriter writer, List<WriteEntry> tocEntries)
    {
        var lookupGroups = tocEntries
            .GroupBy(static entry => entry.Lookup)
            .ToDictionary(static group => group.Key, static group => group.ToArray());

        for (var lookup = 0; lookup < LookupTableEntries; lookup++)
        {
            if (lookupGroups.TryGetValue(lookup, out var entries))
            {
                writer.Write(checked((ushort)(entries[0].TocIndex + 1)));
                writer.Write(checked((ushort)entries.Length));
            }
            else
            {
                writer.Write((ushort)0);
                writer.Write((ushort)0);
            }
        }
    }

    private static void ValidateEntries(List<WriteEntry> entries)
    {
        var duplicate = entries
            .GroupBy(static entry => entry.FullPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate LGP entry '{duplicate.Key}'.");

        foreach (var entry in entries)
        {
            if (entry.FileName.Length is < 2 or > FileNameSize)
                throw new InvalidOperationException($"LGP file name '{entry.FileName}' must be 2 to 20 characters.");

            if (entry.Directory.Length > DirectoryNameSize)
                throw new InvalidOperationException($"LGP directory '{entry.Directory}' exceeds 128 characters.");

            if (entry.Lookup < 0)
                throw new InvalidOperationException($"LGP file name '{entry.FileName}' is not valid for lookup table indexing.");
        }
    }

    private static int FindFileIndex(List<LgpArchiveFile> files, string normalizedName)
    {
        var fullPathIndex = files.FindIndex(file =>
            NormalizePath(file.FullPath).Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
        if (fullPathIndex >= 0)
            return fullPathIndex;

        var fileName = FileNamePart(normalizedName);
        var fileNameIndexes = files
            .Select((file, index) => new { File = file, Index = index })
            .Where(item => FileNamePart(NormalizePath(item.File.FullPath))
                .Equals(fileName, StringComparison.OrdinalIgnoreCase))
            .Select(static item => item.Index)
            .ToArray();

        return fileNameIndexes.Length switch
        {
            0 => -1,
            1 => fileNameIndexes[0],
            _ => throw new InvalidOperationException($"LGP entry name '{normalizedName}' is ambiguous; use the full archive path."),
        };
    }

    private static void ReplaceArchive(string tempPath, string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            File.Move(tempPath, destinationPath);
            return;
        }

        try
        {
            File.Replace(tempPath, destinationPath, destinationBackupFileName: $"{destinationPath}.bak");
        }
        catch (PlatformNotSupportedException)
        {
            File.Copy(destinationPath, $"{destinationPath}.bak", overwrite: true);
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch (IOException)
        {
            File.Copy(destinationPath, $"{destinationPath}.bak", overwrite: true);
            File.Move(tempPath, destinationPath, overwrite: true);
        }
    }

    private static byte[] ReadExact(BinaryReader reader, int count)
    {
        var data = reader.ReadBytes(count);
        if (data.Length != count)
            throw new EndOfStreamException($"Expected {count} bytes, got {data.Length}.");

        return data;
    }

    private static string ReadHeaderString(byte[] data) =>
        Encoding.Latin1.GetString(data).Trim('\0');

    private static string ReadFixedString(byte[] data)
    {
        var length = Array.IndexOf(data, (byte)0);
        if (length < 0)
            length = data.Length;

        return Encoding.Latin1.GetString(data, 0, length);
    }

    private static void WriteFixedString(
        BinaryWriter writer,
        string value,
        int size,
        bool rightJustified = false)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        if (bytes.Length > size)
            throw new InvalidOperationException($"Value '{value}' is longer than {size} bytes.");

        var buffer = new byte[size];
        var offset = rightJustified ? size - bytes.Length : 0;
        bytes.CopyTo(buffer, offset);
        writer.Write(buffer);
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').Trim('/');

    private static string FileNamePart(string path)
    {
        var normalized = NormalizePath(path);
        var index = normalized.LastIndexOf('/');
        return index < 0 ? normalized : normalized[(index + 1)..];
    }

    private static string DirectoryPart(string path)
    {
        var normalized = NormalizePath(path);
        var index = normalized.LastIndexOf('/');
        return index < 0 ? string.Empty : normalized[..index];
    }

    private static int LookupValue(string fullPath)
    {
        var fileName = FileNamePart(fullPath);
        if (fileName.Length < 2)
            return -1;

        var first = LookupValue(fileName[0]);
        if (first < 0)
            return -1;

        var result = first * LookupValueMax;
        if (fileName[1] != '.')
        {
            var second = LookupValue(fileName[1]);
            if (second < 0)
                return -1;

            result += second + 1;
        }

        return result >= LookupTableEntries ? -1 : result;
    }

    private static int LookupValue(char value)
    {
        var c = char.ToLowerInvariant(value);
        if (c is >= '0' and <= '9')
            return c - '0';
        if (c == '_')
            return 10;
        if (c == '-')
            return 11;
        if (c is >= 'a' and <= '~')
        {
            var lookup = c - 'a';
            return lookup <= 29 ? lookup : -1;
        }

        return -1;
    }

    private sealed class Entry
    {
        public int TocIndex { get; init; }
        public string FileName { get; init; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public uint Offset { get; init; }
        public int Size { get; set; }
        public byte Type { get; init; }
        public ushort Conflict { get; init; }
    }

    private sealed class WriteEntry
    {
        private WriteEntry(string fullPath, byte[] data, int originalIndex)
        {
            FullPath = fullPath;
            FileName = FileNamePart(fullPath);
            Directory = DirectoryPart(fullPath);
            Data = data;
            OriginalIndex = originalIndex;
            Lookup = LookupValue(fullPath);
        }

        public string FullPath { get; }
        public string FileName { get; }
        public string Directory { get; }
        public byte[] Data { get; }
        public int OriginalIndex { get; }
        public int TocIndex { get; set; }
        public int Lookup { get; }
        public uint Offset { get; set; }
        public byte Type { get; } = 0x0E;
        public ushort Conflict { get; set; }

        public static WriteEntry Create(LgpArchiveFile file, int originalIndex) =>
            new(NormalizePath(file.FullPath), file.Data.ToArray(), originalIndex);
    }

    private sealed record ConflictRecord(string Directory, ushort TocIndex);
}

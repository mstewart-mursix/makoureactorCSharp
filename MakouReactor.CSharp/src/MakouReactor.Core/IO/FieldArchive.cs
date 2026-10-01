using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using MakouReactor.Core.Models;

namespace MakouReactor.Core.IO;

/// <summary>
/// Field archive facade used by GUI surfaces for PC LGP files, loose PC field
/// directories, and loose PlayStation field directories.
/// </summary>
public sealed class FieldArchive
{
    private readonly LgpArchive? _archive;
    private readonly List<LgpArchiveEntry>? _directoryEntries;
    private readonly List<Iso9660ImageEntry>? _imageEntries;

    private FieldArchive(
        string archivePath,
        FieldArchivePlatform platform,
        LgpArchive? archive,
        List<LgpArchiveEntry>? directoryEntries = null,
        List<Iso9660ImageEntry>? imageEntries = null)
    {
        ArchivePath = archivePath;
        Platform = platform;
        _archive = archive;
        _directoryEntries = directoryEntries;
        _imageEntries = imageEntries;
    }

    /// <summary>
    /// Gets the archive path on disk.
    /// </summary>
    public string ArchivePath { get; }

    public FieldArchivePlatform Platform { get; }

    /// <summary>
    /// Gets all raw archive entries.
    /// </summary>
    public IReadOnlyList<LgpArchiveEntry> ArchiveEntries => _archive?.Entries ?? _directoryEntries ?? ImageArchiveEntries;

    /// <summary>
    /// Gets a value indicating whether this archive facade is backed by a loose
    /// PC field directory instead of an LGP file.
    /// </summary>
    public bool IsLooseDirectory => _archive == null && _imageEntries == null;

    public bool IsReadOnlyImage => _imageEntries != null;

    private IReadOnlyList<LgpArchiveEntry> ImageArchiveEntries =>
        _imageEntries?
            .Select(static entry => new LgpArchiveEntry(
                entry.Name,
                entry.FullPath,
                ImageDirectory(entry.FullPath),
                entry.Size,
                checked((uint)Math.Min(entry.Offset, uint.MaxValue))))
            .ToArray() ?? [];

    private static string ImageDirectory(string fullPath)
    {
        var index = fullPath.LastIndexOf('/');
        return index <= 0 ? string.Empty : fullPath[..index];
    }

    /// <summary>
    /// Gets entries that follow the PC field naming convention.
    /// </summary>
    public IReadOnlyList<FieldArchiveEntry> FieldEntries =>
        ArchiveEntries
            .Where(entry => (Platform == FieldArchivePlatform.PlayStation || !entry.FullPath.Contains('/')) &&
                            IsLikelyFieldName(entry.Name))
            .Where(entry => Platform == FieldArchivePlatform.PlayStation
                ? IsLikelyPlayStationFieldName(entry.Name)
                : IsLikelyPcFieldName(entry.Name))
            .Select(static (entry, index) => new FieldArchiveEntry(index, entry.Name, entry.Size))
            .ToArray();

    /// <summary>
    /// Opens a PC LGP field archive.
    /// </summary>
    /// <param name="archivePath">Path to an LGP archive.</param>
    /// <returns>The opened field archive facade.</returns>
    public static FieldArchive Open(string archivePath)
    {
        return Open(archivePath, CancellationToken.None);
    }

    public static FieldArchive Open(string archivePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"Archive not found: {archivePath}", archivePath);

        return new FieldArchive(archivePath, FieldArchivePlatform.PC, LgpArchive.Open(archivePath, cancellationToken));
    }

    /// <summary>
    /// Opens a directory containing loose PC field files.
    /// </summary>
    /// <param name="directoryPath">Directory containing files such as maplist and field names.</param>
    /// <returns>The opened loose-directory field archive facade.</returns>
    public static FieldArchive OpenPcDirectory(string directoryPath)
    {
        return OpenPcDirectory(directoryPath, CancellationToken.None);
    }

    public static FieldArchive OpenPcDirectory(string directoryPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

        var root = Path.GetFullPath(directoryPath);
        var entries = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select((path, index) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileInfo = new FileInfo(path);
                var name = fileInfo.Name;
                return new LgpArchiveEntry(
                    name,
                    name,
                    string.Empty,
                    checked((int)Math.Min(fileInfo.Length, int.MaxValue)),
                    checked((uint)index));
            })
            .ToList();

        return new FieldArchive(root, FieldArchivePlatform.PC, archive: null, entries);
    }

    public static FieldArchive OpenPlayStationDirectory(string directoryPath)
    {
        return OpenPlayStationDirectory(directoryPath, CancellationToken.None);
    }

    public static FieldArchive OpenPlayStationDirectory(string directoryPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

        var root = Path.GetFullPath(directoryPath);
        var entries = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select((path, index) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileInfo = new FileInfo(path);
                var name = fileInfo.Name;
                return new LgpArchiveEntry(
                    name,
                    name,
                    string.Empty,
                    checked((int)Math.Min(fileInfo.Length, int.MaxValue)),
                    checked((uint)index));
            })
            .ToList();

        return new FieldArchive(root, FieldArchivePlatform.PlayStation, archive: null, entries);
    }

    public static FieldArchive OpenPlayStationImage(string imagePath)
    {
        return OpenPlayStationImage(imagePath, CancellationToken.None);
    }

    public static FieldArchive OpenPlayStationImage(string imagePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Image not found: {imagePath}", imagePath);

        var entries = Iso9660Image.ListFiles(imagePath, cancellationToken)
            .OrderBy(static entry => entry.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new FieldArchive(
            Path.GetFullPath(imagePath),
            FieldArchivePlatform.PlayStation,
            archive: null,
            directoryEntries: null,
            imageEntries: entries);
    }

    /// <summary>
    /// Reads a raw file from the underlying archive.
    /// </summary>
    /// <param name="name">Archive-local path or unambiguous file name.</param>
    /// <returns>The stored file bytes.</returns>
    public byte[] ReadRawFile(string name)
    {
        if (_archive != null)
            return _archive.ReadFile(name);
        if (_imageEntries != null)
        {
            var entry = ResolveImageEntry(name);
            return Iso9660Image.ReadFile(ArchivePath, entry);
        }

        return File.ReadAllBytes(ResolveLooseDirectoryPath(name));
    }

    /// <summary>
    /// Adds or replaces a raw archive entry through the safe LGP rewrite path.
    /// </summary>
    /// <param name="name">Archive-local path or unambiguous existing file name.</param>
    /// <param name="data">Raw file bytes to store.</param>
    public void ReplaceRawFile(string name, byte[] data)
    {
        if (_archive != null)
        {
            _archive.WriteFile(name, data);
            return;
        }
        if (_imageEntries != null)
            throw new NotSupportedException("PlayStation disc image entries are read-only.");

        File.WriteAllBytes(ResolveLooseDirectoryPath(name), data);
        RefreshLooseDirectoryEntries();
    }

    /// <summary>
    /// Removes a raw archive entry through the safe LGP rewrite path.
    /// </summary>
    /// <param name="name">Archive-local path or unambiguous existing file name.</param>
    public void RemoveRawFile(string name)
    {
        if (_archive != null)
        {
            _archive.RemoveFile(name);
            return;
        }
        if (_imageEntries != null)
            throw new NotSupportedException("PlayStation disc image entries are read-only.");

        File.Delete(ResolveLooseDirectoryPath(name));
        RefreshLooseDirectoryEntries();
    }

    /// <summary>
    /// Renames or moves a raw archive entry through the safe LGP rewrite path.
    /// </summary>
    /// <param name="name">Archive-local path or unambiguous existing file name.</param>
    /// <param name="newName">New archive-local path.</param>
    public void RenameRawFile(string name, string newName)
    {
        if (_archive != null)
        {
            _archive.RenameFile(name, newName);
            return;
        }
        if (_imageEntries != null)
            throw new NotSupportedException("PlayStation disc image entries are read-only.");

        var source = ResolveLooseDirectoryPath(name);
        var destination = BuildSafeLooseDirectoryPath(newName);
        if (File.Exists(destination))
            throw new InvalidOperationException($"Destination file already exists: {newName}");
        File.Move(source, destination);
        RefreshLooseDirectoryEntries();
    }

    /// <summary>
    /// Extracts a single archive entry to a destination file.
    /// </summary>
    /// <param name="name">Archive-local path or unambiguous file name.</param>
    /// <param name="destinationPath">The destination file path.</param>
    public void ExtractFile(string name, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllBytes(destinationPath, ReadRawFile(name));
    }

    /// <summary>
    /// Extracts every archive entry under a destination directory.
    /// </summary>
    /// <param name="destinationDirectory">The root output directory.</param>
    /// <returns>The number of extracted files.</returns>
    public int ExtractAll(string destinationDirectory)
    {
        return ExtractAll(destinationDirectory, CancellationToken.None);
    }

    public int ExtractAll(string destinationDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationDirectory);

        var root = Path.GetFullPath(destinationDirectory);
        var count = 0;
        foreach (var entry in ArchiveEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = BuildSafeExtractPath(root, entry.FullPath);
            ExtractFile(entry.FullPath, destinationPath);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Opens a PC field from the archive.
    /// </summary>
    /// <param name="name">Field file name.</param>
    /// <returns>The parsed PC field section table.</returns>
    public FieldPC OpenField(string name) =>
        Platform == FieldArchivePlatform.PC
            ? FieldPC.OpenCompressed(name, ReadRawFile(name))
            : throw new NotSupportedException("Use OpenPlayStationField to parse PlayStation DAT fields.");

    public FieldPS OpenPlayStationField(string name) =>
        Platform == FieldArchivePlatform.PlayStation
            ? FieldPS.OpenCompressed(Path.GetFileNameWithoutExtension(name), ReadRawFile(name))
            : throw new NotSupportedException("PlayStation field parsing is only available for PlayStation archives.");

    public byte[] ReadPlayStationModelData(string fieldName) =>
        Platform == FieldArchivePlatform.PlayStation
            ? LzsCompression.DecompressWithHeader(
                ReadRawFile($"{Path.GetFileNameWithoutExtension(fieldName).ToUpperInvariant()}.BSX"))
            : throw new NotSupportedException("PlayStation model data is only available for PlayStation archives.");

    /// <summary>
    /// Saves a parsed PC field back to the archive. Unmodified fields opened
    /// from this archive keep their original compressed bytes.
    /// </summary>
    /// <param name="field">The field to save.</param>
    public void SaveField(FieldPC field)
    {
        SaveField(field, CancellationToken.None);
    }

    public void SaveField(FieldPS field)
    {
        SaveField(field, CancellationToken.None);
    }

    public void SaveField(FieldPC field, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(field);
        cancellationToken.ThrowIfCancellationRequested();
        if (Platform != FieldArchivePlatform.PC)
            throw new NotSupportedException("Use SaveField(FieldPS) to save PlayStation DAT fields.");

        if (_archive != null)
        {
            // Keep a rotating, timestamped copy of the pre-save archive; the .bak written by the LGP
            // rewrite only ever holds the previous save.
            SafeArchiveWriter.BackupArchive(ArchivePath);
            _archive.WriteFile(field.Name, field.SaveCompressed(), cancellationToken);
        }
        else
        {
            File.WriteAllBytes(ResolveLooseDirectoryPath(field.Name), field.SaveCompressed());
        }
        cancellationToken.ThrowIfCancellationRequested();
        field.SetSaved();
        RefreshLooseDirectoryEntries();
    }

    public void SaveField(FieldPS field, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(field);
        cancellationToken.ThrowIfCancellationRequested();
        if (Platform != FieldArchivePlatform.PlayStation)
            throw new NotSupportedException("PlayStation field saving is only available for PlayStation directories.");
        if (!IsLooseDirectory)
            throw new NotSupportedException("PlayStation field saving is only available for loose directories.");

        var entryName = field.Name.EndsWith(".DAT", StringComparison.OrdinalIgnoreCase)
            ? field.Name
            : $"{field.Name}.DAT";
        File.WriteAllBytes(ResolveLooseDirectoryPath(entryName), field.SaveCompressed());
        cancellationToken.ThrowIfCancellationRequested();
        field.SetSaved();
        RefreshLooseDirectoryEntries();
    }

    public FieldPC CreatePcField(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();
        if (Platform != FieldArchivePlatform.PC)
            throw new NotSupportedException("Field creation is only available for PC archives.");
        if (_imageEntries != null)
            throw new NotSupportedException("PlayStation disc image entries are read-only.");

        var normalizedName = name.Trim();
        if (!IsLikelyPcFieldName(normalizedName) || normalizedName.Contains('/') || normalizedName.Contains('\\'))
            throw new InvalidDataException($"Invalid PC field name: {name}");
        if (ArchiveEntries.Any(entry => entry.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Archive entry already exists: {normalizedName}");

        var field = FieldPC.CreateEmpty(normalizedName);
        var bytes = field.SaveCompressed();
        if (_archive != null)
        {
            _archive.WriteFile(normalizedName, bytes, cancellationToken);
        }
        else
        {
            File.WriteAllBytes(BuildSafeLooseDirectoryPath(normalizedName), bytes);
            RefreshLooseDirectoryEntries();
        }

        return OpenField(normalizedName);
    }

    private string ResolveLooseDirectoryPath(string name)
    {
        var entry = ArchiveEntries.FirstOrDefault(entry =>
            entry.FullPath.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            throw new FileNotFoundException($"Directory entry not found: {name}", name);

        return BuildSafeLooseDirectoryPath(entry.FullPath);
    }

    private Iso9660ImageEntry ResolveImageEntry(string name)
    {
        if (_imageEntries == null)
            throw new InvalidOperationException("Image entries are not available for this archive.");

        var matches = _imageEntries
            .Where(entry =>
                entry.FullPath.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new FileNotFoundException($"Image entry not found: {name}", name),
            _ => throw new InvalidOperationException($"Image entry name is ambiguous: {name}"),
        };
    }

    private string BuildSafeLooseDirectoryPath(string archivePath)
    {
        if (!IsLooseDirectory)
            throw new InvalidOperationException("Loose directory paths are only available for directory archives.");

        var parts = archivePath
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
        if (parts.Length != 1 || parts[0] is "." or "..")
            throw new InvalidDataException($"Unsafe loose directory file name: {archivePath}");

        var root = Path.GetFullPath(ArchivePath);
        var destination = Path.GetFullPath(Path.Combine(root, parts[0]));
        if (!destination.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Loose directory file escapes archive path: {archivePath}");

        return destination;
    }

    private void RefreshLooseDirectoryEntries()
    {
        if (_directoryEntries == null)
            return;

        _directoryEntries.Clear();
        var refreshed = Platform == FieldArchivePlatform.PlayStation
            ? OpenPlayStationDirectory(ArchivePath)
            : OpenPcDirectory(ArchivePath);
        _directoryEntries.AddRange(refreshed.ArchiveEntries);
    }

    private static string BuildSafeExtractPath(string root, string archivePath)
    {
        var parts = archivePath
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => part is "." or ".."
                ? throw new InvalidDataException($"Unsafe archive path segment: {part}")
                : part)
            .ToArray();

        if (parts.Length == 0)
            throw new InvalidDataException("Archive entry path is empty.");

        var destination = Path.GetFullPath(Path.Combine([root, .. parts]));
        if (!destination.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) &&
            !destination.Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Archive entry path escapes destination: {archivePath}");

        return destination;
    }

    /// <summary>
    /// Returns true for names that are normally field files in PC flevel.lgp.
    /// </summary>
    /// <param name="name">Archive-local file name.</param>
    /// <returns>True when the entry should be shown in the field list.</returns>
    public static bool IsLikelyFieldName(string name) =>
        IsLikelyPcFieldName(name) || IsLikelyPlayStationFieldName(name);

    public static bool IsLikelyPcFieldName(string name) =>
        !name.Equals("maplist", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains('.') &&
        !string.IsNullOrWhiteSpace(name);

    public static bool IsLikelyPlayStationFieldName(string name) =>
        name.EndsWith(".DAT", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(name));
}

public enum FieldArchivePlatform
{
    PC,
    PlayStation,
}

/// <summary>
/// A field entry listed from an archive.
/// </summary>
/// <param name="Id">Zero-based list id for the current archive view.</param>
/// <param name="Name">Field file name.</param>
/// <param name="Size">Stored file size.</param>
public sealed record FieldArchiveEntry(int Id, string Name, int Size);

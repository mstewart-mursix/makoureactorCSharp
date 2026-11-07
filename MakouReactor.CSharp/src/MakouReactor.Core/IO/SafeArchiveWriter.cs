using System.Globalization;

using MakouReactor.Core.Models;

namespace MakouReactor.Core.IO;

/// <summary>A timestamped backup created by <see cref="SafeArchiveWriter"/>.</summary>
/// <param name="Path">Full path of the backup file.</param>
/// <param name="CreatedAt">Timestamp encoded in the file name (local time).</param>
public sealed record ArchiveBackup(string Path, DateTime CreatedAt);

/// <summary>Outcome of <see cref="SafeArchiveWriter.ApplyField"/>.</summary>
public sealed class SafeApplyResult
{
    /// <summary>True when the archive now contains the modified field.</summary>
    public bool Ok { get; init; }

    /// <summary>Failure description; empty on success. The original archive is untouched on failure.</summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>Backup of the archive as it was before this apply (null on failure).</summary>
    public ArchiveBackup? Backup { get; init; }

    /// <summary>Backups deleted by pruning after a successful apply.</summary>
    public IReadOnlyList<string> PrunedBackups { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Writes a modified field into a PC <c>flevel.lgp</c> without ever leaving the original in a
/// half-written state:
/// <list type="number">
/// <item>copy the archive to <c>&lt;archive&gt;.mr-backup-yyyyMMdd-HHmmss</c>;</item>
/// <item>write the change into <c>&lt;archive&gt;.tmp</c>, re-open it and verify the field reads
/// back byte-identical;</item>
/// <item>atomically replace the original, then prune old backups (newest
/// <see cref="BackupsToKeep"/> kept, plus the oldest one, which is normally the untouched
/// original archive).</item>
/// </list>
/// On any failure the temporary files and the just-made backup are removed, the original
/// archive bytes are unchanged and the field stays marked modified.
/// </summary>
public sealed class SafeArchiveWriter
{
    /// <summary>Default number of backups retained.</summary>
    public const int DefaultBackupsToKeep = 5;

    /// <summary>Infix used in backup file names.</summary>
    public const string BackupInfix = ".mr-backup-";

    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    private readonly Func<DateTime> _clock;

    /// <summary>Create a writer for the PC LGP archive at <paramref name="archivePath"/>.</summary>
    public SafeArchiveWriter(string archivePath, int backupsToKeep = DefaultBackupsToKeep)
        : this(archivePath, backupsToKeep, static () => DateTime.Now)
    {
    }

    internal SafeArchiveWriter(string archivePath, int backupsToKeep, Func<DateTime> clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(backupsToKeep, 1);
        ArchivePath = Path.GetFullPath(archivePath);
        BackupsToKeep = backupsToKeep;
        _clock = clock;
    }

    /// <summary>Full path of the archive being protected.</summary>
    public string ArchivePath { get; }

    /// <summary>
    /// Number of newest backups retained after each successful apply (the oldest backup is
    /// always kept in addition).
    /// </summary>
    public int BackupsToKeep { get; }

    /// <summary>Path of the staging file the modified archive is written to.</summary>
    public string TempPath => ArchivePath + ".tmp";

    /// <summary>Test hook: runs after the staging archive is written and verified, before the swap.</summary>
    internal Action<string>? BeforeReplace { get; set; }

    /// <summary>
    /// Save <paramref name="field"/> into the archive through the backup → stage → verify →
    /// replace pipeline. Never throws for IO or format problems; see <see cref="SafeApplyResult.Error"/>.
    /// </summary>
    public SafeApplyResult ApplyField(FieldPC field)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (!File.Exists(ArchivePath))
            return new SafeApplyResult { Error = $"Archive not found: {ArchivePath}" };

        ArchiveBackup? backup = null;
        try
        {
            backup = CreateBackup();

            var expected = field.SaveDecompressed();
            var compressed = field.SaveCompressed();

            // Stage through LgpArchive directly: FieldArchive.SaveField would mark the field saved
            // before the staged copy is verified, losing the dirty flag if verification fails.
            File.Copy(ArchivePath, TempPath, overwrite: true);
            LgpArchive.Open(TempPath).WriteFile(field.Name, compressed);

            VerifyStagedField(field.Name, expected);
            BeforeReplace?.Invoke(TempPath);

            ReplaceAtomically(TempPath, ArchivePath);
            field.SetSaved();
            var pruned = PruneBackups();
            return new SafeApplyResult { Ok = true, Backup = backup, PrunedBackups = pruned };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                       or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            if (backup != null)
                TryDelete(backup.Path);
            return new SafeApplyResult { Error = ex.Message };
        }
        finally
        {
            TryDelete(TempPath);
            // LgpArchive.WriteFile keeps a ".bak" of the file it replaces; for the staging copy
            // that is a second full-size archive nobody needs.
            TryDelete(TempPath + ".bak");
        }
    }

    /// <summary>All backups of this archive, newest first.</summary>
    public IReadOnlyList<ArchiveBackup> ListBackups()
    {
        var directory = Path.GetDirectoryName(ArchivePath)!;
        if (!Directory.Exists(directory))
            return Array.Empty<ArchiveBackup>();

        var prefix = Path.GetFileName(ArchivePath) + BackupInfix;
        return Directory.EnumerateFiles(directory, prefix + "*")
            .Select(path => TryParseBackup(path, prefix))
            .OfType<ArchiveBackup>()
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Restore the newest backup over the archive (atomically, via the staging file).
    /// Returns the restored backup, or null when none exists. The backup file is kept.
    /// </summary>
    public ArchiveBackup? RestoreNewestBackup()
    {
        var newest = ListBackups().FirstOrDefault();
        if (newest == null)
            return null;

        try
        {
            File.Copy(newest.Path, TempPath, overwrite: true);
            _ = LgpArchive.Open(TempPath);
            ReplaceAtomically(TempPath, ArchivePath);
        }
        finally
        {
            TryDelete(TempPath);
        }
        return newest;
    }

    // -----------------------------------------------------------------------
    // Pipeline steps
    // -----------------------------------------------------------------------

    private ArchiveBackup CreateBackup()
    {
        var now = _clock();
        var stamp = now.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var path = ArchivePath + BackupInfix + stamp;
        for (var n = 2; File.Exists(path); n++)
            path = ArchivePath + BackupInfix + stamp + "-" + n.ToString(CultureInfo.InvariantCulture);

        File.Copy(ArchivePath, path, overwrite: false);
        return new ArchiveBackup(path, DateTime.ParseExact(stamp, TimestampFormat, CultureInfo.InvariantCulture));
    }

    /// <summary>Re-open the staged archive and require the field to read back byte-identical.</summary>
    private void VerifyStagedField(string fieldName, byte[] expected)
    {
        var reopened = FieldArchive.Open(TempPath).OpenField(fieldName);
        if (!reopened.SaveDecompressed().AsSpan().SequenceEqual(expected))
            throw new InvalidDataException($"Verification failed: field '{fieldName}' did not read back identically from the staged archive.");
    }

    private IReadOnlyList<string> PruneBackups()
    {
        var pruned = new List<string>();
        var backups = ListBackups();
        var oldest = backups.Count > 0 ? backups[^1] : null;
        foreach (var old in backups.Skip(BackupsToKeep))
        {
            // Never prune the oldest backup: after the first apply it is the pristine archive.
            if (ReferenceEquals(old, oldest))
                continue;

            if (TryDelete(old.Path))
                pruned.Add(old.Path);
        }
        return pruned;
    }

    private static void ReplaceAtomically(string source, string destination)
    {
        try
        {
            File.Replace(source, destination, destinationBackupFileName: null);
        }
        catch (PlatformNotSupportedException)
        {
            File.Move(source, destination, overwrite: true);
        }
    }

    private static ArchiveBackup? TryParseBackup(string path, string prefix)
    {
        var suffix = Path.GetFileName(path)[prefix.Length..];
        var stamp = suffix.Length >= TimestampFormat.Length ? suffix[..TimestampFormat.Length] : suffix;
        var rest = suffix[stamp.Length..];
        if (rest.Length > 0 && (rest[0] != '-' || !rest[1..].All(char.IsAsciiDigit) || rest.Length == 1))
            return null;

        return DateTime.TryParseExact(stamp, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var createdAt)
            ? new ArchiveBackup(path, createdAt)
            : null;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

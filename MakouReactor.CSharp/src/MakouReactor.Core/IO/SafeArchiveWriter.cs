using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using MakouReactor.Core.Models;

namespace MakouReactor.Core.IO;

/// <summary>A timestamped backup of an archive created by <see cref="SafeArchiveWriter"/>.</summary>
/// <param name="Path">Full path of the backup file.</param>
/// <param name="CreatedUtc">Timestamp encoded in the backup name.</param>
public sealed record ArchiveBackup(string Path, DateTime CreatedUtc);

/// <summary>Outcome of a safe field write.</summary>
/// <param name="BackupPath">The backup taken before the write.</param>
/// <param name="PrunedBackups">How many older backups were deleted.</param>
public sealed record SafeWriteResult(string BackupPath, int PrunedBackups);

/// <summary>
/// Writes a field into an LGP archive without ever leaving it half-written: take a timestamped backup,
/// build the new archive beside the original, verify it re-opens and the field parses back to the
/// same bytes, then atomically replace the original. Any failure leaves the original untouched.
/// </summary>
public static class SafeArchiveWriter
{
    /// <summary>Number of backups kept per archive.</summary>
    public const int DefaultKeepBackups = 5;

    private const string BackupMarker = ".mr-backup-";
    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    /// <summary>
    /// Replace (or add) <paramref name="fieldName"/> in the archive with <paramref name="compressedFieldBytes"/>
    /// (header-prefixed LZS, as stored in flevel.lgp).
    /// </summary>
    /// <exception cref="InvalidDataException">The written archive failed verification; the original is unchanged.</exception>
    public static SafeWriteResult WriteField(
        string archivePath,
        string fieldName,
        byte[] compressedFieldBytes,
        int keepBackups = DefaultKeepBackups,
        DateTime? nowUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentNullException.ThrowIfNull(compressedFieldBytes);
        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"Archive not found: {archivePath}", archivePath);

        var source = LgpArchive.Open(archivePath);
        var backupPath = CreateBackup(archivePath, nowUtc ?? DateTime.UtcNow);
        var tempPath = $"{archivePath}.tmp-{Guid.NewGuid():N}";
        var replaced = false;
        try
        {
            source.WriteFileCopy(tempPath, fieldName, compressedFieldBytes);
            Verify(tempPath, fieldName, compressedFieldBytes);
            ReplaceAtomically(tempPath, archivePath);
            replaced = true;
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            // A failed write leaves the original untouched, so the backup would only be clutter.
            if (!replaced && File.Exists(backupPath))
                File.Delete(backupPath);
        }

        var pruned = Prune(archivePath, keepBackups);
        return new SafeWriteResult(backupPath, pruned);
    }

    /// <summary>
    /// Takes a timestamped backup of the archive as it is now and prunes old ones. Used before in-place
    /// saves so the pre-edit archive survives repeated saves (a plain <c>.bak</c> is overwritten each time).
    /// </summary>
    /// <returns>The backup path.</returns>
    public static string BackupArchive(string archivePath, int keepBackups = DefaultKeepBackups, DateTime? nowUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        var backupPath = CreateBackup(archivePath, nowUtc ?? DateTime.UtcNow);
        Prune(archivePath, keepBackups);
        return backupPath;
    }

    /// <summary>Lists backups of <paramref name="archivePath"/>, newest first.</summary>
    public static IReadOnlyList<ArchiveBackup> ListBackups(string archivePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(archivePath)) ?? ".";
        var prefix = Path.GetFileName(archivePath) + BackupMarker;
        if (!Directory.Exists(directory))
            return [];

        var backups = new List<ArchiveBackup>();
        foreach (var path in Directory.EnumerateFiles(directory, prefix + "*"))
        {
            var stamp = Path.GetFileName(path)[prefix.Length..];
            var core = stamp.Length >= TimestampFormat.Length ? stamp[..TimestampFormat.Length] : stamp;
            if (DateTime.TryParseExact(core, TimestampFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var created))
                backups.Add(new ArchiveBackup(path, created));
        }

        return backups
            .OrderByDescending(static b => b.CreatedUtc)
            .ThenByDescending(static b => b.Path, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Restores the newest backup over the archive (the current archive is itself backed up first so a
    /// restore can be undone). Returns the backup that was restored.
    /// </summary>
    public static ArchiveBackup RestoreLatestBackup(string archivePath, int keepBackups = DefaultKeepBackups)
    {
        var latest = ListBackups(archivePath).FirstOrDefault()
            ?? throw new FileNotFoundException($"No backups found for {archivePath}.", archivePath);

        // Verify the backup is a readable archive before trusting it.
        _ = LgpArchive.Open(latest.Path);

        // Keep the pre-restore state so the restore itself can be undone.
        CreateBackup(archivePath, DateTime.UtcNow);

        var tempPath = $"{archivePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            File.Copy(latest.Path, tempPath, overwrite: true);
            ReplaceAtomically(tempPath, archivePath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        Prune(archivePath, keepBackups);
        return latest;
    }

    private static string CreateBackup(string archivePath, DateTime nowUtc)
    {
        var stamp = nowUtc.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var backupPath = $"{archivePath}{BackupMarker}{stamp}";
        for (var suffix = 1; File.Exists(backupPath); suffix++)
            backupPath = $"{archivePath}{BackupMarker}{stamp}-{suffix}";

        File.Copy(archivePath, backupPath);
        return backupPath;
    }

    private static void Verify(string tempPath, string fieldName, byte[] expected)
    {
        try
        {
            var reopened = LgpArchive.Open(tempPath);
            var stored = reopened.ReadFile(fieldName);
            if (!stored.AsSpan().SequenceEqual(expected))
                throw new InvalidDataException("written field bytes differ from the requested bytes");

            _ = FieldPC.OpenCompressed(fieldName, stored);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Verification of the written archive failed: {ex.Message}", ex);
        }
    }

    private static void ReplaceAtomically(string tempPath, string destinationPath)
    {
        try
        {
            File.Replace(tempPath, destinationPath, destinationBackupFileName: null);
        }
        catch (PlatformNotSupportedException)
        {
            File.Move(tempPath, destinationPath, overwrite: true);
        }
    }

    private static int Prune(string archivePath, int keep)
    {
        keep = Math.Max(keep, 1);
        var pruned = 0;
        foreach (var old in ListBackups(archivePath).Skip(keep))
        {
            File.Delete(old.Path);
            pruned++;
        }

        return pruned;
    }
}

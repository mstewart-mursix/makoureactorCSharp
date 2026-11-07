using System.IO;
using System.IO.Compression;

namespace MakouReactor.Core.Text;

/// <summary>
/// Reads the international character width table from the game's <c>WINDOW.BIN</c>, the font the
/// engine actually draws dialog text with. The C++ editor sizes windows from this file when it can
/// find it (<c>Data::loadWindowBin</c>, <c>FF7Font::charW</c>) and only falls back to its built-in
/// table otherwise; the built-in table is narrower than the PC font for some glyphs (for example
/// <c>!</c> and <c>?</c>), which clips the last character of a window.
/// </summary>
/// <remarks>
/// <c>WINDOW.BIN</c> is a kernel-style archive: each chunk is <c>u16 compressed size</c>,
/// <c>u16 raw size</c>, <c>u16 type</c>, then gzip data. The third chunk holds the width tables; its
/// first 256 bytes are the international table, one byte per character id (low 5 bits = glyph width,
/// high 3 bits = left padding), the same encoding as <see cref="FF7TextMetrics"/> uses.
/// </remarks>
public static class WindowBinFont
{
    private const int ChunkHeaderSize = 6;
    private const int FontWidthChunk = 2;
    private const int TableSize = 256;

    /// <summary>Extract the international width table, or null when the data is not a usable WINDOW.BIN.</summary>
    public static byte[]? TryParse(ReadOnlySpan<byte> windowBin)
    {
        var position = 0;
        for (var chunk = 0; position + ChunkHeaderSize <= windowBin.Length; chunk++)
        {
            var compressedSize = windowBin[position] | (windowBin[position + 1] << 8);
            var start = position + ChunkHeaderSize;
            if (compressedSize == 0 || start + compressedSize > windowBin.Length)
                return null;

            if (chunk == FontWidthChunk)
            {
                try
                {
                    using var input = new MemoryStream(windowBin.Slice(start, compressedSize).ToArray());
                    using var gzip = new GZipStream(input, CompressionMode.Decompress);
                    var table = new byte[TableSize];
                    var read = 0;
                    while (read < TableSize)
                    {
                        var n = gzip.Read(table, read, TableSize - read);
                        if (n == 0)
                            return null;
                        read += n;
                    }

                    return table;
                }
                catch (InvalidDataException)
                {
                    return null;
                }
            }

            position = start + compressedSize;
        }

        return null;
    }

    /// <summary>
    /// Candidate <c>WINDOW.BIN</c> locations for a PC field archive (<c>data/field/flevel.lgp</c>):
    /// <c>data/kernel</c> (original PC release) and <c>data/lang-*/kernel</c> (Steam release).
    /// </summary>
    public static IEnumerable<string> CandidatePaths(string fieldArchivePath)
    {
        var fieldDir = Path.GetDirectoryName(Path.GetFullPath(fieldArchivePath));
        var dataDir = fieldDir == null ? null : Path.GetDirectoryName(fieldDir);
        if (dataDir == null || !Directory.Exists(dataDir))
            yield break;

        yield return Path.Combine(dataDir, "kernel", "WINDOW.BIN");
        IEnumerable<string> langDirs;
        try
        {
            langDirs = Directory.EnumerateDirectories(dataDir, "lang-*").OrderBy(static d => d, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        // English first: it is the font this editor's text codec targets.
        foreach (var dir in langDirs.OrderBy(static d => Path.GetFileName(d).Equals("lang-en", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            yield return Path.Combine(dir, "kernel", "WINDOW.BIN");
    }

    /// <summary>
    /// Find the game font next to <paramref name="fieldArchivePath"/> and make
    /// <see cref="FF7TextMetrics"/> use it. Returns the path that was loaded, or null (the built-in
    /// table stays in use).
    /// </summary>
    public static string? TryLoadFor(string fieldArchivePath)
    {
        foreach (var candidate in CandidatePaths(fieldArchivePath))
        {
            try
            {
                if (!File.Exists(candidate))
                    continue;

                var table = TryParse(File.ReadAllBytes(candidate));
                if (table == null)
                    continue;

                FF7TextMetrics.UseGameFont(table);
                return candidate;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable: keep looking, then fall back to the built-in table.
            }
        }

        return null;
    }
}

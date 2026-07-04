using System.Buffers.Binary;
using System.Text;

namespace MakouReactor.Core.IO;

internal sealed record Iso9660ImageEntry(string Name, string FullPath, int Size, long Offset);

internal static class Iso9660Image
{
    private const int SectorSize = 2048;
    private const int PrimaryVolumeDescriptorSector = 16;

    public static IReadOnlyList<Iso9660ImageEntry> ListFiles(string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        using var stream = File.OpenRead(imagePath);
        var root = ReadPrimaryVolumeDescriptorRoot(stream);
        var files = new List<Iso9660ImageEntry>();
        ReadDirectory(stream, root.Offset, root.Size, string.Empty, files, cancellationToken);
        return files;
    }

    public static byte[] ReadFile(string imagePath, Iso9660ImageEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();
        var data = new byte[entry.Size];
        using var stream = File.OpenRead(imagePath);
        stream.Position = entry.Offset;
        var read = 0;
        while (read < data.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = stream.Read(data.AsSpan(read));
            if (count == 0)
                throw new EndOfStreamException($"ISO file entry is truncated: {entry.FullPath}");
            read += count;
        }

        return data;
    }

    private static Iso9660ImageEntry ReadPrimaryVolumeDescriptorRoot(Stream stream)
    {
        var descriptor = new byte[SectorSize];
        stream.Position = PrimaryVolumeDescriptorSector * SectorSize;
        ReadExact(stream, descriptor);

        if (descriptor[0] != 1 ||
            Encoding.ASCII.GetString(descriptor, 1, 5) != "CD001")
        {
            throw new InvalidDataException("ISO9660 primary volume descriptor was not found.");
        }

        return ReadDirectoryRecord(descriptor.AsSpan(156), string.Empty);
    }

    private static void ReadDirectory(
        Stream stream,
        long offset,
        int size,
        string path,
        List<Iso9660ImageEntry> files,
        CancellationToken cancellationToken)
    {
        var data = new byte[size];
        stream.Position = offset;
        ReadExact(stream, data);

        var cursor = 0;
        while (cursor < data.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = data[cursor];
            if (length == 0)
            {
                cursor = ((cursor / SectorSize) + 1) * SectorSize;
                continue;
            }

            if (cursor + length > data.Length)
                throw new InvalidDataException("ISO9660 directory record exceeds directory data.");

            var recordStart = cursor;
            var recordSpan = data.AsSpan(recordStart, length);
            var record = ReadDirectoryRecord(recordSpan, path);
            cursor += length + (length % 2);

            if (record.Name is "." or "..")
                continue;

            var flags = recordSpan[25];
            if ((flags & 0x02) != 0)
                ReadDirectory(stream, record.Offset, record.Size, record.FullPath, files, cancellationToken);
            else
                files.Add(record);
        }
    }

    private static Iso9660ImageEntry ReadDirectoryRecord(ReadOnlySpan<byte> record, string parentPath)
    {
        if (record.Length < 34)
            throw new InvalidDataException("ISO9660 directory record is too short.");

        var extent = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(2, 4));
        var size = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(10, 4));
        var nameLength = record[32];
        if (33 + nameLength > record.Length)
            throw new InvalidDataException("ISO9660 directory record name exceeds record size.");

        var nameBytes = record.Slice(33, nameLength);
        var name = nameLength == 1 && nameBytes[0] == 0
            ? "."
            : nameLength == 1 && nameBytes[0] == 1
                ? ".."
                : NormalizeName(Encoding.ASCII.GetString(nameBytes));
        var fullPath = string.IsNullOrEmpty(parentPath) || name is "." or ".."
            ? name
            : $"{parentPath}/{name}";

        return new Iso9660ImageEntry(
            name,
            fullPath,
            checked((int)size),
            checked((long)extent * SectorSize));
    }

    private static string NormalizeName(string name)
    {
        var semicolon = name.IndexOf(';', StringComparison.Ordinal);
        if (semicolon >= 0)
            name = name[..semicolon];
        return name;
    }

    private static void ReadExact(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(buffer[read..]);
            if (count == 0)
                throw new EndOfStreamException("Unexpected end of ISO9660 image.");
            read += count;
        }
    }
}

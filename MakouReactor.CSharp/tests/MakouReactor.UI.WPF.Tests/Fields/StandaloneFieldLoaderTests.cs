using System.IO;
using System.Text;

using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.Fields;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Fields;

public sealed class StandaloneFieldLoaderTests
{
    [Fact]
    public void open_dec_uses_decompressed_field_data()
    {
        var field = StandaloneFieldLoader.Open("md1stin", "md1stin.dec", BuildPcField());

        field.Name.Should().Be("md1stin");
        field.Data.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void open_lzs_uses_compressed_field_data()
    {
        var compressed = LzsCompression.CompressWithHeader(BuildPcField());

        var field = StandaloneFieldLoader.Open("md1stin", "md1stin.lzs", compressed);

        field.Name.Should().Be("md1stin");
        field.Data.ToArray().Should().Equal(BuildPcField());
    }

    [Fact]
    public void open_unknown_extension_falls_back_to_decompressed_when_compressed_open_fails()
    {
        var field = StandaloneFieldLoader.Open("md1stin", "md1stin.bin", BuildPcField());

        field.Name.Should().Be("md1stin");
        field.Data.ToArray().Should().Equal(BuildPcField());
    }

    [Fact]
    public void save_dec_writes_decompressed_field_data_and_marks_field_saved()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "md1stin.dec");
        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField());
        field.SetModified();

        StandaloneFieldLoader.Save(path, field);

        File.ReadAllBytes(path).Should().Equal(field.SaveDecompressed());
        field.IsModified.Should().BeFalse();
    }

    [Fact]
    public void save_lzs_writes_compressed_field_data_that_reopens()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "md1stin.lzs");
        var field = FieldPC.OpenDecompressed("md1stin", BuildPcField());
        field.SetModified();

        StandaloneFieldLoader.Save(path, field);

        var reopened = FieldPC.OpenCompressed("md1stin", File.ReadAllBytes(path));
        reopened.Data.ToArray().Should().Equal(field.Data.ToArray());
        field.IsModified.Should().BeFalse();
    }

    private static byte[] BuildPcField()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new[] { (byte)index })
            .ToArray();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write((ushort)0);
        writer.Write(9u);
        for (var index = 0; index < 9; index++)
            writer.Write(0u);

        var offsets = new uint[9];
        for (var index = 0; index < 9; index++)
        {
            offsets[index] = checked((uint)stream.Position);
            writer.Write((uint)sections[index].Length);
            writer.Write(sections[index]);
        }

        writer.Write("FINAL FANTASY7"u8);

        stream.Position = 6;
        foreach (var offset in offsets)
            writer.Write(offset);

        return stream.ToArray();
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

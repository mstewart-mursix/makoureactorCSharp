using System.IO.Compression;

using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;
using MakouReactor.Core.Text;

using Xunit;

namespace MakouReactor.Tests.Text;

/// <summary>
/// Tests that change which font <see cref="FF7TextMetrics"/> measures with. The font is process-wide,
/// so these run apart from the parallel tests (and always put the built-in font back).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalFontCollection
{
    public const string Name = "Global FF7 font state";
}

[Collection(GlobalFontCollection.Name)]
public sealed class WindowBinFontTests : IDisposable
{
    public void Dispose() => FF7TextMetrics.UseBuiltInFont();

    /// <summary>A WINDOW.BIN-shaped archive: two filler chunks, then the width chunk.</summary>
    private static byte[] BuildWindowBin(byte[] widths)
    {
        using var output = new MemoryStream();
        foreach (var raw in new[] { new byte[64], new byte[32], widths.Concat(new byte[1046]).ToArray() })
        {
            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                gzip.Write(raw);
            var bytes = compressed.ToArray();
            output.Write(BitConverter.GetBytes((ushort)bytes.Length));
            output.Write(BitConverter.GetBytes((ushort)raw.Length));
            output.Write(BitConverter.GetBytes((ushort)0));
            output.Write(bytes);
        }

        return output.ToArray();
    }

    /// <summary>Made-up widths (not game data): every glyph 6 px, '!' (0x01) 9 px.</summary>
    private static byte[] Widths()
    {
        var widths = Enumerable.Repeat((byte)6, 256).ToArray();
        widths[0x01] = 9;
        return widths;
    }

    [Fact]
    public void parse_reads_the_third_chunk_as_the_width_table()
    {
        WindowBinFont.TryParse(BuildWindowBin(Widths())).Should().Equal(Widths());
    }

    [Fact]
    public void parse_rejects_data_that_is_not_a_window_bin()
    {
        WindowBinFont.TryParse(new byte[] { 1, 2, 3 }).Should().BeNull();
        WindowBinFont.TryParse(new byte[64]).Should().BeNull();
    }

    [Fact]
    public void window_width_follows_the_loaded_game_font()
    {
        var text = FF7TextCodec.Encode("Stop!", includeTerminator: false);
        var builtIn = FF7TextMetrics.CalcWindowSize(text).Width;

        FF7TextMetrics.UseGameFont(Widths());

        FF7TextMetrics.UsesGameFont.Should().BeTrue();
        FF7TextMetrics.CalcWindowSize(text).Width.Should().Be(19 + (4 * 6) + 9);
        FF7TextMetrics.UseBuiltInFont();
        FF7TextMetrics.CalcWindowSize(text).Width.Should().Be(builtIn);
    }

    [Fact]
    public void opening_an_archive_loads_the_window_bin_beside_it()
    {
        // <root>/data/field/flevel.lgp with <root>/data/lang-en/kernel/WINDOW.BIN, as in the Steam release.
        var root = Path.Combine(Path.GetTempPath(), "mr_font_" + Guid.NewGuid().ToString("N"));
        try
        {
            var field = Directory.CreateDirectory(Path.Combine(root, "data", "field")).FullName;
            var kernel = Directory.CreateDirectory(Path.Combine(root, "data", "lang-en", "kernel")).FullName;
            File.WriteAllBytes(Path.Combine(kernel, "WINDOW.BIN"), BuildWindowBin(Widths()));
            var archive = Path.Combine(field, "flevel.lgp");
            LgpArchive.Create(archive, [new LgpArchiveFile("testmap", FieldPC.CreateEmpty("testmap").SaveCompressed())]);

            FieldArchive.Open(archive);

            FF7TextMetrics.UsesGameFont.Should().BeTrue();
            FF7TextMetrics.CharFullWidth(0x01).Should().Be(9);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

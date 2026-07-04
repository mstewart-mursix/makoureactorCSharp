using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;
using MakouReactor.Core.Services;
using MakouReactor.UI.WPF.Fields;
using MakouReactor.UI.WPF.Scripts;
using MakouReactor.UI.WPF.ViewModels;

using Xunit;

namespace MakouReactor.UI.WPF.Tests;

public sealed class MainWindowArchiveIntegrationTests
{
    [Fact]
    public void shell_opens_pc_flevel_and_selects_first_plain_script_and_text_heavy_fields()
    {
        RunSta(() =>
        {
            using var temp = new TempDirectory();
            var archivePath = Path.Combine(temp.Path, "flevel.lgp");
            var scriptHeavyField = BuildPcField(
                SectionsWithSection1(BuildSection1(groupCount: 3, ["Map name", "Hello"])));
            var textHeavyField = BuildPcField(
                SectionsWithSection1(BuildSection1(groupCount: 1, ["Map name", "A", "B", "C", "D", "E"])));
            var scriptHeavySection = FieldPC.OpenDecompressed("manyscr", scriptHeavyField).ScriptsAndTexts;
            scriptHeavySection.Should().NotBeNull();
            scriptHeavySection!.GrpScriptCount.Should().Be(3);
            var textHeavySection = FieldPC.OpenDecompressed("manytxt", textHeavyField).ScriptsAndTexts;
            textHeavySection.Should().NotBeNull();
            textHeavySection!.TextCount.Should().Be(6);

            LgpArchive.Create(archivePath,
            [
                new LgpArchiveFile("aaa", LzsCompression.CompressWithHeader(BuildPcField(SimpleSections()))),
                new LgpArchiveFile("manyscr", LzsCompression.CompressWithHeader(scriptHeavyField)),
                new LgpArchiveFile("manytxt", LzsCompression.CompressWithHeader(textHeavyField)),
            ]);

            var window = CreateWindow();
            try
            {
                window.Show();
                Pump(InvokeOpenArchive(window, archivePath));

                var fieldList = Find<FieldListView>(window, "FieldListView");
                fieldList.Items.Count.Should().Be(3);
                Find<TextBlock>(window, "ArchiveLabel").Text.Should().Be("flevel.lgp");

                fieldList.SelectedIndex = 0;
                PumpLayout();
                ((MainWindowViewModel)window.DataContext).CurrentFieldName.Should().Be("aaa");
                Find<MenuItem>(window, "TextsMenuItem").IsEnabled.Should().BeTrue();

                fieldList.SelectedIndex = 1;
                PumpLayout();
                var viewModel = (MainWindowViewModel)window.DataContext;
                viewModel.CurrentFieldName.Should().Be("manyscr");
                var scriptView = Find<ScriptManagerView>(window, "ScriptManagerView");
                scriptView.GroupsSource.Should().BeAssignableTo<IEnumerable<object>>()
                    .Which.Should().HaveCount(3);
                Find<Button>(window, "TextsToolButton").IsEnabled.Should().BeTrue();

                fieldList.SelectedIndex = 2;
                PumpLayout();
                ((MainWindowViewModel)window.DataContext).CurrentFieldName.Should().Be("manytxt");
                Find<MenuItem>(window, "TextsMenuItem").IsEnabled.Should().BeTrue();
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static MainWindow CreateWindow()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            "MakouReactor.WpfTests",
            Guid.NewGuid().ToString("N"),
            "settings.json");
        return new MainWindow(new ShellSessionService(new JsonAppSettingsService(settingsPath)));
    }

    private static Task InvokeOpenArchive(MainWindow window, string archivePath)
    {
        var method = typeof(MainWindow).GetMethod(
            "OpenArchiveAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (Task)method!.Invoke(window, [archivePath])!;
    }

    private static T Find<T>(MainWindow window, string name)
        where T : class
    {
        window.FindName(name).Should().BeOfType<T>();
        return (T)window.FindName(name);
    }

    private static void Pump(Task task)
    {
        while (!task.IsCompleted)
        {
            Dispatcher.CurrentDispatcher.Invoke(
                DispatcherPriority.Background,
                new Action(() => { }));
            Thread.Sleep(1);
        }

        task.GetAwaiter().GetResult();
        PumpLayout();
    }

    private static void PumpLayout()
    {
        Dispatcher.CurrentDispatcher.Invoke(
            DispatcherPriority.Background,
            new Action(() => { }));
    }

    private static byte[][] SimpleSections() =>
        Enumerable.Range(1, 9).Select(index => new[] { (byte)index }).ToArray();

    private static byte[][] SectionsWithSection1(byte[] section1)
    {
        var sections = SimpleSections();
        sections[0] = section1;
        return sections;
    }

    private static byte[] BuildPcField(IReadOnlyList<byte[]> sections)
    {
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

    private static byte[] BuildSection1(int groupCount, IReadOnlyList<string> texts)
    {
        const int scriptCount = 32;
        var headerSize = 32;
        var scriptOffsetsOffset = headerSize + (groupCount * 8);
        var scriptDataOffset = scriptOffsetsOffset + (groupCount * scriptCount * 2);
        var scripts = Enumerable.Range(0, groupCount)
            .Select(index => new byte[] { (byte)index, 0x00 })
            .ToArray();
        var posText = scriptDataOffset + scripts.Sum(static script => script.Length);
        var textSection = BuildTextSection(texts);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)0x0502);
        writer.Write((byte)groupCount);
        writer.Write((byte)1);
        writer.Write((ushort)posText);
        writer.Write((ushort)0);
        writer.Write((ushort)512);
        writer.Write(new byte[6]);
        WriteFixed(writer, "makou", 8);
        WriteFixed(writer, "md1stin", 8);
        for (var group = 0; group < groupCount; group++)
            WriteFixed(writer, $"grp{group}", 8);

        for (var group = 0; group < groupCount; group++)
        {
            var scriptOffset = checked((ushort)(scriptDataOffset + scripts.Take(group).Sum(static script => script.Length)));
            var textOffset = checked((ushort)posText);
            writer.Write(scriptOffset);
            for (var script = 1; script < scriptCount; script++)
                writer.Write(textOffset);
        }

        foreach (var script in scripts)
            writer.Write(script);
        writer.Write(textSection);

        return stream.ToArray();
    }

    private static byte[] BuildTextSection(IReadOnlyList<string> texts)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write((ushort)texts.Count);
        var offset = checked((ushort)(2 + (texts.Count * 2)));
        foreach (var text in texts)
        {
            writer.Write(offset);
            offset += checked((ushort)(Encoding.ASCII.GetByteCount(text) + 1));
        }

        foreach (var text in texts)
        {
            writer.Write(Encoding.ASCII.GetBytes(text));
            writer.Write((byte)0xFF);
        }

        return stream.ToArray();
    }

    private static void WriteFixed(BinaryWriter writer, string value, int length)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        writer.Write(bytes);
        writer.Write(new byte[length - bytes.Length]);
    }

    private static void RunSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MakouReactor.WpfTests",
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

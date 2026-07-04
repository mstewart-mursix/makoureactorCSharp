using System.IO;
using System.Threading;
using System.Windows;

using FluentAssertions;

using MakouReactor.UI.WPF.Services;
using MakouReactor.UI.WPF.Shared;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Services;

public sealed class ArchiveSaveAsServiceTests
{
    [Fact]
    public void pick_destination_uses_lgp_save_dialog_options()
    {
        var picker = new RecordingFilePicker("C:\\ff7\\copy.lgp");
        var service = new ArchiveSaveAsService(picker);

        var destination = service.PickDestination(owner: null!, "C:\\ff7\\flevel.lgp");

        destination.Should().Be("C:\\ff7\\copy.lgp");
        picker.SaveOptions.Should().Be(new FilePickerOptions(
            "Save field archive as",
            FileDialogFilters.SaveLgpArchive,
            "flevel.lgp",
            ".lgp",
            AddExtension: true));
    }

    [Fact]
    public void copy_archive_writes_new_archive_and_overwrites_existing_destination()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "flevel.lgp");
        var destinationPath = Path.Combine(temp.Path, "copy.lgp");
        File.WriteAllBytes(sourcePath, [0x4C, 0x47, 0x50]);
        File.WriteAllBytes(destinationPath, [0x00]);
        var service = new ArchiveSaveAsService(new RecordingFilePicker(null));

        service.CopyArchive(sourcePath, destinationPath);

        File.ReadAllBytes(destinationPath).Should().Equal([0x4C, 0x47, 0x50]);
    }

    [Fact]
    public void copy_archive_honors_cancelled_token()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "flevel.lgp");
        var destinationPath = Path.Combine(temp.Path, "copy.lgp");
        File.WriteAllBytes(sourcePath, [0x4C, 0x47, 0x50]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new ArchiveSaveAsService(new RecordingFilePicker(null));

        var copy = () => service.CopyArchive(sourcePath, destinationPath, cancellation.Token);

        copy.Should().Throw<OperationCanceledException>();
        File.Exists(destinationPath).Should().BeFalse();
    }

    private sealed class RecordingFilePicker : IFilePickerService
    {
        private readonly string? _saveResult;

        public RecordingFilePicker(string? saveResult)
        {
            _saveResult = saveResult;
        }

        public FilePickerOptions? SaveOptions { get; private set; }

        public string? OpenFile(Window owner, FilePickerOptions options) => null;

        public string? SaveFile(Window owner, FilePickerOptions options)
        {
            SaveOptions = options;
            return _saveResult;
        }

        public string? OpenFolder(Window owner, string title) => null;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
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

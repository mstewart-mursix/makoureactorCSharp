using System.IO;
using System.Threading;
using System.Windows;

using MakouReactor.UI.WPF.Shared;

namespace MakouReactor.UI.WPF.Services;

public interface IArchiveSaveAsService
{
    string? PickDestination(Window owner, string sourceArchivePath);
    void CopyArchive(string sourceArchivePath, string destinationArchivePath, CancellationToken cancellationToken = default);
}

public sealed class ArchiveSaveAsService : IArchiveSaveAsService
{
    private readonly IFilePickerService _filePickerService;

    public ArchiveSaveAsService(IFilePickerService filePickerService)
    {
        _filePickerService = filePickerService;
    }

    public string? PickDestination(Window owner, string sourceArchivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceArchivePath);

        return _filePickerService.SaveFile(owner,
            new FilePickerOptions(
                "Save field archive as",
                FileDialogFilters.SaveLgpArchive,
                Path.GetFileName(sourceArchivePath),
                ".lgp",
                AddExtension: true));
    }

    public void CopyArchive(
        string sourceArchivePath,
        string destinationArchivePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceArchivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationArchivePath);
        cancellationToken.ThrowIfCancellationRequested();

        File.Copy(sourceArchivePath, destinationArchivePath, overwrite: true);
        cancellationToken.ThrowIfCancellationRequested();
    }
}

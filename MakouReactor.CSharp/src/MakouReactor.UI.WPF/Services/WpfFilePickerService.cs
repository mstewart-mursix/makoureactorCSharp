using System.Windows;

using Microsoft.Win32;

namespace MakouReactor.UI.WPF.Services;

public sealed class WpfFilePickerService : IFilePickerService
{
    public string? OpenFile(Window owner, FilePickerOptions options)
    {
        var dialog = new OpenFileDialog
        {
            Title = options.Title,
            Filter = options.Filter,
            FileName = options.FileName ?? string.Empty,
            DefaultExt = options.DefaultExtension ?? string.Empty,
            AddExtension = options.AddExtension,
        };

        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? SaveFile(Window owner, FilePickerOptions options)
    {
        var dialog = new SaveFileDialog
        {
            Title = options.Title,
            Filter = options.Filter,
            FileName = options.FileName ?? string.Empty,
            DefaultExt = options.DefaultExtension ?? string.Empty,
            AddExtension = options.AddExtension,
        };

        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? OpenFolder(Window owner, string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
        };

        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }
}

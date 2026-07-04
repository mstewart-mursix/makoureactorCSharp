using System.Windows;

namespace MakouReactor.UI.WPF.Services;

public interface IFilePickerService
{
    string? OpenFile(Window owner, FilePickerOptions options);
    string? SaveFile(Window owner, FilePickerOptions options);
    string? OpenFolder(Window owner, string title);
}

public sealed record FilePickerOptions(
    string Title,
    string Filter,
    string? FileName = null,
    string? DefaultExtension = null,
    bool AddExtension = false);

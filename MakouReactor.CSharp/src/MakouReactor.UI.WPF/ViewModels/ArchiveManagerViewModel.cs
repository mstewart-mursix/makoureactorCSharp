using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

namespace MakouReactor.UI.WPF.ViewModels;

public sealed partial class ArchiveManagerViewModel : ObservableObject
{
    public ObservableCollection<ArchiveEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    private ArchiveEntryViewModel? _selectedEntry;

    [ObservableProperty]
    private string _previewText = "Archive preview will appear here.";
}

public sealed record ArchiveEntryViewModel(string Name, string FullPath, string Directory, int Size);

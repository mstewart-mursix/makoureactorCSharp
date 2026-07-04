using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

namespace MakouReactor.UI.WPF.ViewModels;

public sealed partial class FieldListViewModel : ObservableObject
{
    public ObservableCollection<FieldListEntryViewModel> Fields { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private FieldListEntryViewModel? _selectedField;
}

public sealed record FieldListEntryViewModel(int Id, string Name, int Size, string Status);

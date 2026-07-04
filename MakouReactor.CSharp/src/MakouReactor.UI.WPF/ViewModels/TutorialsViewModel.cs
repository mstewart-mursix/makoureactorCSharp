using CommunityToolkit.Mvvm.ComponentModel;

using MakouReactor.Core.Models;

namespace MakouReactor.UI.WPF.ViewModels;

public sealed partial class TutorialsViewModel : ObservableObject
{
    private readonly IReadOnlyList<TutorialEntry> _allEntries;

    [ObservableProperty]
    private bool _showMusic = true;

    [ObservableProperty]
    private bool _showTutorials = true;

    [ObservableProperty]
    private IReadOnlyList<TutorialEntry> _entries;

    [ObservableProperty]
    private TutorialEntry? _selectedEntry;

    public TutorialsViewModel(string fieldName, TutorialFile tutorialFile)
    {
        FieldName = fieldName;
        _allEntries = tutorialFile.Entries;
        _entries = _allEntries;
        SelectedEntry = Entries.FirstOrDefault();
    }

    public string FieldName { get; }

    public bool HasSelectedEntry => SelectedEntry != null;

    public string StatusText =>
        $"{Entries.Count} of {_allEntries.Count} entr{(_allEntries.Count == 1 ? "y" : "ies")} shown. " +
        $"{_allEntries.Count(entry => entry.Kind == TutorialEntryKind.Music)} music, " +
        $"{_allEntries.Count(entry => entry.Kind == TutorialEntryKind.Tutorial)} tutorial.";

    public string KindText => SelectedEntry == null ? string.Empty : $"Type: {SelectedEntry.Kind}";

    public string SizeText => SelectedEntry == null ? string.Empty : $"Size: {SelectedEntry.Size:N0} bytes";

    public string MusicIdText => SelectedEntry?.MusicId.HasValue == true ? $"Music ID: {SelectedEntry.MusicId}" : "Music ID:";

    public string BrokenText => SelectedEntry == null ? string.Empty : SelectedEntry.Broken ? "Broken: yes" : "Broken: no";

    public string DescriptionText => SelectedEntry?.Description ?? string.Empty;

    partial void OnShowMusicChanged(bool value)
    {
        ApplyFilter();
    }

    partial void OnShowTutorialsChanged(bool value)
    {
        ApplyFilter();
    }

    partial void OnSelectedEntryChanged(TutorialEntry? value)
    {
        OnPropertyChanged(nameof(HasSelectedEntry));
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(MusicIdText));
        OnPropertyChanged(nameof(BrokenText));
        OnPropertyChanged(nameof(DescriptionText));
    }

    private void ApplyFilter()
    {
        Entries = _allEntries
            .Where(entry =>
                (ShowMusic && entry.Kind == TutorialEntryKind.Music) ||
                (ShowTutorials && entry.Kind == TutorialEntryKind.Tutorial))
            .ToArray();
        SelectedEntry = Entries.FirstOrDefault();
        OnPropertyChanged(nameof(StatusText));
    }
}

using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using MakouReactor.Core.Models;

namespace MakouReactor.UI.WPF.ViewModels;

public sealed partial class PlayStationSectionManagerViewModel : ObservableObject
{
    private readonly FieldPS _field;

    [ObservableProperty]
    private IReadOnlyList<PlayStationSectionEntryViewModel> _sections;

    [ObservableProperty]
    private PlayStationSectionEntryViewModel? _selectedSection;

    public PlayStationSectionManagerViewModel(FieldPS field)
    {
        _field = field;
        FieldName = field.Name;
        _sections = BuildSections();
        SelectedSection = Sections.FirstOrDefault();
    }

    public string FieldName { get; }

    public string StatusText =>
        $"{Sections.Count} PlayStation DAT section{(Sections.Count == 1 ? string.Empty : "s")} loaded" +
        $"{(IsModified ? "; unsaved section changes" : string.Empty)}.";

    public bool IsModified => _field.IsModified;

    public bool HasSelectedSection => SelectedSection != null;

    public string PreviewText
    {
        get
        {
            if (SelectedSection == null)
                return "Select a PlayStation DAT section.";

            var section = _field.Sections.First(item => item.Index == SelectedSection.Index);
            var bytes = _field.GetSectionData(section.Section);
            var preview = string.Join(" ",
                bytes.Take(128).Select(static value => value.ToString("X2", CultureInfo.InvariantCulture)));
            if (bytes.Length > 128)
                preview += " ...";

            return $"{SelectedSection.Name} section\n" +
                   $"Stored offset: {SelectedSection.StoredOffsetHex}\n" +
                   $"Data offset: {SelectedSection.DataOffsetHex}\n" +
                   $"Size: {SelectedSection.Size} bytes\n" +
                   $"Parser: {SelectedSection.ParserStatus}\n\n" +
                   preview;
        }
    }

    partial void OnSelectedSectionChanged(PlayStationSectionEntryViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedSection));
        OnPropertyChanged(nameof(PreviewText));
    }

    public byte[] ExportSelectedSection()
    {
        if (SelectedSection == null)
            throw new InvalidOperationException("No PlayStation DAT section is selected.");

        var section = _field.Sections.First(item => item.Index == SelectedSection.Index);
        return _field.GetSectionData(section.Section);
    }

    public void ReplaceSelectedSection(ReadOnlySpan<byte> data)
    {
        if (SelectedSection == null)
            throw new InvalidOperationException("No PlayStation DAT section is selected.");

        var selectedIndex = SelectedSection.Index;
        var section = _field.Sections.First(item => item.Index == selectedIndex);
        _field.SetSectionData(section.Section, data);
        Sections = BuildSections();
        SelectedSection = Sections.First(item => item.Index == selectedIndex);
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PreviewText));
    }

    private IReadOnlyList<PlayStationSectionEntryViewModel> BuildSections() =>
        _field.Sections
            .Select(section => new PlayStationSectionEntryViewModel(
                section.Index,
                section.Section.ToString(),
                section.StoredOffset,
                section.DataOffset,
                section.Size,
                ParserStatus(_field, section.Section)))
            .ToArray();

    private static string ParserStatus(FieldPS field, FieldSection section) => section switch
    {
        FieldSection.Scripts => field.ScriptsAndTexts == null ? "Raw only" : "Parsed scripts/texts",
        FieldSection.Walkmesh => field.Walkmesh == null ? "Raw only" : "Parsed walkmesh",
        FieldSection.Inf => field.Inf == null ? "Raw only" : "Parsed INF",
        FieldSection.Encounter => field.Encounters == null ? "Raw only" : "Parsed encounters",
        FieldSection.ModelLoader => field.ModelLoaderPS == null ? "Raw only" : "Parsed model loader",
        FieldSection.Background => "Raw background data",
        FieldSection.Camera => "Raw camera data",
        _ => "Raw only",
    };
}

public sealed record PlayStationSectionEntryViewModel(
    int Index,
    string Name,
    int StoredOffset,
    int DataOffset,
    int Size,
    string ParserStatus)
{
    public string StoredOffsetHex => $"0x{StoredOffset:X}";
    public string DataOffsetHex => $"0x{DataOffset:X}";
}

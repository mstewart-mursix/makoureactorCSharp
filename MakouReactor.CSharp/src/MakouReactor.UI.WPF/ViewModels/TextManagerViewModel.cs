using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using MakouReactor.Core.Models;

namespace MakouReactor.UI.WPF.ViewModels;

public sealed partial class TextManagerViewModel : ObservableObject
{
    public TextManagerViewModel(string fieldName, IEnumerable<FF7String> texts)
    {
        FieldName = fieldName;
        Texts = new ObservableCollection<TextEntryViewModel>(
            texts.Select((text, index) => new TextEntryViewModel(index, text.Value)));
    }

    public string FieldName { get; }

    public ObservableCollection<TextEntryViewModel> Texts { get; }

    [ObservableProperty]
    private TextEntryViewModel? _selectedText;

    public IReadOnlyList<FF7String> ToFF7Strings() =>
        Texts.Select(static text => new FF7String(text.Value)).ToArray();

    public TextEntryViewModel InsertText(int index, string value)
    {
        index = Math.Clamp(index, 0, Texts.Count);
        var item = new TextEntryViewModel(index, value);
        Texts.Insert(index, item);
        RenumberTexts();
        return item;
    }

    public void RemoveTextAt(int index)
    {
        if (index < 0 || index >= Texts.Count)
            return;

        Texts.RemoveAt(index);
        RenumberTexts();
    }

    private void RenumberTexts()
    {
        for (var index = 0; index < Texts.Count; index++)
            Texts[index].Id = index;
    }
}

public sealed partial class TextEntryViewModel(int id, string value) : ObservableObject
{
    [ObservableProperty]
    private int _id = id;

    [ObservableProperty]
    private string _value = value;

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(Length));
        OnPropertyChanged(nameof(Preview));
    }

    public int Length => Value.Length;

    public string Preview => string.IsNullOrEmpty(Value)
        ? "(empty)"
        : Value.Length > 48
            ? string.Concat(Value.AsSpan(0, 48), "...")
            : Value;
}

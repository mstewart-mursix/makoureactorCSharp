using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

namespace MakouReactor.UI.WPF.Texts;

public partial class TextManagerDialog : Window
{
    private readonly TextManagerViewModel _viewModel;

    public event Action<int, string>? TextApplied;
    public event Action<int, string>? TextInserted;
    public event Action<int>? TextDeleted;

    public TextManagerDialog(string fieldName, Section1File section, int? selectedTextIndex = null)
    {
        InitializeComponent();

        _viewModel = new TextManagerViewModel(fieldName, section.Texts);

        FieldLabel.Text = _viewModel.FieldName;
        TextListView.ItemsSource = _viewModel.Texts;
        TokenComboBox.SelectedIndex = 0;
        StatusLabel.Text = $"{_viewModel.Texts.Count} text entr{(_viewModel.Texts.Count == 1 ? "y" : "ies")} loaded.";
        UpdateTextListActions();
        if (_viewModel.Texts.Count > 0)
            SelectText(selectedTextIndex.GetValueOrDefault());
        else
            ShowText(null);
    }

    public void SelectText(int index)
    {
        if (index < 0 || index >= _viewModel.Texts.Count)
            return;

        TextListView.SelectedIndex = index;
        TextListView.ScrollIntoView(_viewModel.Texts[index]);
        UpdateTextListActions();
    }

    private void TextListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowText(TextListView.SelectedItem as TextEntryViewModel);
        UpdateTextListActions();
    }

    private void AddText_Click(object sender, RoutedEventArgs e)
    {
        var index = TextListView.SelectedIndex >= 0
            ? TextListView.SelectedIndex + 1
            : _viewModel.Texts.Count;
        var text = _viewModel.InsertText(index, string.Empty);
        TextInserted?.Invoke(index, text.Value);
        TextListView.Items.Refresh();
        SelectText(index);
        TextEditor.Focus();
        StatusLabel.Text = $"Inserted text {index}.";
        UpdateTextListActions();
    }

    private void RemoveText_Click(object sender, RoutedEventArgs e)
    {
        var index = TextListView.SelectedIndex;
        if (index < 0 || index >= _viewModel.Texts.Count)
        {
            StatusLabel.Text = "No text selected.";
            return;
        }

        TextDeleted?.Invoke(index);
        _viewModel.RemoveTextAt(index);
        TextListView.Items.Refresh();
        if (_viewModel.Texts.Count == 0)
            ShowText(null);
        else
            SelectText(Math.Min(index, _viewModel.Texts.Count - 1));

        StatusLabel.Text = $"Removed text {index}.";
        UpdateTextListActions();
    }

    private void ApplyText_Click(object sender, RoutedEventArgs e)
    {
        if (TextListView.SelectedItem is not TextEntryViewModel text)
        {
            StatusLabel.Text = "No text selected.";
            return;
        }

        var value = TextEditor.Text;
        var unsupportedTokens = FF7TextCodec.FindUnsupportedTokens(value);
        if (unsupportedTokens.Count > 0)
        {
            StatusLabel.Text = $"Unsupported token(s): {string.Join(", ", unsupportedTokens)}";
            return;
        }

        var lineIssues = TextLineValidator.Validate(value);
        if (lineIssues.Count > 0)
        {
            StatusLabel.Text = TextLineValidator.Format(lineIssues);
            return;
        }

        TextApplied?.Invoke(text.Id, value);
        text.Value = value;
        TextListView.Items.Refresh();
        PreviewText.Text = string.IsNullOrEmpty(value) ? "(empty)" : value;
        StatusLabel.Text = $"Applied text {text.Id} - remember to save the field archive.";
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        SearchNext();
        e.Handled = true;
    }

    private void SearchNext_Click(object sender, RoutedEventArgs e)
    {
        SearchNext();
    }

    private void InsertToken_Click(object sender, RoutedEventArgs e)
    {
        if (TokenComboBox.SelectedItem is not ComboBoxItem { Tag: string token })
        {
            StatusLabel.Text = "Select a token to insert.";
            return;
        }

        var insertion = token is "{NEW PAGE}" or "{NEW PAGE 2}"
            ? $"{Environment.NewLine}{token}{Environment.NewLine}"
            : token;
        var start = TextEditor.SelectionStart;
        TextEditor.SelectedText = insertion;
        TextEditor.SelectionStart = start + insertion.Length;
        TextEditor.SelectionLength = 0;
        TextEditor.Focus();
        PreviewText.Text = string.IsNullOrEmpty(TextEditor.Text) ? "(empty)" : TextEditor.Text;
        StatusLabel.Text = $"Inserted {token}.";
    }

    private void SearchNext()
    {
        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            StatusLabel.Text = "Enter text to search for.";
            return;
        }

        if (_viewModel.Texts.Count == 0)
        {
            StatusLabel.Text = "No text entries are loaded.";
            return;
        }

        var start = TextListView.SelectedIndex < 0
            ? 0
            : (TextListView.SelectedIndex + 1) % _viewModel.Texts.Count;
        for (var offset = 0; offset < _viewModel.Texts.Count; offset++)
        {
            var index = (start + offset) % _viewModel.Texts.Count;
            if (!_viewModel.Texts[index].Value.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            SelectText(index);
            StatusLabel.Text = $"Found '{query}' in text {index}.";
            return;
        }

        StatusLabel.Text = $"No text entries contain '{query}'.";
    }

    private void ShowText(TextEntryViewModel? text)
    {
        if (text == null)
        {
            TextEditor.Text = string.Empty;
            ApplyTextButton.IsEnabled = false;
            PreviewText.Text = "No text selected.";
            StatusLabel.Text = "No text selected.";
            return;
        }

        TextEditor.Text = text.Value;
        ApplyTextButton.IsEnabled = true;
        PreviewText.Text = string.IsNullOrEmpty(text.Value)
            ? "(empty)"
            : text.Value;
        StatusLabel.Text = $"Selected text {text.Id} - {text.Length} character(s).";
    }

    private void UpdateTextListActions()
    {
        AddTextButton.IsEnabled = _viewModel.Texts.Count < Section1File.MaxTextCount;
        RemoveTextButton.IsEnabled = TextListView.SelectedIndex >= 0 && _viewModel.Texts.Count > 0;
    }
}

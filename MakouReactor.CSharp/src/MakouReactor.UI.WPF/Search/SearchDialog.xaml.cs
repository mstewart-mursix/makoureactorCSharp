using System.Windows;
using System.Windows.Input;

using MakouReactor.Core.Models;
using MakouReactor.Core.Services;

namespace MakouReactor.UI.WPF.Search;

public partial class SearchDialog : Window
{
    private readonly string _fieldName;
    private readonly Section1File _section;
    private readonly Func<string, IReadOnlyList<FieldSearchResult>>? _searchAllFields;
    private readonly FieldSearchService _searchService = new();

    public event Action<FieldSearchResult>? ResultActivated;

    public SearchDialog(
        string fieldName,
        Section1File section,
        Func<string, IReadOnlyList<FieldSearchResult>>? searchAllFields = null)
    {
        InitializeComponent();
        _fieldName = fieldName;
        _section = section;
        _searchAllFields = searchAllFields;
        AllFieldsCheckBox.IsEnabled = searchAllFields != null;
        ScopeLabel.Text = searchAllFields == null
            ? $"Current field: {fieldName}"
            : $"Current field: {fieldName}";
        QueryBox.Focus();
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        await SearchAsync();
    }

    private async void QueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        await SearchAsync();
        e.Handled = true;
    }

    private void ResultsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsListView.SelectedItem is not FieldSearchResult result)
            return;

        ResultActivated?.Invoke(result);
        StatusLabel.Text = $"Opened {result.Location}.";
    }

    private async Task SearchAsync()
    {
        var query = QueryBox.Text;
        IReadOnlyList<FieldSearchResult> results;
        IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        StatusLabel.Text = AllFieldsCheckBox.IsChecked == true
            ? "Searching all fields..."
            : "Searching current field...";

        try
        {
            results = AllFieldsCheckBox.IsChecked == true && _searchAllFields != null
                ? await Task.Run(() => _searchAllFields(query))
                : _searchService.SearchCurrentField(_fieldName, _section, query);
        }
        finally
        {
            IsEnabled = true;
            Mouse.OverrideCursor = null;
        }

        ResultsListView.ItemsSource = results;
        StatusLabel.Text = string.IsNullOrWhiteSpace(query)
            ? "Enter a search query."
            : $"{results.Count} result{(results.Count == 1 ? string.Empty : "s")} found.";
    }
}

using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MakouReactor.UI.WPF.Archive;

public partial class ArchiveManagerView : UserControl
{
    public ArchiveManagerView()
    {
        InitializeComponent();
    }

    public event SelectionChangedEventHandler? SelectionChanged;
    public event EventHandler<ArchiveColumnHeaderClickedEventArgs>? ColumnHeaderClicked;
    public event RoutedEventHandler? ExtractCurrentRequested;
    public event RoutedEventHandler? ExtractAllRequested;
    public event RoutedEventHandler? ReplaceCurrentRequested;
    public event RoutedEventHandler? AddRequested;
    public event RoutedEventHandler? RemoveCurrentRequested;
    public event RoutedEventHandler? RenameCurrentRequested;

    public IEnumerable? ItemsSource
    {
        get => EntriesList.ItemsSource;
        set => EntriesList.ItemsSource = value;
    }

    public object? SelectedItem
    {
        get => EntriesList.SelectedItem;
        set => EntriesList.SelectedItem = value;
    }

    public IReadOnlyList<string> ColumnHeaders =>
    [
        "Name",
        "Path",
        "Directory",
        "Size",
    ];

    public bool ExtractCurrentEnabled
    {
        get => ExtractCurrentButton.IsEnabled;
        set => ExtractCurrentButton.IsEnabled = value;
    }

    public bool ExtractAllEnabled
    {
        get => ExtractAllButton.IsEnabled;
        set => ExtractAllButton.IsEnabled = value;
    }

    public bool ReplaceCurrentEnabled
    {
        get => ReplaceCurrentButton.IsEnabled;
        set => ReplaceCurrentButton.IsEnabled = value;
    }

    public bool AddEnabled
    {
        get => AddButton.IsEnabled;
        set => AddButton.IsEnabled = value;
    }

    public bool RemoveCurrentEnabled
    {
        get => RemoveCurrentButton.IsEnabled;
        set => RemoveCurrentButton.IsEnabled = value;
    }

    public bool RenameCurrentEnabled
    {
        get => RenameCurrentButton.IsEnabled;
        set => RenameCurrentButton.IsEnabled = value;
    }

    public ImageSource? PreviewImageSource
    {
        get => PreviewImage.Source;
        set => PreviewImage.Source = value;
    }

    public Visibility PreviewImageVisibility
    {
        get => PreviewImage.Visibility;
        set => PreviewImage.Visibility = value;
    }

    public Visibility PreviewTextVisibility
    {
        get => PreviewLabel.Visibility;
        set => PreviewLabel.Visibility = value;
    }

    public string PreviewText
    {
        get => PreviewLabel.Text;
        set => PreviewLabel.Text = value;
    }

    private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectionChanged?.Invoke(this, e);

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is GridViewColumnHeader { Tag: string propertyName })
            ColumnHeaderClicked?.Invoke(this, new ArchiveColumnHeaderClickedEventArgs(propertyName));
    }

    private void ExtractCurrentButton_Click(object sender, RoutedEventArgs e) =>
        ExtractCurrentRequested?.Invoke(this, e);

    private void ExtractAllButton_Click(object sender, RoutedEventArgs e) =>
        ExtractAllRequested?.Invoke(this, e);

    private void ReplaceCurrentButton_Click(object sender, RoutedEventArgs e) =>
        ReplaceCurrentRequested?.Invoke(this, e);

    private void AddButton_Click(object sender, RoutedEventArgs e) =>
        AddRequested?.Invoke(this, e);

    private void RemoveCurrentButton_Click(object sender, RoutedEventArgs e) =>
        RemoveCurrentRequested?.Invoke(this, e);

    private void RenameCurrentButton_Click(object sender, RoutedEventArgs e) =>
        RenameCurrentRequested?.Invoke(this, e);
}

public sealed class ArchiveColumnHeaderClickedEventArgs(string propertyName) : EventArgs
{
    public string PropertyName { get; } = propertyName;
}

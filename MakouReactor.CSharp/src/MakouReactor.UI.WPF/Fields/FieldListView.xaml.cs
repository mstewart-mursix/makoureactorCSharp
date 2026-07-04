using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections;

namespace MakouReactor.UI.WPF.Fields;

public partial class FieldListView : UserControl
{
    public FieldListView()
    {
        InitializeComponent();
    }

    public event SelectionChangedEventHandler? SelectionChanged;
    public event TextChangedEventHandler? SearchTextChanged;
    public event RoutedEventHandler? SearchGotFocus;
    public event EventHandler<FieldColumnHeaderClickedEventArgs>? ColumnHeaderClicked;
    public event EventHandler? FieldContextMenuOpened;
    public event EventHandler? OpenSelectedFieldRequested;
    public event EventHandler? ImportSelectedFieldRequested;
    public event EventHandler? ExportSelectedFieldRequested;
    public event EventHandler? CreateFieldRequested;
    public event EventHandler? DeleteSelectedFieldRequested;
    public event EventHandler? RenameSelectedFieldRequested;

    public IEnumerable? ItemsSource
    {
        get => List.ItemsSource;
        set => List.ItemsSource = value;
    }

    public object? SelectedItem
    {
        get => List.SelectedItem;
        set => List.SelectedItem = value;
    }

    public int SelectedIndex
    {
        get => List.SelectedIndex;
        set => List.SelectedIndex = value;
    }

    public ItemCollection Items => List.Items;

    public IReadOnlyList<string> ColumnHeaders =>
    [
        "File",
        "Id",
        "Status",
    ];

    public bool HasCreateDeleteToolbar => CreateFieldButton != null && DeleteFieldButton != null;

    public string SearchText
    {
        get => SearchBox.Text;
        set => SearchBox.Text = value;
    }

    public bool OpenContextActionEnabled
    {
        get => OpenFieldContextMenuItem.IsEnabled;
        set => OpenFieldContextMenuItem.IsEnabled = value;
    }

    public bool ImportContextActionEnabled
    {
        get => ImportFieldContextMenuItem.IsEnabled;
        set => ImportFieldContextMenuItem.IsEnabled = value;
    }

    public bool ExportContextActionEnabled
    {
        get => ExportFieldContextMenuItem.IsEnabled;
        set => ExportFieldContextMenuItem.IsEnabled = value;
    }

    public bool DeleteFieldActionEnabled
    {
        get => DeleteFieldButton.IsEnabled;
        set
        {
            DeleteFieldButton.IsEnabled = value;
            DeleteFieldContextMenuItem.IsEnabled = value;
        }
    }

    public bool CreateFieldActionEnabled
    {
        get => CreateFieldButton.IsEnabled;
        set
        {
            CreateFieldButton.IsEnabled = value;
            CreateFieldContextMenuItem.IsEnabled = value;
        }
    }

    public bool RenameFieldActionEnabled
    {
        get => RenameFieldContextMenuItem.IsEnabled;
        set => RenameFieldContextMenuItem.IsEnabled = value;
    }

    public void ScrollIntoView(object item) => List.ScrollIntoView(item);

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e) =>
        SearchGotFocus?.Invoke(this, e);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        SearchTextChanged?.Invoke(this, e);

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectionChanged?.Invoke(this, e);

    private void List_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);
        if (item == null)
            return;

        item.IsSelected = true;
        item.Focus();
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e) =>
        FieldContextMenuOpened?.Invoke(this, EventArgs.Empty);

    private void OpenFieldContextMenuItem_Click(object sender, RoutedEventArgs e) =>
        OpenSelectedFieldRequested?.Invoke(this, EventArgs.Empty);

    private void ImportFieldContextMenuItem_Click(object sender, RoutedEventArgs e) =>
        ImportSelectedFieldRequested?.Invoke(this, EventArgs.Empty);

    private void ExportFieldContextMenuItem_Click(object sender, RoutedEventArgs e) =>
        ExportSelectedFieldRequested?.Invoke(this, EventArgs.Empty);

    private void CreateFieldButton_Click(object sender, RoutedEventArgs e) =>
        CreateFieldRequested?.Invoke(this, EventArgs.Empty);

    private void CreateFieldContextMenuItem_Click(object sender, RoutedEventArgs e) =>
        CreateFieldRequested?.Invoke(this, EventArgs.Empty);

    private void DeleteFieldButton_Click(object sender, RoutedEventArgs e) =>
        DeleteSelectedFieldRequested?.Invoke(this, EventArgs.Empty);

    private void DeleteFieldContextMenuItem_Click(object sender, RoutedEventArgs e) =>
        DeleteSelectedFieldRequested?.Invoke(this, EventArgs.Empty);

    private void RenameFieldContextMenuItem_Click(object sender, RoutedEventArgs e) =>
        RenameSelectedFieldRequested?.Invoke(this, EventArgs.Empty);

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is GridViewColumnHeader { Tag: string propertyName })
            ColumnHeaderClicked?.Invoke(this, new FieldColumnHeaderClickedEventArgs(propertyName));
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}

public sealed class FieldColumnHeaderClickedEventArgs(string propertyName) : EventArgs
{
    public string PropertyName { get; } = propertyName;
}

using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MakouReactor.UI.WPF.Scripts;

public partial class ScriptManagerView : UserControl
{
    public ScriptManagerView()
    {
        InitializeComponent();
    }

    public event SelectionChangedEventHandler? GroupSelectionChanged;
    public event SelectionChangedEventHandler? ScriptSelectionChanged;
    public event SelectionChangedEventHandler? OpcodeSelectionChanged;
    public event MouseButtonEventHandler? OpcodeMouseDoubleClick;
    public event KeyEventHandler? OpcodeKeyDown;
    public event RoutedEventHandler? ExpandTreeRequested;
    public event RoutedEventHandler? DisableTreeRequested;

    public IEnumerable? GroupsSource
    {
        get => GroupList.ItemsSource;
        set => GroupList.ItemsSource = value;
    }

    public IEnumerable? ScriptsSource
    {
        get => ScriptList.ItemsSource;
        set => ScriptList.ItemsSource = value;
    }

    public IEnumerable? OpcodesSource
    {
        get => OpcodeList.ItemsSource;
        set => OpcodeList.ItemsSource = value;
    }

    public IEnumerable? OpcodeTreeSource
    {
        get => OpcodeTree.ItemsSource;
        set => OpcodeTree.ItemsSource = value;
    }

    public object? SelectedGroup
    {
        get => GroupList.SelectedItem;
        set => GroupList.SelectedItem = value;
    }

    public object? SelectedScript
    {
        get => ScriptList.SelectedItem;
        set => ScriptList.SelectedItem = value;
    }

    public object? SelectedOpcode
    {
        get => OpcodeList.SelectedItem;
        set
        {
            OpcodeList.SelectedItem = value;
            SelectTreeOpcode(value);
        }
    }

    public int SelectedGroupIndex
    {
        get => GroupList.SelectedIndex;
        set => GroupList.SelectedIndex = value;
    }

    public int SelectedScriptIndex
    {
        get => ScriptList.SelectedIndex;
        set => ScriptList.SelectedIndex = value;
    }

    public ItemCollection Opcodes => OpcodeList.Items;

    public bool IsTreeMode
    {
        get => OpcodeTree.Visibility == Visibility.Visible;
        set
        {
            OpcodeTree.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            OpcodeList.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public int OpcodeTreeRootCount => OpcodeTree.Items.Count;

    public IReadOnlyList<string> GroupColumnHeaders => ["Group", "Type"];
    public IReadOnlyList<string> ScriptColumnHeaders => ["Script", "Size"];
    public IReadOnlyList<string> OpcodeColumnHeaders =>
    [
        "Offset",
        "Opcode",
        "Id",
        "Size",
        "Arguments",
        "Warning",
        "Raw bytes",
    ];

    public bool TreeButtonsEnabled
    {
        get => ExpandTreeButton.IsEnabled && DisableTreeButton.IsEnabled;
        set
        {
            ExpandTreeButton.IsEnabled = value;
            DisableTreeButton.IsEnabled = value;
        }
    }

    public FontWeight ExpandTreeFontWeight
    {
        get => ExpandTreeButton.FontWeight;
        set => ExpandTreeButton.FontWeight = value;
    }

    public FontWeight DisableTreeFontWeight
    {
        get => DisableTreeButton.FontWeight;
        set => DisableTreeButton.FontWeight = value;
    }

    public string PlaceholderText
    {
        get => WorkspaceLabel.Text;
        set => WorkspaceLabel.Text = value;
    }

    public Visibility PlaceholderVisibility
    {
        get => WorkspacePlaceholder.Visibility;
        set => WorkspacePlaceholder.Visibility = value;
    }

    public void ScrollOpcodeIntoView(object item) => OpcodeList.ScrollIntoView(item);

    public void FocusOpcodes() => OpcodeList.Focus();

    private void GroupList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        GroupSelectionChanged?.Invoke(this, e);

    private void ScriptList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ScriptSelectionChanged?.Invoke(this, e);

    private void OpcodeList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        OpcodeSelectionChanged?.Invoke(this, e);

    private void OpcodeList_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        OpcodeMouseDoubleClick?.Invoke(this, e);

    private void OpcodeList_KeyDown(object sender, KeyEventArgs e) =>
        OpcodeKeyDown?.Invoke(this, e);

    private void OpcodeTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not ScriptTreeItem { OpcodeItem: { } opcodeItem })
            return;

        OpcodeList.SelectedItem = opcodeItem;
        OpcodeSelectionChanged?.Invoke(this, new SelectionChangedEventArgs(
            Selector.SelectionChangedEvent,
            Array.Empty<object>(),
            new[] { opcodeItem }));
    }

    private void OpcodeTree_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        OpcodeMouseDoubleClick?.Invoke(this, e);

    private void OpcodeTree_KeyDown(object sender, KeyEventArgs e) =>
        OpcodeKeyDown?.Invoke(this, e);

    private void ExpandTreeButton_Click(object sender, RoutedEventArgs e) =>
        ExpandTreeRequested?.Invoke(this, e);

    private void DisableTreeButton_Click(object sender, RoutedEventArgs e) =>
        DisableTreeRequested?.Invoke(this, e);

    private void SelectTreeOpcode(object? opcodeItem)
    {
        if (opcodeItem == null)
            return;

        foreach (var root in OpcodeTree.Items.OfType<ScriptTreeItem>())
        {
            if (SelectTreeOpcode(root, opcodeItem))
                return;
        }
    }

    private static bool SelectTreeOpcode(ScriptTreeItem item, object opcodeItem)
    {
        if (ReferenceEquals(item.OpcodeItem, opcodeItem))
            return true;

        return item.Children.Any(child => SelectTreeOpcode(child, opcodeItem));
    }
}

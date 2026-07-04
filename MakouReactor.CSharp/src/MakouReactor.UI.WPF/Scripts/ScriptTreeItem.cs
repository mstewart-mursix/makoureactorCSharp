using System.Collections.ObjectModel;

namespace MakouReactor.UI.WPF.Scripts;

public sealed class ScriptTreeItem
{
    public ScriptTreeItem(string text, object? opcodeItem = null, IEnumerable<ScriptTreeItem>? children = null)
    {
        Text = text;
        OpcodeItem = opcodeItem;
        Children = new ObservableCollection<ScriptTreeItem>(children ?? []);
    }

    public string Text { get; }

    public object? OpcodeItem { get; }

    public ObservableCollection<ScriptTreeItem> Children { get; }
}

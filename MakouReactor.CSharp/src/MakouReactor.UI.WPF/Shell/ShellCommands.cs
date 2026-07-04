using System.Windows.Input;

namespace MakouReactor.UI.WPF.Shell;

public static class ShellCommands
{
    public static readonly RoutedUICommand OpenFile = Create("Open", "OpenFile", Key.O, ModifierKeys.Control);
    public static readonly RoutedUICommand OpenDirectory = Create("Open Directory", "OpenDirectory", Key.O, ModifierKeys.Control | ModifierKeys.Shift);
    public static readonly RoutedUICommand Save = Create("Save", "Save", Key.S, ModifierKeys.Control);
    public static readonly RoutedUICommand SaveAs = Create("Save As", "SaveAs", Key.S, ModifierKeys.Control | ModifierKeys.Shift);
    public static readonly RoutedUICommand ExportCurrentMap = Create("Export Current Map", "ExportCurrentMap", Key.E, ModifierKeys.Control);
    public static readonly RoutedUICommand ExportChunks = Create("Export Map Into Chunks", "ExportChunks", Key.U, ModifierKeys.Control);
    public static readonly RoutedUICommand MassExport = Create("Mass Export", "MassExport", Key.E, ModifierKeys.Control | ModifierKeys.Shift);
    public static readonly RoutedUICommand ImportCurrentMap = Create("Import To Current Map", "ImportCurrentMap", Key.I, ModifierKeys.Control);
    public static readonly RoutedUICommand RunFf7 = Create("Run FF7", "RunFf7", Key.F8, ModifierKeys.None);
    public static readonly RoutedUICommand CloseArchive = Create("Close", "CloseArchive");
    public static readonly RoutedUICommand Exit = Create("Exit", "Exit", Key.Q, ModifierKeys.Control);

    public static readonly RoutedUICommand OpenTexts = Create("Texts", "OpenTexts", Key.T, ModifierKeys.Control);
    public static readonly RoutedUICommand OpenModels = Create("Map Models", "OpenModels", Key.M, ModifierKeys.Control);
    public static readonly RoutedUICommand OpenEncounters = Create("Encounters", "OpenEncounters", Key.N, ModifierKeys.Control);
    public static readonly RoutedUICommand OpenTutorials = Create("Musics/Tutorials", "OpenTutorials", Key.K, ModifierKeys.Control);
    public static readonly RoutedUICommand OpenWalkmesh = Create("Walkmesh", "OpenWalkmesh", Key.W, ModifierKeys.Control);
    public static readonly RoutedUICommand OpenBackground = Create("Background", "OpenBackground", Key.B, ModifierKeys.Control);
    public static readonly RoutedUICommand OpenMiscellaneous = Create("Miscellaneous", "OpenMiscellaneous");
    public static readonly RoutedUICommand OpenPlayStationSections = Create("PlayStation Sections", "OpenPlayStationSections");
    public static readonly RoutedUICommand OpenVariableManager = Create("Variable Manager", "OpenVariableManager", Key.G, ModifierKeys.Control);
    public static readonly RoutedUICommand OpenFind = Create("Find", "OpenFind", Key.F, ModifierKeys.Control);
    public static readonly RoutedUICommand OpenBatchProcessing = Create("Batch Processing", "OpenBatchProcessing");
    public static readonly RoutedUICommand OpenLlmGenerator = Create("LLM Generate", "OpenLlmGenerator");
    public static readonly RoutedUICommand RevertLlmApply = Create("Revert LLM Apply", "RevertLlmApply", Key.Z, ModifierKeys.Control | ModifierKeys.Shift);

    public static readonly RoutedUICommand ToggleJapaneseCharacters = Create("Japanese Characters", "ToggleJapaneseCharacters");
    public static readonly RoutedUICommand OpenConfiguration = Create("Configuration", "OpenConfiguration");
    public static readonly RoutedUICommand ToggleFieldList = Create("Field List", "ToggleFieldList");
    public static readonly RoutedUICommand TogglePreview = Create("Background Preview", "TogglePreview");
    public static readonly RoutedUICommand SetBackgroundPreviewMode = Create("Background Preview Mode", "SetBackgroundPreviewMode");
    public static readonly RoutedUICommand SetModelPreviewMode = Create("Model Preview Mode", "SetModelPreviewMode");
    public static readonly RoutedUICommand About = Create("About", "About");

    private static RoutedUICommand Create(string text, string name) =>
        new(text, name, typeof(ShellCommands));

    private static RoutedUICommand Create(string text, string name, Key key, ModifierKeys modifiers) =>
        new(text, name, typeof(ShellCommands), [new KeyGesture(key, modifiers)]);
}

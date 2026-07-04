namespace MakouReactor.UI.WPF.Services;

public enum UnsavedChangesAction
{
    Continue,
    Cancel,
    Save,
}

public static class UnsavedChangesPromptPolicy
{
    public static UnsavedChangesAction Decide(
        bool isModified,
        bool hasArchive,
        DialogResultChoice promptChoice)
    {
        if (!isModified)
            return UnsavedChangesAction.Continue;

        return promptChoice switch
        {
            DialogResultChoice.Cancel or DialogResultChoice.None => UnsavedChangesAction.Cancel,
            DialogResultChoice.No => UnsavedChangesAction.Continue,
            DialogResultChoice.Yes => UnsavedChangesAction.Save,
            _ => UnsavedChangesAction.Cancel,
        };
    }
}

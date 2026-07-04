namespace MakouReactor.UI.WPF.Services;

public sealed record ShellCommandState(
    bool Save,
    bool SaveAs,
    bool ExportCurrentMap,
    bool ExportChunks,
    bool MassExport,
    bool ImportCurrentMap,
    bool CloseArchive,
    bool ArchiveAdd,
    bool ArchiveReplaceCurrent,
    bool ArchiveRemoveCurrent,
    bool ArchiveRenameCurrent,
    bool RunFf7,
    bool Texts,
    bool Models,
    bool Encounters,
    bool Tutorials,
    bool Walkmesh,
    bool Background,
    bool Misc,
    bool PlayStationSections,
    bool Batch,
    bool TextsTool,
    bool ModelsTool,
    bool WalkmeshTool,
    bool LlmGenerate,
    bool LlmRevert)
{
    public static ShellCommandState Empty { get; } = new(
        Save: false,
        SaveAs: false,
        ExportCurrentMap: false,
        ExportChunks: false,
        MassExport: false,
        ImportCurrentMap: false,
        CloseArchive: false,
        ArchiveAdd: false,
        ArchiveReplaceCurrent: false,
        ArchiveRemoveCurrent: false,
        ArchiveRenameCurrent: false,
        RunFf7: false,
        Texts: false,
        Models: false,
        Encounters: false,
        Tutorials: false,
        Walkmesh: false,
        Background: false,
        Misc: false,
        PlayStationSections: false,
        Batch: false,
        TextsTool: false,
        ModelsTool: false,
        WalkmeshTool: false,
        LlmGenerate: false,
        LlmRevert: false);
}

public static class ShellCommandStatePolicy
{
    public static ShellCommandState ForArchiveOpen(
        bool isOpen,
        bool canRunFf7,
        bool hasLlmSnapshot,
        bool canBatch = true)
    {
        return ShellCommandState.Empty with
        {
            SaveAs = isOpen,
            MassExport = isOpen,
            CloseArchive = isOpen,
            ArchiveAdd = isOpen,
            RunFf7 = canRunFf7,
            Batch = isOpen && canBatch,
            LlmRevert = hasLlmSnapshot,
        };
    }

    public static ShellCommandState ForPcFieldSelection(
        bool isSelected,
        bool hasArchive,
        bool canRunFf7,
        bool hasLlmSnapshot)
    {
        return ShellCommandState.Empty with
        {
            Save = isSelected,
            SaveAs = hasArchive,
            ExportCurrentMap = isSelected && hasArchive,
            ExportChunks = isSelected,
            MassExport = hasArchive,
            ImportCurrentMap = isSelected && hasArchive,
            CloseArchive = isSelected || hasArchive,
            ArchiveAdd = hasArchive,
            RunFf7 = canRunFf7,
            Texts = isSelected,
            Models = isSelected,
            Encounters = isSelected,
            Tutorials = isSelected,
            Walkmesh = isSelected,
            Background = isSelected,
            Misc = isSelected,
            TextsTool = isSelected,
            ModelsTool = isSelected,
            WalkmeshTool = isSelected,
            LlmGenerate = isSelected,
            LlmRevert = hasLlmSnapshot,
            Batch = hasArchive,
        };
    }

    public static ShellCommandState ForPlayStationField(
        bool hasArchive,
        bool canMutateArchive,
        bool canRunFf7,
        bool hasModelLoader,
        bool hasLlmSnapshot)
    {
        return ShellCommandState.Empty with
        {
            SaveAs = hasArchive && canMutateArchive,
            MassExport = hasArchive,
            CloseArchive = hasArchive,
            ArchiveAdd = hasArchive && canMutateArchive,
            RunFf7 = canRunFf7,
            Models = hasModelLoader,
            ModelsTool = hasModelLoader,
            PlayStationSections = true,
            Batch = hasArchive && canMutateArchive,
            LlmRevert = hasLlmSnapshot,
        };
    }

    public static ShellCommandState ForDirtyState(
        bool isModified,
        bool hasSaveTarget)
    {
        return ShellCommandState.Empty with
        {
            Save = isModified && hasSaveTarget,
        };
    }
}

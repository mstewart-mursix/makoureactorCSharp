namespace MakouReactor.UI.WPF.Diagnostics;

public sealed record DialogParityEntry(
    string Name,
    IReadOnlyList<string> OriginalSources,
    IReadOnlyList<string> DataDependencies,
    string WpfSurface,
    string ViewModel,
    string State,
    string EmptyState,
    string ReadOnlyDisplay,
    string EditingPolicy,
    string DirtyStatePolicy,
    string CloseCancelPolicy,
    IReadOnlyList<string> TestCoverage);

public static class DialogParityRegistry
{
    public static IReadOnlyList<DialogParityEntry> Entries { get; } =
    [
        Entry(
            "Text Manager",
            ["src/widgets/TextManager.cpp", "src/widgets/TextManager.h"],
            ["Section1File", "Script", "WindowBinFile text encoding"],
            "MakouReactor.UI.WPF.Texts.TextManagerDialog",
            "TextManagerViewModel",
            "Editable PC text lines with validation and dirty tracking"),

        Entry(
            "Walkmesh Manager",
            ["src/widgets/WalkmeshManager.cpp", "src/widgets/WalkmeshManager.h"],
            ["IdFile", "Field.Walkmesh"],
            "MakouReactor.UI.WPF.Walkmesh.WalkmeshManagerDialog",
            "WalkmeshManagerViewModel",
            "Editable add/remove walkmesh triangles with top-down preview and field dirty-state apply"),

        Entry(
            "Background Manager",
            ["src/widgets/BGDialog.cpp", "src/widgets/BGDialog.h", "src/widgets/BackgroundEditor.cpp"],
            ["BackgroundFile", "Background tiles", "Palettes", "Textures"],
            "MakouReactor.UI.WPF.Background.BackgroundDialog",
            "Dialog-local projection over BackgroundFile",
            "Rendered tile-layout preview, raw section export, and background metadata display"),

        Entry(
            "Variable Manager",
            ["src/widgets/VarManager.cpp", "src/widgets/VarManager.h"],
            ["Section1File", "VariableReferenceScanner", "RawOpcode"],
            "MakouReactor.UI.WPF.Variables.VariableManagerDialog",
            "VariableManagerViewModel",
            "Filterable variable reference list with script/opcode navigation"),

        Entry(
            "Search",
            ["src/widgets/Search.cpp", "src/widgets/Search.h"],
            ["FieldSearchService", "Section1File", "Text lines", "RawOpcode"],
            "MakouReactor.UI.WPF.Search.SearchDialog",
            "Dialog-local search result projection",
            "Current-field search with navigation callbacks"),

        Entry(
            "LLM Generate",
            ["src/ui/LLMSceneDialog.cpp", "src/ui/LLMSceneDialog.h", "src/ai/ScenePlanMapper.cpp"],
            ["LLMClient", "ScenePlanParser", "ScenePlanMapper", "FieldEditSnapshot"],
            "MakouReactor.UI.WPF.AI.LLMSceneDialog",
            "LLMSceneDialogViewModel",
            "Preview and apply flow with revert snapshot"),

        Entry(
            "Map Models Manager",
            ["src/widgets/ModelManager.cpp", "src/widgets/ModelManagerPC.cpp", "src/widgets/ModelManagerPS.cpp"],
            ["FieldModelLoaderPC", "FieldModelLoaderPS", "FieldModelAnimation"],
            "MakouReactor.UI.WPF.Models.ModelManagerDialog / PlayStationModelManagerDialog",
            "ModelManagerViewModel; PlayStationModelManagerViewModel",
            "Editable PC and PS model-loader metadata with field dirty-state apply"),

        Entry(
            "Animation Selector",
            ["src/widgets/AnimEditorDialog.cpp", "src/widgets/AnimEditorDialog.h"],
            ["FieldModelLoaderPC", "FieldModelAnimation"],
            "MakouReactor.UI.WPF.Models.AnimationSelectorDialog",
            "Dialog-local animation rows",
            "Read-only selector parity for original animation chooser"),

        Entry(
            "Encounters Manager",
            ["src/widgets/EncounterWidget.cpp", "src/widgets/EncounterTableWidget.h", "src/core/field/EncounterFile.cpp"],
            ["EncounterFile", "Encounter tables"],
            "MakouReactor.UI.WPF.Encounters.EncounterDialog",
            "EncounterManagerViewModel",
            "Editable encounter tables with field dirty-state apply"),

        Entry(
            "Musics/Tutorials Manager",
            ["src/widgets/TutWidget.cpp", "src/widgets/TutWidget.h", "src/widgets/PsfDialog.cpp"],
            ["TutorialFile", "AKAO/TUTO entries", "PSF tags"],
            "MakouReactor.UI.WPF.Tutorials.TutorialsDialog",
            "TutorialsViewModel",
            "Filterable tutorial/music display with raw entry export"),

        Entry(
            "Miscellaneous Manager",
            ["src/widgets/MiscWidget.cpp", "src/widgets/EncounterWidget.cpp", "src/core/field/InfFile.cpp"],
            ["InfFile", "triggers", "camera ranges", "field metadata"],
            "MakouReactor.UI.WPF.Misc.MiscellaneousDialog",
            "MiscellaneousViewModel",
            "Editable INF general metadata with exits/triggers/arrows display"),

        Entry(
            "Batch Processing",
            ["src/widgets/OperationsManager.cpp", "src/widgets/MassExportDialog.cpp", "src/widgets/MassImportDialog.cpp"],
            ["FieldArchive", "Section1File", "Script", "EncounterFile", "FieldModelLoaderPC", "PC field sections", "text operations", "encounter tables"],
            "MakouReactor.UI.WPF.Batch.BatchProcessingDialog",
            "Dialog-local batch operation model",
            "Safe text operations, text-window autosize, encounter disabling, PC model-loader cleanup, and PC background tile-section cleanup enabled; unsupported operations disabled"),

        Entry(
            "Archive Manager",
            ["src/core/field/FieldArchive.cpp", "src/widgets/ImportDialog.cpp", "src/widgets/ExportChunksDialog.cpp"],
            ["FieldArchive", "archive entries", "raw entry bytes"],
            "MakouReactor.UI.WPF.Archive.ArchiveManagerView",
            "ArchiveManagerViewModel",
            "Extract, rename, replace, and status display"),

        Entry(
            "Script Opcode Editor",
            ["src/widgets/ScriptEditor.cpp", "src/widgets/ScriptEditorWidgets"],
            ["RawOpcode", "Script.ReplaceRawOpcodeBytes", "Section1File"],
            "MakouReactor.UI.WPF.Scripts.RawOpcodeDialog",
            "TypedOpcodeEditor",
            "Raw and typed fixed-size opcode editing"),

        Entry(
            "PlayStation Section Manager",
            ["src/core/field/FieldPS.cpp", "src/core/field/FieldPSDemo.cpp"],
            ["FieldPS", "DAT section table", "section parser status"],
            "MakouReactor.UI.WPF.PlayStation.PlayStationSectionManagerDialog",
            "PlayStationSectionManagerViewModel",
            "PS DAT section inspection with raw section export/replacement")
    ];

    private static DialogParityEntry Entry(
        string name,
        IReadOnlyList<string> originalSources,
        IReadOnlyList<string> dataDependencies,
        string wpfSurface,
        string viewModel,
        string state,
        string emptyState = "Command is disabled until required field/archive data exists, or the dialog displays an empty/status message.",
        string readOnlyDisplay = "Read-only rows and detail text are visible for parsed data; unsupported edit paths are disabled.",
        string editingPolicy = "Editing is enabled only where the save model is implemented; otherwise the dialog remains read-only or visibly disabled.",
        string dirtyStatePolicy = "Editable flows notify the shell dirty state; read-only flows do not mutate field data.",
        string closeCancelPolicy = "Dialog closes through WPF Close/Cancel/IsCancel behavior without mutating data unless Apply/OK succeeds.",
        IReadOnlyList<string>? testCoverage = null) =>
        new(
            name,
            originalSources,
            dataDependencies,
            wpfSurface,
            viewModel,
            state,
            emptyState,
            readOnlyDisplay,
            editingPolicy,
            dirtyStatePolicy,
            closeCancelPolicy,
            testCoverage ?? ["MainWindowStartupTests.cs"]);
}

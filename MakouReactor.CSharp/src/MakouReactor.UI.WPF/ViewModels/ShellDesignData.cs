namespace MakouReactor.UI.WPF.ViewModels;

public static class ShellDesignData
{
    public static MainWindowViewModel Create()
    {
        var viewModel = new MainWindowViewModel
        {
            IsArchiveOpen = true,
            IsFieldSelected = true,
            ArchiveName = "flevel.lgp",
            ArchivePath = @"C:\Games\Final Fantasy VII\data\field\flevel.lgp",
            ArchiveType = ShellArchiveType.Lgp,
            CurrentFieldName = "md1stin",
            StatusText = "Ready",
            SelectedGroupIndex = 0,
            SelectedGroupName = "Cloud",
            SelectedScriptIndex = 0,
            SelectedScriptName = "Init",
            SelectedOpcodeOffset = 0,
            SelectedOpcodeName = "REQ",
        };

        viewModel.FieldList.Fields.Add(new FieldListEntryViewModel(0, "md1stin", 140320, "Ready"));
        viewModel.FieldList.Fields.Add(new FieldListEntryViewModel(1, "nmkin_1", 98244, "Ready"));
        viewModel.FieldList.SelectedField = viewModel.FieldList.Fields[0];

        viewModel.ScriptManager.Groups.Add(new GroupScriptViewModel(0, "Cloud", "Model"));
        viewModel.ScriptManager.Scripts.Add(new ScriptEntryViewModel(0, "Init", 128));
        viewModel.ScriptManager.Opcodes.Add(new OpcodeEntryViewModel(0, "REQ", "group=1 script=0", string.Empty));

        viewModel.ArchiveManager.Entries.Add(new ArchiveEntryViewModel("md1stin", "md1stin", string.Empty, 140320));
        viewModel.ArchiveManager.PreviewText = "Synthetic archive preview";

        viewModel.Preview.FieldName = "md1stin";
        viewModel.Preview.Summary = "md1stin\nBackground preview sample";

        return viewModel;
    }
}

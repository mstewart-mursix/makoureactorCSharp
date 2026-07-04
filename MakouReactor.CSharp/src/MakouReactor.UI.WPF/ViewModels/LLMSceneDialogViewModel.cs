using CommunityToolkit.Mvvm.ComponentModel;

namespace MakouReactor.UI.WPF.ViewModels;

public sealed partial class LLMSceneDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private string _previewSummary = string.Empty;

    [ObservableProperty]
    private bool _canApply;
}

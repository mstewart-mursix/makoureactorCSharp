using CommunityToolkit.Mvvm.ComponentModel;

namespace MakouReactor.UI.WPF.ViewModels;

public sealed partial class PreviewViewModel : ObservableObject
{
    [ObservableProperty]
    private string _mode = "Background";

    [ObservableProperty]
    private string _fieldName = string.Empty;

    [ObservableProperty]
    private string _summary = "Background preview\nNo field selected";
}

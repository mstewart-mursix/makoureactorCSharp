using CommunityToolkit.Mvvm.ComponentModel;

namespace MakouReactor.UI.WPF.ViewModels;

public sealed partial class StatusBarViewModel : ObservableObject
{
    [ObservableProperty]
    private string _message = "Ready";

    [ObservableProperty]
    private bool _isBusy;
}

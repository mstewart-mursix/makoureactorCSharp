using System.Windows;

namespace MakouReactor.UI.WPF.Services;

public interface IDialogService
{
    void ShowInformation(Window owner, string title, string message);
    void ShowWarning(Window owner, string title, string message);
    void ShowError(Window owner, string title, string message);
    DialogResultChoice ConfirmYesNoCancel(Window owner, string title, string message);
}

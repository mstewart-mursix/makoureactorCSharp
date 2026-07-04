using System.Windows;

namespace MakouReactor.UI.WPF.Services;

public sealed class WpfDialogService : IDialogService, IErrorReporter
{
    public void ShowInformation(Window owner, string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(Window owner, string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowError(Window owner, string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public DialogResultChoice ConfirmYesNoCancel(Window owner, string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning) switch
        {
            MessageBoxResult.Yes => DialogResultChoice.Yes,
            MessageBoxResult.No => DialogResultChoice.No,
            MessageBoxResult.Cancel => DialogResultChoice.Cancel,
            _ => DialogResultChoice.None,
        };

    public void Report(Window owner, string title, Exception exception) =>
        ShowError(owner, title, exception.Message);
}

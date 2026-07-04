using System.Windows;
using System.Windows.Input;

using MakouReactor.Core.Models;
using MakouReactor.Core.Services;
using MakouReactor.UI.WPF.ViewModels;

namespace MakouReactor.UI.WPF.Variables;

public partial class VariableManagerDialog : Window
{
    private readonly VariableManagerViewModel _viewModel;

    public event Action<VariableReference>? ReferenceActivated;

    public VariableManagerDialog(string fieldName, Section1File section)
    {
        InitializeComponent();
        _viewModel = new VariableManagerViewModel(fieldName, section);
        DataContext = _viewModel;
        StatusLabel.SetBinding(System.Windows.Controls.TextBlock.TextProperty, "StatusText");
        QueryBox.Focus();
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearFilter();
        QueryBox.Focus();
    }

    private void GoToReference_Click(object sender, RoutedEventArgs e)
    {
        ActivateSelectedReference();
    }

    private void VariableListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ActivateSelectedReference();
    }

    private void ActivateSelectedReference()
    {
        if (_viewModel.SelectedReference == null)
            return;

        ReferenceActivated?.Invoke(_viewModel.SelectedReference);
    }
}

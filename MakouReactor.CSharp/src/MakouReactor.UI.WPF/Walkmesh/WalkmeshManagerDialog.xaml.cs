using System.Windows;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

namespace MakouReactor.UI.WPF.Walkmesh;

public partial class WalkmeshManagerDialog : Window
{
    private readonly WalkmeshManagerViewModel _viewModel;

    public WalkmeshManagerDialog(string fieldName, FieldPC field)
    {
        InitializeComponent();
        _viewModel = new WalkmeshManagerViewModel(fieldName, field);
        DataContext = _viewModel;
        StatusLabel.SetBinding(System.Windows.Controls.TextBlock.TextProperty, "StatusText");
    }

    private void AddTriangle_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddTriangle();
    }

    private void RemoveTriangle_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RemoveSelectedTriangle();
    }

    private void ApplyWalkmesh_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ApplyChanges();
    }
}

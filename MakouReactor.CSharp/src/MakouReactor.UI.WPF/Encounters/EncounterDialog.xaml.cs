using System.Windows;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

namespace MakouReactor.UI.WPF.Encounters;

public partial class EncounterDialog : Window
{
    private readonly EncounterManagerViewModel _viewModel;

    public EncounterDialog(string fieldName, FieldPC field)
    {
        InitializeComponent();
        _viewModel = new EncounterManagerViewModel(fieldName, field);
        DataContext = _viewModel;
        StatusLabel.SetBinding(System.Windows.Controls.TextBlock.TextProperty, "StatusText");
    }

    private void ApplyEncounter_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.ApplyChanges();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                ex.Message,
                "Encounters",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}

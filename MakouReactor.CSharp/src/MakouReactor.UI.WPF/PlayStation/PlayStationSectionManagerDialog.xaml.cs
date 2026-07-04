using System.IO;
using System.Windows;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

using Microsoft.Win32;

namespace MakouReactor.UI.WPF.PlayStation;

public partial class PlayStationSectionManagerDialog : Window
{
    private readonly PlayStationSectionManagerViewModel _viewModel;

    public PlayStationSectionManagerDialog(FieldPS field)
    {
        InitializeComponent();
        _viewModel = new PlayStationSectionManagerViewModel(field);
        DataContext = _viewModel;
    }

    private void ExportRawSection_Click(object sender, RoutedEventArgs e)
    {
        var section = _viewModel.SelectedSection;
        if (section == null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export PlayStation Section",
            FileName = $"{_viewModel.FieldName}_{section.Index}_{section.Name}.bin",
            Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllBytes(dialog.FileName, _viewModel.ExportSelectedSection());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                ex.Message,
                "Export PlayStation Section",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ReplaceRawSection_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Replace PlayStation Section",
            Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            _viewModel.ReplaceSelectedSection(File.ReadAllBytes(dialog.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                ex.Message,
                "Replace PlayStation Section",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}

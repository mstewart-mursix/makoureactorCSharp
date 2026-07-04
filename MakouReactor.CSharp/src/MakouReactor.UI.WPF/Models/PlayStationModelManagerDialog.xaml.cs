using System.IO;
using System.Windows;

using Microsoft.Win32;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.Shared;
using MakouReactor.UI.WPF.ViewModels;

namespace MakouReactor.UI.WPF.Models;

public partial class PlayStationModelManagerDialog : Window
{
    private readonly PlayStationModelManagerViewModel _viewModel;

    public PlayStationModelManagerDialog(string fieldName, FieldModelLoaderPS modelLoader)
        : this(fieldName, modelLoader, field: null)
    {
    }

    public PlayStationModelManagerDialog(
        string fieldName,
        FieldModelLoaderPS modelLoader,
        FieldPS? field,
        ReadOnlySpan<byte> bsxData = default)
    {
        InitializeComponent();
        _viewModel = new PlayStationModelManagerViewModel(fieldName, modelLoader, field, bsxData);
        DataContext = _viewModel;
        StatusLabel.SetBinding(System.Windows.Controls.TextBlock.TextProperty, "StatusText");
    }

    public int ModelCount => _viewModel.Models.Count;

    public int SelectedModelId => _viewModel.SelectedModel?.Id ?? -1;

    private void ApplyModelLoader_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ApplyChanges();
    }

    private void ExportAnimation_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export PlayStation animation",
            FileName = _viewModel.DefaultAnimationExportFileName(),
            Filter = FileDialogFilters.ExportAnimation,
        };
        if (dialog.ShowDialog(this) != true)
            return;

        File.WriteAllBytes(dialog.FileName, _viewModel.ExportSelectedAnimation());
        StatusLabel.Text = $"Exported animation to {dialog.FileName}.";
    }
}

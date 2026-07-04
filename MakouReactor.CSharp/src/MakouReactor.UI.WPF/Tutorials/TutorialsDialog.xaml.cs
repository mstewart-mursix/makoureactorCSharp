using System.IO;
using System.Windows;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

using Microsoft.Win32;

namespace MakouReactor.UI.WPF.Tutorials;

public partial class TutorialsDialog : Window
{
    private readonly TutorialsViewModel _viewModel;

    public TutorialsDialog(string fieldName, TutorialFile tutorialFile)
    {
        InitializeComponent();
        _viewModel = new TutorialsViewModel(fieldName, tutorialFile);
        DataContext = _viewModel;
        StatusLabel.SetBinding(System.Windows.Controls.TextBlock.TextProperty, "StatusText");
    }

    private void ExportEntry_Click(object sender, RoutedEventArgs e)
    {
        var entry = _viewModel.SelectedEntry;
        if (entry == null)
            return;

        var extension = entry.Kind == TutorialEntryKind.Music ? "akao" : "tuto";
        var dialog = new SaveFileDialog
        {
            Title = "Export Tutorial/Music Entry",
            FileName = $"{_viewModel.FieldName}_{entry.Name}.{extension}",
            Filter = "Binary files (*.bin;*.akao;*.tuto)|*.bin;*.akao;*.tuto|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllBytes(dialog.FileName, entry.RawData);
            StatusLabel.Text = $"Exported {entry.Name} to {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                ex.Message,
                "Export Tutorial/Music Entry",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}

using System.Windows;

using MakouReactor.Core.Services;

namespace MakouReactor.UI.WPF.Batch;

public partial class BatchProcessingDialog : Window
{
    public ArchiveBatchOperation SelectedOperations { get; private set; }

    public BatchProcessingDialog(bool isPcArchive)
    {
        InitializeComponent();
        CleanModelLoaderCheckBox.Visibility = isPcArchive ? Visibility.Visible : Visibility.Collapsed;
        RemoveUnusedBackgroundCheckBox.Visibility = isPcArchive ? Visibility.Visible : Visibility.Collapsed;
        RepairBackgroundsCheckBox.Visibility = isPcArchive ? Visibility.Visible : Visibility.Collapsed;
        ResizeBackgroundsCheckBox.Visibility = isPcArchive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Operation_Checked(object sender, RoutedEventArgs e)
    {
        var selected = BuildSelectedOperations();
        ApplyButton.IsEnabled = selected != ArchiveBatchOperation.None;
        StatusLabel.Text = selected == ArchiveBatchOperation.None
            ? "Select one or more available batch operations."
            : "Ready to apply selected batch operations.";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        SelectedOperations = BuildSelectedOperations();
        if (SelectedOperations == ArchiveBatchOperation.None)
        {
            StatusLabel.Text = "Select one or more available batch operations.";
            return;
        }

        DialogResult = true;
    }

    private ArchiveBatchOperation BuildSelectedOperations()
    {
        var operations = ArchiveBatchOperation.None;
        if (CleanUnusedTextsCheckBox.IsChecked == true)
            operations |= ArchiveBatchOperation.CleanUnusedTexts;
        if (EmptyTextsCheckBox.IsChecked == true)
            operations |= ArchiveBatchOperation.EmptyTexts;
        if (AutosizeTextWindowsCheckBox.IsChecked == true)
            operations |= ArchiveBatchOperation.AutosizeTextWindows;
        if (DisableBattlesCheckBox.IsChecked == true)
            operations |= ArchiveBatchOperation.DisableBattles;
        if (CleanModelLoaderCheckBox.IsChecked == true)
            operations |= ArchiveBatchOperation.CleanModelLoader;
        if (RemoveUnusedBackgroundCheckBox.IsChecked == true)
            operations |= ArchiveBatchOperation.RemoveUnusedBackgroundSections;
        if (RepairBackgroundsCheckBox.IsChecked == true)
            operations |= ArchiveBatchOperation.RepairBackgrounds;
        if (ResizeBackgroundsCheckBox.IsChecked == true)
            operations |= ArchiveBatchOperation.ResizeBackgrounds;
        return operations;
    }
}

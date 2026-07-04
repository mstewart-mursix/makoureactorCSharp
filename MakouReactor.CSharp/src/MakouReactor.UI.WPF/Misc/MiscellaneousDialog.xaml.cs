using System.Windows;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

namespace MakouReactor.UI.WPF.Misc;

public partial class MiscellaneousDialog : Window
{
    private readonly MiscellaneousViewModel _viewModel;

    public MiscellaneousDialog(string fieldName, FieldPC field, Section1File? section)
    {
        if (field.Inf == null)
            throw new ArgumentException("Field does not contain parsed INF data.", nameof(field));

        InitializeComponent();
        _viewModel = new MiscellaneousViewModel(fieldName, field, section);
        DataContext = _viewModel;

        var background = field.Inf.BackgroundLayers;
        LayerFlagsBox.Text = $"{background.Layer1Flag}, {background.Layer2Flag}, {background.Layer3Flag}, {background.Layer4Flag}";
        Layer3SizeBox.Text = $"{background.Layer3Width} x {background.Layer3Height}";
        Layer4SizeBox.Text = $"{background.Layer4Width} x {background.Layer4Height}";
        Layer3OffsetBox.Text = $"{background.Layer3X}, {background.Layer3Y}";
        Layer4OffsetBox.Text = $"{background.Layer4X}, {background.Layer4Y}";
        LayerMultiplierBox.Text =
            $"L3 {background.Layer3XMultiplier}, {background.Layer3YMultiplier}; " +
            $"L4 {background.Layer4XMultiplier}, {background.Layer4YMultiplier}";

        StatusLabel.Text = $"Loaded INF metadata ({field.Inf.Size} bytes).";
    }

    private void ApplyMisc_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ApplyChanges())
            StatusLabel.Text = "Applied INF metadata changes.";
    }
}

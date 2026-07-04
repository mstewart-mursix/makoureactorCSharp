using System.Windows.Controls;

using MakouReactor.Core.Models;

namespace MakouReactor.UI.WPF.Preview;

public partial class PreviewPanel : UserControl
{
    public PreviewPanel()
    {
        InitializeComponent();
    }

    public bool IsModelPreviewVisible => ModelPreview.Visibility == System.Windows.Visibility.Visible;

    public string? CurrentModelPreviewName => ModelPreview.CurrentModelName;

    public void ShowField(FieldPC? field, string previewMode)
    {
        if (previewMode.Equals("Model", StringComparison.OrdinalIgnoreCase))
        {
            BackgroundPreview.Visibility = System.Windows.Visibility.Collapsed;
            ModelPreview.Visibility = System.Windows.Visibility.Visible;
            ModelPreview.ShowField(field);
            return;
        }

        ModelPreview.Visibility = System.Windows.Visibility.Collapsed;
        BackgroundPreview.Visibility = System.Windows.Visibility.Visible;
        BackgroundPreview.ShowField(field, previewMode);
    }

    public void ShowLoadFailed(string fieldName)
    {
        ModelPreview.Visibility = System.Windows.Visibility.Collapsed;
        BackgroundPreview.Visibility = System.Windows.Visibility.Visible;
        BackgroundPreview.ShowLoadFailed(fieldName);
    }

    public void ShowPlayStationField(FieldPS field)
    {
        ModelPreview.Visibility = System.Windows.Visibility.Collapsed;
        BackgroundPreview.Visibility = System.Windows.Visibility.Visible;
        BackgroundPreview.ShowPlayStationField(field);
    }
}

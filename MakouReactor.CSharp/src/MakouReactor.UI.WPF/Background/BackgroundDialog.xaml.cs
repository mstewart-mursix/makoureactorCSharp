using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;

using MakouReactor.Core.Models;

using Microsoft.Win32;

namespace MakouReactor.UI.WPF.Background;

public partial class BackgroundDialog : Window
{
    private readonly string _fieldName;
    private readonly Action<byte[]>? _importBackground;
    private BackgroundFilePC _background;

    public BackgroundDialog(string fieldName, BackgroundFilePC background, Action<byte[]>? importBackground = null)
    {
        InitializeComponent();
        _fieldName = fieldName;
        _background = background;
        _importBackground = importBackground;
        ImportBackgroundButton.IsEnabled = importBackground != null;
        RefreshBackground(background);
        StatusLabel.Text =
            $"Loaded {background.TileCount:N0} tile(s) and {background.ExistingTextureCount:N0} texture metadata record(s).";
    }

    private void RefreshBackground(BackgroundFilePC background)
    {
        _background = background;
        FieldLabel.Text = _fieldName;
        SectionSizeLabel.Text = $"Section: {background.Size:N0} bytes";
        TileCountLabel.Text = $"Tiles: {background.TileCount:N0}";
        TextureCountLabel.Text = $"Textures: {background.ExistingTextureCount}/{background.Textures.Count}";
        TextureOffsetLabel.Text = $"Texture table: 0x{background.TextureTableOffset:X}";
        LayerListView.ItemsSource = background.Layers;
        TextureListView.ItemsSource = background.Textures.Where(static texture => texture.Exists).ToArray();
        BackgroundPreview.ShowBackground(_fieldName, background);
        if (background.Layers.Count > 0)
            LayerListView.SelectedIndex = 0;
    }

    private void LayerListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        TileListView.ItemsSource = LayerListView.SelectedItem is BackgroundLayerPC layer
            ? layer.Tiles
            : null;
    }

    private void ExportBackground_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Background Section",
            FileName = $"{_fieldName}_background.bin",
            Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllBytes(dialog.FileName, _background.RawData.ToArray());
            StatusLabel.Text = $"Exported background section to {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                ex.Message,
                "Export Background",
                MessageBoxButton.OK,
            MessageBoxImage.Error);
        }
    }

    private void ImportBackground_Click(object sender, RoutedEventArgs e)
    {
        if (_importBackground == null)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Import Background Section",
            Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var data = File.ReadAllBytes(dialog.FileName);
            var parsed = BackgroundFilePC.Open(data);
            _importBackground(data);
            RefreshBackground(parsed);
            StatusLabel.Text = $"Imported background section from {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                ex.Message,
                "Import Background",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}

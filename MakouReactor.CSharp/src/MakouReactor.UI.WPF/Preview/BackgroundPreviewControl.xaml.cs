using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using MakouReactor.Core.Models;

namespace MakouReactor.UI.WPF.Preview;

public partial class BackgroundPreviewControl : UserControl
{
    private static readonly Brush[] LayerFills =
    [
        FrozenBrush(Color.FromArgb(130, 90, 170, 255)),
        FrozenBrush(Color.FromArgb(120, 120, 220, 140)),
        FrozenBrush(Color.FromArgb(120, 255, 190, 90)),
        FrozenBrush(Color.FromArgb(120, 220, 110, 190)),
    ];

    private static readonly Brush[] LayerStrokes =
    [
        FrozenBrush(Colors.LightSkyBlue),
        FrozenBrush(Colors.LightGreen),
        FrozenBrush(Colors.Khaki),
        FrozenBrush(Colors.Plum),
    ];

    public BackgroundPreviewControl()
    {
        InitializeComponent();
        ShowMessage("Background preview\nNo field selected");
    }

    public int RenderedTileCount { get; private set; }

    public bool IsTilePreviewVisible => TileCanvas.Visibility == Visibility.Visible;

    public Brush PreviewBackground => PreviewBorder.Background;

    public void ShowField(FieldPC? field, string previewMode)
    {
        if (field == null)
        {
            ShowMessage(previewMode == "Model"
                ? "Model preview\nNo field selected"
                : "Background preview\nNo field selected");
            return;
        }

        if (previewMode == "Model")
        {
            var modelCount = field.ModelLoader?.Models.Count ?? 0;
            ShowMessage($"{field.Name}\nModel preview\n{modelCount} model(s)");
            return;
        }

        if (field.Background == null || field.Background.TileCount == 0)
        {
            ShowMessage(
                $"{field.Name}\n" +
                $"{field.Data.Length:N0} bytes\n" +
                $"{field.Sections.Count} sections");
            return;
        }

        ShowBackgroundTiles(field);
    }

    public void ShowLoadFailed(string fieldName) => ShowMessage($"{fieldName}\nLoad failed");

    public void ShowPlayStationField(FieldPS field) =>
        ShowMessage(
            $"{field.Name}\n" +
            "PlayStation field preview\n" +
            $"{field.Sections.Count} sections, {field.Data.Length:N0} bytes");

    public void ShowBackground(string fieldName, BackgroundFilePC background)
    {
        ArgumentNullException.ThrowIfNull(background);
        ShowBackgroundTiles(fieldName, background);
    }

    private void ShowMessage(string message)
    {
        PreviewText.Text = message;
        PreviewText.Visibility = Visibility.Visible;
        TileCanvas.Visibility = Visibility.Collapsed;
        TileItemsControl.ItemsSource = Array.Empty<PreviewTile>();
        RenderedTileCount = 0;
    }

    private void ShowBackgroundTiles(FieldPC field)
    {
        ShowBackgroundTiles(field.Name, field.Background!);
    }

    private void ShowBackgroundTiles(string fieldName, BackgroundFilePC background)
    {
        var tiles = background.Layers
            .Where(static layer => layer.Exists)
            .SelectMany(static layer => layer.Tiles)
            .ToArray();
        if (tiles.Length == 0)
        {
            ShowMessage($"{fieldName}\nBackground preview\nNo visible tiles");
            return;
        }

        const double previewWidth = 180;
        const double previewHeight = 130;
        var minX = tiles.Min(static tile => (double)tile.DestinationX);
        var minY = tiles.Min(static tile => (double)tile.DestinationY);
        var maxX = tiles.Max(static tile => tile.DestinationX + tile.Size);
        var maxY = tiles.Max(static tile => tile.DestinationY + tile.Size);
        var width = Math.Max(1, maxX - minX);
        var height = Math.Max(1, maxY - minY);
        var scale = Math.Min(previewWidth / width, previewHeight / height);

        TileCanvas.Width = previewWidth;
        TileCanvas.Height = previewHeight;
        var previewTiles = tiles.Select(tile =>
        {
            var layerIndex = Math.Clamp(tile.LayerId, 0, LayerFills.Length - 1);
            return new PreviewTile(
                (tile.DestinationX - minX) * scale,
                (tile.DestinationY - minY) * scale,
                Math.Max(2, tile.Size * scale),
                LayerFills[layerIndex],
                LayerStrokes[layerIndex],
                $"Layer {tile.LayerId + 1}, tile {tile.TileId}: {tile.Destination}");
        }).ToArray();
        TileItemsControl.ItemsSource = previewTiles;
        RenderedTileCount = previewTiles.Length;

        PreviewText.Text =
            $"{fieldName}\n" +
            $"{background.TileCount} tile(s), {background.ExistingTextureCount} texture(s)";
        PreviewText.Visibility = Visibility.Visible;
        TileCanvas.Visibility = Visibility.Visible;
    }

    private sealed record PreviewTile(
        double X,
        double Y,
        double Size,
        Brush Fill,
        Brush Stroke,
        string ToolTip);

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

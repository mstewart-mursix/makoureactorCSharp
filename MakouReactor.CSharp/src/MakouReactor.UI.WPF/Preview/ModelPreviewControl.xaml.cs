using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using MakouReactor.Core.Models;

namespace MakouReactor.UI.WPF.Preview;

public partial class ModelPreviewControl : UserControl
{
    public ModelPreviewControl()
    {
        InitializeComponent();
        ShowEmpty("Model preview\nNo field selected");
    }

    public string? CurrentModelName { get; private set; }

    public int CurrentAnimationCount { get; private set; }

    public bool IsModelVisible => ModelGlyphPanel.Visibility == Visibility.Visible;

    public void ShowField(FieldPC? field)
    {
        if (field == null)
        {
            ShowEmpty("Model preview\nNo field selected");
            return;
        }

        var model = field.ModelLoader?.Models.FirstOrDefault();
        ShowModel(field.Name, model);
    }

    public void ShowModel(string fieldName, FieldModelInfoPC? model)
    {
        if (model == null)
        {
            ShowEmpty($"{fieldName}\nNo model loader data");
            return;
        }

        CurrentModelName = model.CharacterName;
        CurrentAnimationCount = model.Animations.Count;
        EmptyText.Visibility = Visibility.Collapsed;
        ModelGlyphPanel.Visibility = Visibility.Visible;
        TitleText.Visibility = Visibility.Visible;
        SubtitleText.Visibility = Visibility.Visible;
        HrcText.Visibility = Visibility.Visible;
        ScaleText.Visibility = Visibility.Visible;
        AnimationCountText.Visibility = Visibility.Visible;
        ColorText.Visibility = Visibility.Visible;
        AnimationItemsControl.Visibility = Visibility.Visible;

        TitleText.Text = string.IsNullOrWhiteSpace(model.CharacterName)
            ? $"Model {model.Id}"
            : model.CharacterName;
        SubtitleText.Text = $"{fieldName} - model {model.Id} - unknown {model.Unknown}";
        HrcText.Text = string.IsNullOrWhiteSpace(model.HrcName) ? "(none)" : model.HrcName;
        ScaleText.Text = model.Scale.ToString();
        AnimationCountText.Text = model.Animations.Count.ToString();
        ColorText.Text = model.GlobalColor.ToString();
        ColorSwatch.Background = new SolidColorBrush(Color.FromRgb(
            model.GlobalColor.R,
            model.GlobalColor.G,
            model.GlobalColor.B));
        AnimationItemsControl.ItemsSource = model.Animations
            .Take(8)
            .Select(static animation => string.IsNullOrWhiteSpace(animation.Name)
                ? $"Animation {animation.Id}"
                : animation.Name)
            .ToArray();
    }

    private void ShowEmpty(string message)
    {
        CurrentModelName = null;
        CurrentAnimationCount = 0;
        EmptyText.Text = message;
        EmptyText.Visibility = Visibility.Visible;
        ModelGlyphPanel.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Collapsed;
        SubtitleText.Visibility = Visibility.Collapsed;
        HrcText.Visibility = Visibility.Collapsed;
        ScaleText.Visibility = Visibility.Collapsed;
        AnimationCountText.Visibility = Visibility.Collapsed;
        ColorText.Visibility = Visibility.Collapsed;
        AnimationItemsControl.Visibility = Visibility.Collapsed;
        AnimationItemsControl.ItemsSource = Array.Empty<string>();
    }
}

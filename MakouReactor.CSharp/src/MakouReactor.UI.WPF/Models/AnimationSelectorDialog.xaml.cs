using System.Windows;
using System.Windows.Controls;

using MakouReactor.Core.Models;

namespace MakouReactor.UI.WPF.Models;

public partial class AnimationSelectorDialog : Window
{
    private readonly string _fieldName;
    private readonly FieldModelInfoPC _model;

    public AnimationSelectorDialog(string fieldName, FieldModelInfoPC model, int selectedAnimationId = 0)
    {
        InitializeComponent();
        _fieldName = fieldName;
        _model = model;
        AnimationListView.ItemsSource = model.Animations;
        ModelPreview.ShowModel(fieldName, model);

        var selected = model.Animations.FirstOrDefault(animation => animation.Id == selectedAnimationId);
        AnimationListView.SelectedItem = selected ?? model.Animations.FirstOrDefault();
        UpdateAnimationLabel();
    }

    public int SelectedAnimationId =>
        AnimationListView.SelectedItem is FieldModelAnimationInfoPC animation
            ? animation.Id
            : -1;

    public int AnimationCount => AnimationListView.Items.Count;

    private void AnimationListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAnimationLabel();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void UpdateAnimationLabel()
    {
        AnimationLabel.Text = AnimationListView.SelectedItem is FieldModelAnimationInfoPC animation
            ? $"{_fieldName} - {_model.CharacterName} - animation {animation.Id}: {animation.Name}"
            : $"{_fieldName} - {_model.CharacterName} - no animation selected";
    }
}

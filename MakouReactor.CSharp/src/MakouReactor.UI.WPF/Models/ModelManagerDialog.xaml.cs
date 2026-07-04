using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

namespace MakouReactor.UI.WPF.Models;

public partial class ModelManagerDialog : Window
{
    private readonly ModelManagerViewModel _viewModel;

    public ModelManagerDialog(string fieldName, FieldPC field)
    {
        InitializeComponent();
        _viewModel = new ModelManagerViewModel(fieldName, field);
        DataContext = _viewModel;
        StatusLabel.SetBinding(System.Windows.Controls.TextBlock.TextProperty, "StatusText");
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ModelManagerViewModel.SelectedModel) or nameof(ModelManagerViewModel.PreviewText))
                UpdatePreview();
        };
        UpdatePreview();
    }

    private void AnimationListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection state is owned by the view model; this bridge remains for WPF selection updates.
    }

    private void AnimationListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenAnimationSelector();
    }

    private void SelectAnimationButton_Click(object sender, RoutedEventArgs e)
    {
        OpenAnimationSelector();
    }

    private void OpenAnimationSelector()
    {
        if (_viewModel.SelectedModel == null)
            return;

        var model = ToModelInfo(_viewModel.SelectedModel);
        var selectedAnimationId = _viewModel.SelectedAnimation is { } animation
            ? animation.Id
            : 0;
        var dialog = new AnimationSelectorDialog(_viewModel.FieldName, model, selectedAnimationId)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
        {
            var selected = _viewModel.SelectedModel.Animations
                .FirstOrDefault(animation => animation.Id == dialog.SelectedAnimationId);
            if (selected != null)
                _viewModel.SelectedAnimation = selected;
        }
    }

    private void ApplyModelLoader_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ApplyChanges();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        ModelPreview.ShowModel(_viewModel.FieldName,
            _viewModel.SelectedModel == null ? null : ToModelInfo(_viewModel.SelectedModel));
    }

    private static FieldModelInfoPC ToModelInfo(ModelEntryViewModel model) =>
        new(
            model.Id,
            model.CharacterName,
            model.HrcName,
            model.Unknown,
            model.Scale,
            new RgbColor(model.GlobalR, model.GlobalG, model.GlobalB),
            model.Animations.Select(animation => new FieldModelAnimationInfoPC(
                animation.Id,
                animation.Name,
                animation.Unknown)).ToArray(),
            model.LightingData);
}

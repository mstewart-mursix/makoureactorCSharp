using CommunityToolkit.Mvvm.ComponentModel;

using MakouReactor.Core.Models;

namespace MakouReactor.UI.WPF.ViewModels;

public sealed partial class ModelManagerViewModel : ObservableObject
{
    private readonly FieldPC _field;
    private readonly FieldModelLoaderPC _modelLoader;

    [ObservableProperty]
    private ModelEntryViewModel? _selectedModel;

    [ObservableProperty]
    private ModelAnimationEntryViewModel? _selectedAnimation;

    [ObservableProperty]
    private bool _isModified;

    public ModelManagerViewModel(string fieldName, FieldPC field)
    {
        if (field.ModelLoader == null)
            throw new ArgumentException("Field does not contain parsed model-loader data.", nameof(field));

        _field = field;
        _modelLoader = field.ModelLoader;
        FieldName = fieldName;
        Models = _modelLoader.Models.Select(model => new ModelEntryViewModel(model)).ToArray();
        foreach (var model in Models)
        {
            model.PropertyChanged += (_, _) =>
            {
                IsModified = true;
                OnPropertyChanged(nameof(PreviewText));
                OnPropertyChanged(nameof(SelectedModelAnimationCount));
            };
            foreach (var animation in model.Animations)
            {
                animation.PropertyChanged += (_, _) =>
                {
                    IsModified = true;
                    OnPropertyChanged(nameof(PreviewText));
                };
            }
        }

        SelectedModel = Models.FirstOrDefault();
    }

    public string FieldName { get; }

    public IReadOnlyList<ModelEntryViewModel> Models { get; }

    public int SelectedModelAnimationCount => SelectedModel?.Animations.Count ?? 0;

    public bool CanSelectAnimation => SelectedModelAnimationCount > 0 && SelectedAnimation != null;

    public string StatusText =>
        $"{Models.Count} model{(Models.Count == 1 ? string.Empty : "s")} loaded" +
        $"{(IsModified ? "; unapplied model-loader changes" : string.Empty)}.";

    public string PreviewText => SelectedModel == null
        ? "Select a model."
        : $"Model {SelectedModel.Id}\n" +
          $"HRC: {SelectedModel.HrcName}\n" +
          $"Scale: {SelectedModel.Scale}\n" +
          $"Global color: #{SelectedModel.GlobalR:X2}{SelectedModel.GlobalG:X2}{SelectedModel.GlobalB:X2}\n" +
          $"Animations: {SelectedModel.Animations.Count}";

    partial void OnSelectedModelChanged(ModelEntryViewModel? value)
    {
        SelectedAnimation = value?.Animations.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedModelAnimationCount));
        OnPropertyChanged(nameof(CanSelectAnimation));
        OnPropertyChanged(nameof(PreviewText));
    }

    partial void OnSelectedAnimationChanged(ModelAnimationEntryViewModel? value)
    {
        OnPropertyChanged(nameof(CanSelectAnimation));
    }

    partial void OnIsModifiedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    public void ApplyChanges()
    {
        _modelLoader.ReplaceModels(Models.Select(model => new FieldModelInfoPC(
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
            model.LightingData)));
        _field.ApplyModelLoaderChanges();
        IsModified = false;
    }
}

public sealed partial class ModelEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _characterName;

    [ObservableProperty]
    private string _hrcName;

    [ObservableProperty]
    private ushort _unknown;

    [ObservableProperty]
    private ushort _scale;

    [ObservableProperty]
    private byte _globalR;

    [ObservableProperty]
    private byte _globalG;

    [ObservableProperty]
    private byte _globalB;

    public ModelEntryViewModel(FieldModelInfoPC model)
    {
        Id = model.Id;
        _characterName = model.CharacterName;
        _hrcName = model.HrcName;
        _unknown = model.Unknown;
        _scale = model.Scale;
        _globalR = model.GlobalColor.R;
        _globalG = model.GlobalColor.G;
        _globalB = model.GlobalColor.B;
        LightingData = model.LightingData?.ToArray();
        Animations = model.Animations
            .Select(animation => new ModelAnimationEntryViewModel(animation))
            .ToArray();
    }

    public int Id { get; }

    public byte[]? LightingData { get; }

    public IReadOnlyList<ModelAnimationEntryViewModel> Animations { get; }
}

public sealed partial class ModelAnimationEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private ushort _unknown;

    public ModelAnimationEntryViewModel(FieldModelAnimationInfoPC animation)
    {
        Id = animation.Id;
        _name = animation.Name;
        _unknown = animation.Unknown;
    }

    public int Id { get; }
}

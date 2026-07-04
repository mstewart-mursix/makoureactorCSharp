using CommunityToolkit.Mvvm.ComponentModel;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

namespace MakouReactor.UI.WPF.ViewModels;

public sealed partial class PlayStationModelManagerViewModel : ObservableObject
{
    private readonly FieldPS? _field;
    private readonly FieldModelLoaderPS _modelLoader;
    private readonly BsxModelCatalog? _modelCatalog;

    [ObservableProperty]
    private PlayStationModelEntryViewModel? _selectedModel;

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private int _selectedAnimationIndex;

    public PlayStationModelManagerViewModel(
        string fieldName,
        FieldModelLoaderPS modelLoader,
        FieldPS? field = null,
        ReadOnlySpan<byte> bsxData = default)
    {
        _field = field;
        _modelLoader = modelLoader;
        if (!bsxData.IsEmpty)
            _modelCatalog = BsxModelCatalog.Open(bsxData);
        FieldName = fieldName;
        Models = modelLoader.Models
            .Select(model => new PlayStationModelEntryViewModel(
                model.Id,
                model.ModelId,
                model.FaceId,
                model.BonesCount,
                model.PartsCount,
                model.AnimationCount,
                model.Unknown1,
                model.Unknown2,
                model.Unknown3))
            .ToArray();
        foreach (var model in Models)
        {
            model.PropertyChanged += (_, _) =>
            {
                IsModified = true;
                OnPropertyChanged(nameof(PreviewText));
            };
        }
        SelectedModel = Models.FirstOrDefault();
    }

    public string FieldName { get; }

    public IReadOnlyList<PlayStationModelEntryViewModel> Models { get; }

    public string StatusText =>
        $"{Models.Count} PlayStation model{(Models.Count == 1 ? string.Empty : "s")} loaded" +
        $"{(IsModified ? "; unapplied model-loader changes" : string.Empty)}.";

    public string PreviewText => SelectedModel == null
        ? "Select a PlayStation model loader row."
        : $"Model {SelectedModel.Id}\n" +
          $"Model id: {SelectedModel.ModelId}\n" +
          $"Animations: {SelectedModel.AnimationCount}\n" +
          $"BSX animations: {BsxAnimationCountForSelection()}\n\n" +
          AnimationExportStatusText;

    public bool CanExportAnimation =>
        SelectedModel != null &&
        _modelCatalog != null &&
        SelectedModel.Id < _modelCatalog.Models.Count &&
        SelectedAnimationIndex >= 0 &&
        SelectedAnimationIndex < _modelCatalog.Models[SelectedModel.Id].AnimationCount;

    public IReadOnlyList<int> AvailableAnimationIndices =>
        Enumerable.Range(0, BsxAnimationCountForSelection()).ToArray();

    public string AnimationExportStatusText => CanExportAnimation
        ? "Animation 0 can be exported as a PC .a file."
        : "Animation export requires the companion BSX model file for this field.";

    partial void OnSelectedModelChanged(PlayStationModelEntryViewModel? value)
    {
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(CanExportAnimation));
        OnPropertyChanged(nameof(AnimationExportStatusText));
        OnPropertyChanged(nameof(AvailableAnimationIndices));
        SelectedAnimationIndex = 0;
    }

    partial void OnSelectedAnimationIndexChanged(int value)
    {
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(CanExportAnimation));
        OnPropertyChanged(nameof(AnimationExportStatusText));
    }

    partial void OnIsModifiedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    public void ApplyChanges()
    {
        _modelLoader.ReplaceModels(Models.Select(model => new FieldModelInfoPS(
            model.Id,
            model.FaceId,
            model.BonesCount,
            model.PartsCount,
            model.AnimationCount,
            model.Unknown1,
            model.Unknown2,
            model.Unknown3,
            model.ModelId)));

        _field?.SetSectionData(FieldSection.ModelLoader, _modelLoader.Save());
        IsModified = false;
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(CanExportAnimation));
    }

    public byte[] ExportSelectedAnimation()
    {
        if (SelectedModel == null)
            throw new InvalidOperationException("No PlayStation model is selected.");
        if (_modelCatalog == null)
            throw new InvalidOperationException("The companion BSX model file is not loaded.");

        return _modelCatalog.ExportPcAnimation(SelectedModel.Id, SelectedAnimationIndex);
    }

    public string DefaultAnimationExportFileName()
    {
        if (SelectedModel == null)
            return $"{FieldName}-model-0-animation-{SelectedAnimationIndex}.a";

        return $"{FieldName}-model-{SelectedModel.Id}-animation-{SelectedAnimationIndex}.a";
    }

    private int BsxAnimationCountForSelection()
    {
        if (SelectedModel == null || _modelCatalog == null || SelectedModel.Id >= _modelCatalog.Models.Count)
            return 0;

        return _modelCatalog.Models[SelectedModel.Id].AnimationCount;
    }
}

public sealed partial class PlayStationModelEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private byte _modelId;

    [ObservableProperty]
    private byte _faceId;

    [ObservableProperty]
    private byte _bonesCount;

    [ObservableProperty]
    private byte _partsCount;

    [ObservableProperty]
    private byte _animationCount;

    [ObservableProperty]
    private byte _unknown1;

    [ObservableProperty]
    private byte _unknown2;

    [ObservableProperty]
    private byte _unknown3;

    public PlayStationModelEntryViewModel(
        int id,
        byte modelId,
        byte faceId,
        byte bonesCount,
        byte partsCount,
        byte animationCount,
        byte unknown1,
        byte unknown2,
        byte unknown3)
    {
        Id = id;
        _modelId = modelId;
        _faceId = faceId;
        _bonesCount = bonesCount;
        _partsCount = partsCount;
        _animationCount = animationCount;
        _unknown1 = unknown1;
        _unknown2 = unknown2;
        _unknown3 = unknown3;
    }

    public int Id { get; }
}

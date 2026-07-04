using CommunityToolkit.Mvvm.ComponentModel;

using MakouReactor.Core.Models;

using System.IO;

namespace MakouReactor.UI.WPF.ViewModels;

public sealed partial class MiscellaneousViewModel : ObservableObject
{
    private readonly FieldPC _field;
    private readonly Section1File? _section;

    [ObservableProperty]
    private string _mapName;

    [ObservableProperty]
    private string _control;

    [ObservableProperty]
    private string _cameraFocusHeight;

    [ObservableProperty]
    private string _cameraRangeLeft;

    [ObservableProperty]
    private string _cameraRangeTop;

    [ObservableProperty]
    private string _cameraRangeRight;

    [ObservableProperty]
    private string _cameraRangeBottom;

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    public MiscellaneousViewModel(string fieldName, FieldPC field, Section1File? section)
    {
        if (field.Inf == null)
            throw new ArgumentException("Field does not contain parsed INF data.", nameof(field));

        _field = field;
        _section = section;
        FieldName = fieldName;
        var inf = field.Inf;
        _mapName = inf.MapName;
        _control = $"0x{inf.Control:X2}";
        _cameraFocusHeight = inf.CameraFocusHeight.ToString();
        _cameraRangeLeft = inf.CameraRange.Left.ToString();
        _cameraRangeTop = inf.CameraRange.Top.ToString();
        _cameraRangeRight = inf.CameraRange.Right.ToString();
        _cameraRangeBottom = inf.CameraRange.Bottom.ToString();
    }

    public string FieldName { get; }

    public string Author => _section?.Author ?? string.Empty;

    public InfFile Inf => _field.Inf!;

    public string CameraRangeDisplay =>
        $"{CameraRangeLeft}, {CameraRangeTop}, {CameraRangeRight}, {CameraRangeBottom}";

    partial void OnMapNameChanged(string value) => MarkModified();
    partial void OnControlChanged(string value) => MarkModified();
    partial void OnCameraFocusHeightChanged(string value) => MarkModified();
    partial void OnCameraRangeLeftChanged(string value) => MarkModified();
    partial void OnCameraRangeTopChanged(string value) => MarkModified();
    partial void OnCameraRangeRightChanged(string value) => MarkModified();
    partial void OnCameraRangeBottomChanged(string value) => MarkModified();

    public bool ApplyChanges()
    {
        ValidationMessage = string.Empty;
        if (!byte.TryParse(Control.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Control[2..] : Control,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var control))
        {
            ValidationMessage = "Control must be a byte value such as 0x80.";
            return false;
        }

        if (!short.TryParse(CameraFocusHeight, out var focus) ||
            !short.TryParse(CameraRangeLeft, out var left) ||
            !short.TryParse(CameraRangeTop, out var top) ||
            !short.TryParse(CameraRangeRight, out var right) ||
            !short.TryParse(CameraRangeBottom, out var bottom))
        {
            ValidationMessage = "Camera focus and range values must be signed 16-bit integers.";
            return false;
        }

        try
        {
            _field.Inf!.SetGeneralMetadata(MapName, control, focus, new InfRange(left, top, right, bottom));
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            ValidationMessage = ex.Message;
            return false;
        }

        _field.ApplyInfChanges();
        IsModified = false;
        OnPropertyChanged(nameof(CameraRangeDisplay));
        return true;
    }

    private void MarkModified()
    {
        IsModified = true;
        ValidationMessage = string.Empty;
        OnPropertyChanged(nameof(CameraRangeDisplay));
    }
}

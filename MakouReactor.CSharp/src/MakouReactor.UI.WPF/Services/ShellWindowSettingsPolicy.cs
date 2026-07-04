using MakouReactor.Core.Services;

namespace MakouReactor.UI.WPF.Services;

public sealed record ShellWindowStateSnapshot(
    double Width,
    double Height,
    double Left,
    double Top,
    double RestoreWidth,
    double RestoreHeight,
    double RestoreLeft,
    double RestoreTop,
    double MinWidth,
    double MinHeight,
    double LeftPanelActualWidth,
    double LeftPanelWidth,
    double PreviewRowActualHeight,
    double PreviewRowHeight,
    double PreviousPreviewPanelHeight,
    bool FieldListVisible,
    bool PreviewVisible,
    string PreviewMode,
    int SelectedMainTab,
    bool JapaneseText);

public static class ShellWindowSettingsPolicy
{
    public static void Apply(AppSettings settings, ShellWindowStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.JapaneseText = snapshot.JapaneseText;
        settings.Window.Width = ValidPositive(snapshot.RestoreWidth)
            ? Math.Max(snapshot.MinWidth, snapshot.RestoreWidth)
            : ValidPositive(snapshot.Width) ? Math.Max(snapshot.MinWidth, snapshot.Width) : snapshot.MinWidth;
        settings.Window.Height = ValidPositive(snapshot.RestoreHeight)
            ? Math.Max(snapshot.MinHeight, snapshot.RestoreHeight)
            : ValidPositive(snapshot.Height) ? Math.Max(snapshot.MinHeight, snapshot.Height) : snapshot.MinHeight;
        settings.Window.Left = ValidNonNegative(snapshot.RestoreLeft)
            ? snapshot.RestoreLeft
            : ValidNonNegative(snapshot.Left) ? snapshot.Left : -1;
        settings.Window.Top = ValidNonNegative(snapshot.RestoreTop)
            ? snapshot.RestoreTop
            : ValidNonNegative(snapshot.Top) ? snapshot.Top : -1;
        settings.Window.LeftPanelWidth = snapshot.LeftPanelActualWidth > 0
            ? snapshot.LeftPanelActualWidth
            : snapshot.LeftPanelWidth;
        if (snapshot.PreviewVisible)
            settings.Window.PreviewPanelHeight = CurrentPreviewPanelHeight(snapshot);
        settings.Window.FieldListVisible = snapshot.FieldListVisible;
        settings.Window.PreviewVisible = snapshot.PreviewVisible;
        settings.Window.PreviewMode = NormalizePreviewMode(snapshot.PreviewMode);
        settings.Window.SelectedMainTab = snapshot.SelectedMainTab;
    }

    public static string NormalizePreviewMode(string previewMode) =>
        previewMode.Equals("Model", StringComparison.OrdinalIgnoreCase)
            ? "Model"
            : "Background";

    private static double CurrentPreviewPanelHeight(ShellWindowStateSnapshot snapshot)
    {
        if (snapshot.PreviewRowActualHeight > 0)
            return snapshot.PreviewRowActualHeight;

        return snapshot.PreviewRowHeight > 0
            ? snapshot.PreviewRowHeight
            : snapshot.PreviousPreviewPanelHeight;
    }

    private static bool ValidPositive(double value) => double.IsFinite(value) && value > 0;

    private static bool ValidNonNegative(double value) => double.IsFinite(value) && value >= 0;
}

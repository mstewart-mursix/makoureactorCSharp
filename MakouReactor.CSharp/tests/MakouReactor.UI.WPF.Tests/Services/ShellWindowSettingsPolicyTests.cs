using FluentAssertions;

using MakouReactor.Core.Services;
using MakouReactor.UI.WPF.Services;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Services;

public sealed class ShellWindowSettingsPolicyTests
{
    [Fact]
    public void apply_prefers_restore_bounds_and_actual_splitter_sizes()
    {
        var settings = new AppSettings
        {
            Window =
            {
                PreviewPanelHeight = 250,
            },
        };

        ShellWindowSettingsPolicy.Apply(settings,
            new ShellWindowStateSnapshot(
                Width: 800,
                Height: 600,
                Left: 5,
                Top: 6,
                RestoreWidth: 1200,
                RestoreHeight: 900,
                RestoreLeft: 20,
                RestoreTop: 30,
                MinWidth: 900,
                MinHeight: 650,
                LeftPanelActualWidth: 321,
                LeftPanelWidth: 222,
                PreviewRowActualHeight: 180,
                PreviewRowHeight: 140,
                PreviousPreviewPanelHeight: 250,
                FieldListVisible: true,
                PreviewVisible: true,
                PreviewMode: "model",
                SelectedMainTab: 1,
                JapaneseText: true));

        settings.JapaneseText.Should().BeTrue();
        settings.Window.Width.Should().Be(1200);
        settings.Window.Height.Should().Be(900);
        settings.Window.Left.Should().Be(20);
        settings.Window.Top.Should().Be(30);
        settings.Window.LeftPanelWidth.Should().Be(321);
        settings.Window.PreviewPanelHeight.Should().Be(180);
        settings.Window.FieldListVisible.Should().BeTrue();
        settings.Window.PreviewVisible.Should().BeTrue();
        settings.Window.PreviewMode.Should().Be("Model");
        settings.Window.SelectedMainTab.Should().Be(1);
    }

    [Fact]
    public void apply_falls_back_to_window_values_and_previous_preview_height()
    {
        var settings = new AppSettings();

        ShellWindowSettingsPolicy.Apply(settings,
            new ShellWindowStateSnapshot(
                Width: 700,
                Height: double.NaN,
                Left: -10,
                Top: 42,
                RestoreWidth: double.NaN,
                RestoreHeight: -1,
                RestoreLeft: -1,
                RestoreTop: double.NaN,
                MinWidth: 900,
                MinHeight: 650,
                LeftPanelActualWidth: 0,
                LeftPanelWidth: 210,
                PreviewRowActualHeight: 0,
                PreviewRowHeight: 0,
                PreviousPreviewPanelHeight: 240,
                FieldListVisible: false,
                PreviewVisible: true,
                PreviewMode: "other",
                SelectedMainTab: 0,
                JapaneseText: false));

        settings.Window.Width.Should().Be(900);
        settings.Window.Height.Should().Be(650);
        settings.Window.Left.Should().Be(-1);
        settings.Window.Top.Should().Be(42);
        settings.Window.LeftPanelWidth.Should().Be(210);
        settings.Window.PreviewPanelHeight.Should().Be(240);
        settings.Window.FieldListVisible.Should().BeFalse();
        settings.Window.PreviewMode.Should().Be("Background");
    }

    [Fact]
    public void apply_preserves_previous_preview_height_when_preview_is_hidden()
    {
        var settings = new AppSettings
        {
            Window =
            {
                PreviewPanelHeight = 333,
            },
        };

        ShellWindowSettingsPolicy.Apply(settings,
            new ShellWindowStateSnapshot(
                Width: 1000,
                Height: 700,
                Left: 1,
                Top: 2,
                RestoreWidth: 1000,
                RestoreHeight: 700,
                RestoreLeft: 1,
                RestoreTop: 2,
                MinWidth: 900,
                MinHeight: 650,
                LeftPanelActualWidth: 0,
                LeftPanelWidth: 220,
                PreviewRowActualHeight: 200,
                PreviewRowHeight: 200,
                PreviousPreviewPanelHeight: 333,
                FieldListVisible: true,
                PreviewVisible: false,
                PreviewMode: "Background",
                SelectedMainTab: 0,
                JapaneseText: false));

        settings.Window.PreviewPanelHeight.Should().Be(333);
        settings.Window.PreviewVisible.Should().BeFalse();
    }
}

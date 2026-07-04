using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using FluentAssertions;

using MakouReactor.Core.Services;
using MakouReactor.UI.WPF.Settings;

using Xunit;

namespace MakouReactor.UI.WPF.Tests;

public sealed class WindowScreenshotSmokeTests
{
    [Fact]
    public void main_window_renders_nonblank_screenshot_at_minimum_size()
    {
        RunSta(() =>
        {
            var window = new MainWindow(CreateSession())
            {
                Width = 900,
                Height = 650,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10_000,
                Top = -10_000,
                ShowActivated = false,
            };

            try
            {
                var screenshot = CaptureWindow(window, 900, 650, "main-window-minimum.png");

                screenshot.Exists.Should().BeTrue();
                screenshot.Length.Should().BeGreaterThan(10_000);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void main_window_minimum_size_keeps_visible_text_and_toolbar_controls_from_overlapping()
    {
        RunSta(() =>
        {
            var window = new MainWindow(CreateSession())
            {
                Width = 900,
                Height = 650,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10_000,
                Top = -10_000,
                ShowActivated = false,
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                var clippedText = FindDescendants<TextBlock>(window)
                    .Where(static text => text.IsVisible && !string.IsNullOrWhiteSpace(text.Text))
                    .Where(static text => text.ActualWidth > 0 && text.ActualHeight > 0)
                    .Where(static text =>
                        text.TextWrapping == TextWrapping.NoWrap &&
                        RenderedTextWidth(text) > text.ActualWidth + 1)
                    .Select(static text =>
                        $"{(text.Name.Length > 0 ? text.Name : text.Text)} text={RenderedTextWidth(text):N1} actual={text.ActualWidth:N1}")
                    .ToArray();

                clippedText.Should().BeEmpty();

                var toolbarControlNames = new[]
                {
                    "OpenToolButton",
                    "SaveToolButton",
                    "FindToolButton",
                    "RunFf7ToolButton",
                    "TextsToolButton",
                    "ModelsToolButton",
                    "WalkmeshToolButton",
                    "LlmGenerateToolButton",
                    "HeaderTabControl",
                    "ArchiveLabel",
                };
                var toolbarRects = toolbarControlNames
                    .Select(name => (Name: name, Rect: BoundsInWindow(Find<FrameworkElement>(window, name), window)))
                    .Where(static item => item.Rect.Width > 0 && item.Rect.Height > 0)
                    .OrderBy(static item => item.Rect.Left)
                    .ToArray();

                for (var index = 1; index < toolbarRects.Length; index++)
                {
                    toolbarRects[index - 1].Rect.Right.Should().BeLessThanOrEqualTo(
                        toolbarRects[index].Rect.Left + 1,
                        $"{toolbarRects[index - 1].Name} should not overlap {toolbarRects[index].Name}");
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void settings_dialog_renders_nonblank_screenshot()
    {
        RunSta(() =>
        {
            var window = new SettingsDialog(new AppSettings())
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10_000,
                Top = -10_000,
                ShowActivated = false,
            };

            try
            {
                var screenshot = CaptureWindow(window, 720, 520, "settings-dialog.png");

                screenshot.Exists.Should().BeTrue();
                screenshot.Length.Should().BeGreaterThan(10_000);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static FileInfo CaptureWindow(Window window, int width, int height, string fileName)
    {
        window.Width = width;
        window.Height = height;
        window.Show();
        window.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);

        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        pixels.Should().Contain(pixel => pixel != 0);

        var directory = Path.Combine(Path.GetTempPath(), "MakouReactor.WpfScreenshots");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path))
            encoder.Save(stream);

        return new FileInfo(path);
    }

    private static ShellSessionService CreateSession()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            "MakouReactor.WpfTests",
            Guid.NewGuid().ToString("N"),
            "settings.json");
        return new ShellSessionService(new JsonAppSettingsService(settingsPath));
    }

    private static Rect BoundsInWindow(FrameworkElement element, Window window)
    {
        var transform = element.TransformToAncestor(window);
        return transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    }

    private static double RenderedTextWidth(TextBlock text)
    {
        var dpi = VisualTreeHelper.GetDpi(text);
        var formatted = new FormattedText(
            text.Text,
            CultureInfo.CurrentUICulture,
            text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize,
            Brushes.Black,
            dpi.PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    private static T Find<T>(FrameworkElement root, string name)
        where T : class
    {
        root.FindName(name).Should().BeAssignableTo<T>();
        return (T)root.FindName(name);
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindDescendants<T>(child))
                yield return descendant;
        }
    }

    private static void RunSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }
}

using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using FluentAssertions;

using MakouReactor.Core.Services;
using MakouReactor.UI.WPF.Shell;

using Xunit;

namespace MakouReactor.UI.WPF.Tests;

public sealed class MainWindowStartupTests
{
    [Fact]
    public void startup_shell_uses_makou_reactor_title()
    {
        RunSta(() =>
        {
            var window = CreateWindow();
            try
            {
                window.Title.Should().Be("Makou Reactor");
                window.MinWidth.Should().Be(900);
                window.MinHeight.Should().Be(650);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void startup_shell_disables_archive_and_field_actions_without_open_archive()
    {
        RunSta(() =>
        {
            var window = CreateWindow();
            try
            {
                Find<MenuItem>(window, "SaveMenuItem").IsEnabled.Should().BeFalse();
                Find<MenuItem>(window, "SaveAsMenuItem").IsEnabled.Should().BeFalse();
                Find<MenuItem>(window, "ExportCurrentMapMenuItem").IsEnabled.Should().BeFalse();
                Find<MenuItem>(window, "ImportCurrentMapMenuItem").IsEnabled.Should().BeFalse();
                Find<MenuItem>(window, "MassExportMenuItem").IsEnabled.Should().BeFalse();
                Find<MenuItem>(window, "RunFf7MenuItem").IsEnabled.Should().BeFalse();
                Find<MenuItem>(window, "CloseArchiveMenuItem").IsEnabled.Should().BeFalse();
                Find<MenuItem>(window, "TextsMenuItem").IsEnabled.Should().BeFalse();
                Find<MenuItem>(window, "ModelsMenuItem").IsEnabled.Should().BeFalse();
                Find<MenuItem>(window, "WalkmeshMenuItem").IsEnabled.Should().BeFalse();
                Find<MenuItem>(window, "BackgroundMenuItem").IsEnabled.Should().BeFalse();
                Find<MenuItem>(window, "LlmGenerateMenuItem").IsEnabled.Should().BeFalse();
                Find<MenuItem>(window, "RevertLlmApplyMenuItem").IsEnabled.Should().BeFalse();
                Find<Button>(window, "SaveToolButton").IsEnabled.Should().BeFalse();
                Find<Button>(window, "RunFf7ToolButton").IsEnabled.Should().BeFalse();
                Find<Button>(window, "TextsToolButton").IsEnabled.Should().BeFalse();
                Find<Button>(window, "ModelsToolButton").IsEnabled.Should().BeFalse();
                Find<Button>(window, "WalkmeshToolButton").IsEnabled.Should().BeFalse();
                Find<Button>(window, "LlmGenerateToolButton").IsEnabled.Should().BeFalse();
                Find<Button>(window, "RevertLlmApplyToolButton").IsEnabled.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void startup_shell_binds_primary_menu_and_toolbar_actions_to_wpf_commands()
    {
        RunSta(() =>
        {
            var window = CreateWindow();
            try
            {
                Find<MenuItem>(window, "SaveMenuItem").Command.Should().Be(ShellCommands.Save);
                Find<MenuItem>(window, "SaveAsMenuItem").Command.Should().Be(ShellCommands.SaveAs);
                Find<MenuItem>(window, "ExportCurrentMapMenuItem").Command.Should().Be(ShellCommands.ExportCurrentMap);
                Find<MenuItem>(window, "ImportCurrentMapMenuItem").Command.Should().Be(ShellCommands.ImportCurrentMap);
                Find<MenuItem>(window, "RunFf7MenuItem").Command.Should().Be(ShellCommands.RunFf7);
                Find<MenuItem>(window, "TextsMenuItem").Command.Should().Be(ShellCommands.OpenTexts);
                Find<MenuItem>(window, "FindMenuItem").Command.Should().Be(ShellCommands.OpenFind);
                Find<MenuItem>(window, "LlmGenerateMenuItem").Command.Should().Be(ShellCommands.OpenLlmGenerator);
                Find<MenuItem>(window, "RevertLlmApplyMenuItem").Command.Should().Be(ShellCommands.RevertLlmApply);
                Find<Button>(window, "SaveToolButton").Command.Should().Be(ShellCommands.Save);
                Find<Button>(window, "TextsToolButton").Command.Should().Be(ShellCommands.OpenTexts);
                Find<Button>(window, "LlmGenerateToolButton").Command.Should().Be(ShellCommands.OpenLlmGenerator);
                Find<Button>(window, "RevertLlmApplyToolButton").Command.Should().Be(ShellCommands.RevertLlmApply);

                ShellCommands.Save.InputGestures.Cast<KeyGesture>().Should().ContainSingle()
                    .Which.Key.Should().Be(Key.S);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void startup_shell_populates_recent_files_menu_when_saved_recent_file_exists()
    {
        RunSta(() =>
        {
            var recentPath = Path.Combine(
                Path.GetTempPath(),
                "MakouReactor.WpfTests",
                Guid.NewGuid().ToString("N"),
                "flevel.lgp");
            Directory.CreateDirectory(Path.GetDirectoryName(recentPath)!);
            File.WriteAllBytes(recentPath, [0]);

            var window = CreateWindow(new AppSettings { RecentFiles = [recentPath] });
            try
            {
                var menu = Find<MenuItem>(window, "RecentFilesMenu");

                menu.IsEnabled.Should().BeTrue();
                menu.Items.OfType<MenuItem>().First().Header.Should().Be(recentPath);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void startup_shell_reopens_with_saved_geometry_splitters_tabs_and_recent_files()
    {
        RunSta(() =>
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "MakouReactor.WpfTests",
                Guid.NewGuid().ToString("N"));
            var settingsPath = Path.Combine(directory, "settings.json");
            var recentPath = Path.Combine(directory, "flevel.lgp");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(recentPath, [0]);

            var settingsService = new JsonAppSettingsService(settingsPath);
            settingsService.Save(new AppSettings { RecentFiles = [recentPath] });

            var first = new MainWindow(new ShellSessionService(settingsService))
            {
                Width = 1111,
                Height = 777,
            };
            try
            {
                Find<ColumnDefinition>(first, "LeftPanelColumn").Width = new GridLength(333);
                Find<RowDefinition>(first, "PreviewRow").Height = new GridLength(222);
                Find<TabControl>(first, "MainTabControl").SelectedIndex = 1;
            }
            finally
            {
                first.Close();
            }

            var second = new MainWindow(new ShellSessionService(new JsonAppSettingsService(settingsPath)));
            try
            {
                second.Width.Should().Be(1111);
                second.Height.Should().Be(777);
                Find<ColumnDefinition>(second, "LeftPanelColumn").Width.Value.Should().Be(333);
                Find<RowDefinition>(second, "PreviewRow").Height.Value.Should().Be(222);
                Find<TabControl>(second, "MainTabControl").SelectedIndex.Should().Be(1);

                var recentMenu = Find<MenuItem>(second, "RecentFilesMenu");
                recentMenu.IsEnabled.Should().BeTrue();
                recentMenu.Items.OfType<MenuItem>().First().Header.Should().Be(recentPath);
            }
            finally
            {
                second.Close();
            }
        });
    }

    [Fact]
    public void startup_shell_toolbar_buttons_have_tooltips()
    {
        RunSta(() =>
        {
            var window = CreateWindow();
            try
            {
                var toolbarButtons = new[]
                {
                    "OpenToolButton",
                    "SaveToolButton",
                    "FindToolButton",
                    "RunFf7ToolButton",
                    "TextsToolButton",
                    "ModelsToolButton",
                    "WalkmeshToolButton",
                    "LlmGenerateToolButton",
                    "RevertLlmApplyToolButton",
                };

                foreach (var name in toolbarButtons)
                {
                    var button = Find<Button>(window, name);
                    button.ToolTip.Should().BeOfType<string>()
                        .Which.Should().NotBeNullOrWhiteSpace();
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void startup_shell_uses_compact_toolbar_buttons()
    {
        RunSta(() =>
        {
            var window = CreateWindow();
            try
            {
                var compactButtons = new[]
                {
                    "OpenToolButton",
                    "SaveToolButton",
                    "FindToolButton",
                    "RunFf7ToolButton",
                    "TextsToolButton",
                    "ModelsToolButton",
                    "WalkmeshToolButton",
                    "RevertLlmApplyToolButton",
                };

                foreach (var name in compactButtons)
                {
                    var button = Find<Button>(window, name);
                    button.Width.Should().Be(24);
                    button.Height.Should().Be(24);
                }

                var llmButton = Find<Button>(window, "LlmGenerateToolButton");
                llmButton.Width.Should().BeLessThanOrEqualTo(32);
                llmButton.Height.Should().Be(24);
                llmButton.Content.Should().Be("AI");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void startup_shell_uses_compact_desktop_density_and_fits_supported_sizes()
    {
        RunSta(() =>
        {
            var window = CreateWindow();
            try
            {
                window.Width.Should().Be(1280);
                window.Height.Should().Be(900);
                window.MinWidth.Should().Be(900);
                window.MinHeight.Should().Be(650);
                Find<ColumnDefinition>(window, "LeftPanelColumn").Width.Value.Should().BeLessThanOrEqualTo(260);
                Find<TabControl>(window, "HeaderTabControl").Height.Should().BeLessThanOrEqualTo(28);
                Find<TextBlock>(window, "ArchiveLabel").Margin.Left.Should().BeLessThanOrEqualTo(16);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void startup_shell_avoids_oversized_typography()
    {
        RunSta(() =>
        {
            var window = CreateWindow();
            try
            {
                var textElements = FindLogicalDescendants<TextBlock>(window).ToArray();
                textElements.Should().NotBeEmpty();
                textElements
                    .Where(static text => text.FontSize > 0)
                    .Select(static text => text.FontSize)
                    .Should().OnlyContain(size => size <= 16);

                FindLogicalDescendants<Button>(window)
                    .Where(static button => button.FontSize > 0)
                    .Select(static button => button.FontSize)
                    .Should().OnlyContain(size => size <= 16);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void startup_shell_has_keyboard_focus_targets()
    {
        RunSta(() =>
        {
            var window = CreateWindow();
            try
            {
                Find<Button>(window, "OpenToolButton").Focusable.Should().BeTrue();
                Find<Button>(window, "FindToolButton").Focusable.Should().BeTrue();
                Find<TabControl>(window, "HeaderTabControl").Focusable.Should().BeTrue();
                Find<TabControl>(window, "MainTabControl").Focusable.Should().BeTrue();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void startup_shell_uses_standard_editor_chrome_and_split_panes()
    {
        RunSta(() =>
        {
            var window = CreateWindow();
            try
            {
                FindLogicalDescendants<Menu>(window).Should().NotBeEmpty();
                FindLogicalDescendants<ToolBarTray>(window).Should().NotBeEmpty();
                FindLogicalDescendants<StatusBar>(window).Should().NotBeEmpty();
                FindLogicalDescendants<GridSplitter>(window).Should().HaveCountGreaterThanOrEqualTo(2);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void startup_shell_keeps_primary_menu_and_tab_labels_close_to_original()
    {
        RunSta(() =>
        {
            var window = CreateWindow();
            try
            {
                var menu = FindLogicalDescendants<Menu>(window).Single();
                menu.Items.OfType<MenuItem>()
                    .Select(static item => item.Header?.ToString())
                    .Should().Equal("_File", "T_ools", "_Settings", "_View", "_?");

                Find<TabControl>(window, "MainTabControl")
                    .Items.OfType<TabItem>()
                    .Select(static item => item.Header?.ToString())
                    .Should().Equal("Field Scripts", "Archive Manager");

                Find<TabControl>(window, "HeaderTabControl")
                    .Items.OfType<TabItem>()
                    .Select(static item => item.Header?.ToString())
                    .Should().Equal("Field Scripts", "Archive Manager");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void startup_shell_applies_saved_language_to_visible_menu_chrome()
    {
        RunSta(() =>
        {
            var window = CreateWindow(new AppSettings { Language = "fr" });
            try
            {
                Find<MenuItem>(window, "FileMenuItem").Header.Should().Be("_Fichier");
                Find<MenuItem>(window, "ToolsMenuItem").Header.Should().Be("_Outils");
                Find<MenuItem>(window, "LanguageMenuItem").Header.Should().Be("_Langue");
                Find<MenuItem>(window, "FrenchLanguageMenuItem").IsChecked.Should().BeTrue();
                Find<MenuItem>(window, "EnglishLanguageMenuItem").IsChecked.Should().BeFalse();
                Find<Button>(window, "OpenToolButton").ToolTip.Should().Be("Ouvrir un fichier");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void startup_shell_falls_back_to_english_for_unsupported_language()
    {
        RunSta(() =>
        {
            var window = CreateWindow(new AppSettings { Language = "xx" });
            try
            {
                Find<MenuItem>(window, "FileMenuItem").Header.Should().Be("_File");
                Find<MenuItem>(window, "EnglishLanguageMenuItem").IsChecked.Should().BeTrue();
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static T Find<T>(MainWindow window, string name)
        where T : class
    {
        window.FindName(name).Should().BeOfType<T>();
        return (T)window.FindName(name);
    }

    private static MainWindow CreateWindow()
    {
        return CreateWindow(new AppSettings());
    }

    private static MainWindow CreateWindow(AppSettings settings)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            "MakouReactor.WpfTests",
            Guid.NewGuid().ToString("N"),
            "settings.json");
        var settingsService = new JsonAppSettingsService(settingsPath);
        settingsService.Save(settings);
        return new MainWindow(new ShellSessionService(settingsService));
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

    private static IEnumerable<T> FindLogicalDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
                yield return match;

            foreach (var descendant in FindLogicalDescendants<T>(child))
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

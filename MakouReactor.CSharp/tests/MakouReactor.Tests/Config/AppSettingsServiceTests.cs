using FluentAssertions;

using MakouReactor.Core.Services;

using Xunit;

namespace MakouReactor.Tests.Config;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public void save_and_load_round_trips_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "settings.json");
        var service = new JsonAppSettingsService(path);
        var settings = new AppSettings
        {
            RecentFiles = ["C:\\ff7\\flevel.lgp"],
            JapaneseText = true,
            Language = "fr",
            Ff7ExecutablePath = "C:\\ff7\\ff7.exe",
            Ff7DataPath = "C:\\ff7\\data",
            LastSelectedFields =
            {
                ["C:\\ff7\\flevel.lgp"] = "md1stin",
            },
            Llm =
            {
                Backend = "http",
                CodexExecutable = "C:\\tools\\codex.cmd",
                CodexModel = "gpt-5-codex",
                TimeoutMs = 120000,
                RepairAttempts = 3,
                Endpoint = "http://localhost:1234/v1/chat/completions",
                ApiKey = "test-key",
                Model = "local-model",
            },
            Window =
            {
                Width = 1024,
                Height = 768,
                Left = 10,
                Top = 20,
                LeftPanelWidth = 260,
                PreviewPanelHeight = 180,
                FieldListVisible = false,
                PreviewVisible = true,
                PreviewMode = "Model",
                SelectedMainTab = 1,
            },
        };

        service.Save(settings);

        var loaded = service.Load();
        loaded.RecentFiles.Should().Equal("C:\\ff7\\flevel.lgp");
        loaded.JapaneseText.Should().BeTrue();
        loaded.Language.Should().Be("fr");
        loaded.Ff7ExecutablePath.Should().Be("C:\\ff7\\ff7.exe");
        loaded.Ff7DataPath.Should().Be("C:\\ff7\\data");
        loaded.Window.PreviewPanelHeight.Should().Be(180);
        loaded.LastSelectedFields.Should().ContainKey("C:\\ff7\\flevel.lgp")
            .WhoseValue.Should().Be("md1stin");
        loaded.Llm.Backend.Should().Be("http");
        loaded.Llm.CodexExecutable.Should().Be("C:\\tools\\codex.cmd");
        loaded.Llm.CodexModel.Should().Be("gpt-5-codex");
        loaded.Llm.TimeoutMs.Should().Be(120000);
        loaded.Llm.RepairAttempts.Should().Be(3);
        loaded.Llm.Endpoint.Should().Be("http://localhost:1234/v1/chat/completions");
        loaded.Llm.ApiKey.Should().Be("test-key");
        loaded.Llm.Model.Should().Be("local-model");
        loaded.Window.Width.Should().Be(1024);
        loaded.Window.PreviewMode.Should().Be("Model");
        loaded.Window.SelectedMainTab.Should().Be(1);
    }

    [Fact]
    public void load_returns_defaults_when_settings_json_is_invalid()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, "{not valid json");

        var settings = new JsonAppSettingsService(path).Load();

        settings.RecentFiles.Should().BeEmpty();
        settings.Window.Width.Should().Be(1280);
        settings.Window.FieldListVisible.Should().BeTrue();
    }

    [Fact]
    public void load_migrates_legacy_llm_config_when_settings_are_default()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var settingsPath = Path.Combine(directory, "settings.json");
        File.WriteAllText(
            Path.Combine(directory, "llm.config.json"),
            """
            {
              "backend": "http",
              "codex_executable": "C:\\tools\\codex.cmd",
              "codex_model": "gpt-5-codex",
              "timeout_ms": 90000,
              "endpoint": "http://localhost:1234/v1",
              "api_key": "legacy-key",
              "model": "legacy-model"
            }
            """);

        var settings = new JsonAppSettingsService(settingsPath, appDirectory: directory).Load();

        settings.Llm.Backend.Should().Be("http");
        settings.Llm.CodexExecutable.Should().Be("C:\\tools\\codex.cmd");
        settings.Llm.CodexModel.Should().Be("gpt-5-codex");
        settings.Llm.TimeoutMs.Should().Be(90000);
        settings.Llm.Endpoint.Should().Be("http://localhost:1234/v1/chat/completions");
        settings.Llm.ApiKey.Should().Be("legacy-key");
        settings.Llm.Model.Should().Be("legacy-model");
        File.Exists(settingsPath).Should().BeFalse("loading migration should not write settings.json until the app saves");
    }

    [Fact]
    public void load_preserves_existing_llm_settings_over_legacy_config()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var settingsPath = Path.Combine(directory, "settings.json");
        File.WriteAllText(
            Path.Combine(directory, "llm.config.json"),
            """
            {
              "backend": "http",
              "model": "legacy-model"
            }
            """);
        var service = new JsonAppSettingsService(settingsPath, appDirectory: directory);
        service.Save(new AppSettings
        {
            Llm =
            {
                Backend = "codex",
                CodexModel = "existing-model",
            },
        });

        var settings = service.Load();

        settings.Llm.Backend.Should().Be("codex");
        settings.Llm.CodexModel.Should().Be("existing-model");
        settings.Llm.Model.Should().Be("qwen/qwen3-coder-30b");
    }

    [Fact]
    public void load_ignores_invalid_legacy_llm_config()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var settingsPath = Path.Combine(directory, "settings.json");
        File.WriteAllText(Path.Combine(directory, "llm.config.json"), "{not json");

        var settings = new JsonAppSettingsService(settingsPath, appDirectory: directory).Load();

        settings.Llm.Backend.Should().Be("codex");
        settings.Llm.Endpoint.Should().Be("http://localhost:1234/v1/chat/completions");
    }

    [Fact]
    public void default_settings_path_uses_app_local_settings_when_present()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var localPath = Path.Combine(directory, "settings.json");
        File.WriteAllText(localPath, "{}");

        JsonAppSettingsService.DefaultSettingsPath(directory).Should().Be(localPath);
    }

    [Fact]
    public void default_settings_path_falls_back_to_per_user_location()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        JsonAppSettingsService.DefaultSettingsPath(directory)
            .Should().EndWith(Path.Combine("MakouReactor", "settings.json"));
    }

    [Fact]
    public void recent_files_are_deduplicated_reordered_and_capped()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "settings.json");
        var settings = new AppSettings
        {
            RecentFiles =
            [
                "C:\\A\\one.lgp",
                "C:\\A\\ONE.lgp",
            ],
        };
        var settingsService = new JsonAppSettingsService(path);
        var recentFiles = new RecentFilesService(settings, settingsService, maxItems: 3);

        recentFiles.Add("C:\\A\\two.lgp");
        recentFiles.Add("C:\\A\\three.lgp");
        recentFiles.Add("C:\\A\\one.lgp");
        recentFiles.Add("C:\\A\\four.lgp");

        recentFiles.RecentFiles.Should().Equal(
            "C:\\A\\four.lgp",
            "C:\\A\\one.lgp",
            "C:\\A\\three.lgp");
        new JsonAppSettingsService(path).Load().RecentFiles.Should().Equal(recentFiles.RecentFiles);
    }

    [Fact]
    public void shell_session_loads_settings_and_exposes_recent_file_service()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "settings.json");
        var settingsService = new JsonAppSettingsService(path);
        settingsService.Save(new AppSettings
        {
            JapaneseText = true,
            RecentFiles = ["C:\\ff7\\flevel.lgp"],
        });

        var session = new ShellSessionService(settingsService);

        session.Settings.JapaneseText.Should().BeTrue();
        session.RecentFiles.RecentFiles.Should().Equal("C:\\ff7\\flevel.lgp");

        session.RecentFiles.Add("C:\\ff7\\other.lgp");

        new JsonAppSettingsService(path).Load().RecentFiles.Should().Equal(
            "C:\\ff7\\other.lgp",
            "C:\\ff7\\flevel.lgp");
    }

    [Fact]
    public void shell_session_save_persists_current_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "settings.json");
        var session = new ShellSessionService(new JsonAppSettingsService(path));

        session.Settings.Window.Width = 1111;
        session.Settings.LastSelectedFields["C:\\ff7\\flevel.lgp"] = "md1stin";
        session.Save();

        var loaded = new JsonAppSettingsService(path).Load();
        loaded.Window.Width.Should().Be(1111);
        loaded.LastSelectedFields.Should().ContainKey("C:\\ff7\\flevel.lgp")
            .WhoseValue.Should().Be("md1stin");
    }
}

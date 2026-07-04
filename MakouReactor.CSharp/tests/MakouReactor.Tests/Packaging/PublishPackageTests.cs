using System.Xml.Linq;

using FluentAssertions;

using Xunit;

namespace MakouReactor.Tests.Packaging;

public sealed class PublishPackageTests
{
    [Fact]
    public void publish_script_fails_if_gui_package_lacks_gui_executable()
    {
        var root = FindProjectRoot();
        var script = File.ReadAllText(Path.Combine(root, "publish.bat"));

        script.Should().Contain("set \"GUI_OUT=%PUBLISH_ROOT%\\makoureactor-gui-win64\"");
        script.Should().Contain("dotnet publish src\\MakouReactor.UI.WPF\\MakouReactor.UI.WPF.csproj");
        script.Should().Contain("if not exist \"%GUI_OUT%\\MakouReactor.UI.WPF.exe\"");
        script.Should().Contain("exit /b 1");
    }

    [Fact]
    public void publish_script_checks_runtime_files_and_config_templates()
    {
        var root = FindProjectRoot();
        var script = File.ReadAllText(Path.Combine(root, "publish.bat"));

        script.Should().Contain("set \"CONFIG_SRC=%~dp0config\"");
        script.Should().Contain("xcopy /y \"%CONFIG_SRC%\\*.template.json\" \"%GUI_OUT%\\\"");
        script.Should().Contain("xcopy /y \"%CONFIG_SRC%\\*.template.json\" \"%CLI_OUT%\\\"");
        script.Should().Contain("if not exist \"%GUI_OUT%\\MakouReactor.UI.WPF.dll\"");
        script.Should().Contain("if not exist \"%GUI_OUT%\\MakouReactor.UI.WPF.runtimeconfig.json\"");
        script.Should().Contain("if not exist \"%GUI_OUT%\\MakouReactor.UI.WPF.deps.json\"");
        script.Should().Contain("if not exist \"%CLI_OUT%\\MakouReactor.CLI.dll\"");
        script.Should().Contain("if not exist \"%CLI_OUT%\\MakouReactor.CLI.runtimeconfig.json\"");
        script.Should().Contain("if not exist \"%CLI_OUT%\\MakouReactor.CLI.deps.json\"");
        script.Should().Contain("if not exist \"%GUI_OUT%\\settings.template.json\"");
        script.Should().Contain("if not exist \"%GUI_OUT%\\llm.config.template.json\"");
        script.Should().Contain("if not exist \"%CLI_OUT%\\llm.config.template.json\"");
    }

    [Fact]
    public void publish_script_rejects_packaged_game_data()
    {
        var root = FindProjectRoot();
        var script = File.ReadAllText(Path.Combine(root, "publish.bat"));

        script.Should().Contain("for /r \"%GUI_OUT%\" %%F in (*.lgp *.DAT *.lzs *.dec *.iso *.bin *.img)");
        script.Should().Contain("for /r \"%CLI_OUT%\" %%F in (*.lgp *.DAT *.lzs *.dec *.iso *.bin *.img)");
        script.Should().Contain("possible game data");
    }

    [Fact]
    public void package_smoke_script_extracts_launches_screenshots_and_closes_gui()
    {
        var root = FindProjectRoot();
        var script = File.ReadAllText(Path.Combine(root, "package-smoke.ps1"));

        script.Should().Contain("Copy-Item -Path (Join-Path $guiPackage '*') -Destination $extractRoot -Recurse -Force");
        script.Should().Contain("MakouReactor.UI.WPF.exe");
        script.Should().Contain("Start-Process -FilePath $guiExe");
        script.Should().Contain("CopyFromScreen");
        script.Should().Contain("package-smoke.png");
        script.Should().Contain("CloseMainWindow");
        script.Should().Contain("$process.Kill()");
    }

    [Fact]
    public void config_templates_are_valid_json_and_do_not_contain_game_paths()
    {
        var root = FindProjectRoot();
        var configDirectory = Path.Combine(root, "config");

        foreach (var path in Directory.EnumerateFiles(configDirectory, "*.template.json"))
        {
            var json = File.ReadAllText(path);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            document.RootElement.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
            json.Should().NotContain("flevel.lgp");
            json.Should().NotContain(".lgp");
            json.Should().NotContain(".DAT");
        }
    }

    [Fact]
    public void gui_project_publishes_as_windows_executable_separate_from_cli()
    {
        var root = FindProjectRoot();
        var guiProject = XDocument.Load(Path.Combine(
            root,
            "src",
            "MakouReactor.UI.WPF",
            "MakouReactor.UI.WPF.csproj"));
        var cliProject = XDocument.Load(Path.Combine(
            root,
            "src",
            "MakouReactor.CLI",
            "MakouReactor.CLI.csproj"));

        guiProject.Descendants("OutputType").Single().Value.Should().Be("WinExe");
        guiProject.Descendants("UseWPF").Single().Value.Should().Be("true");
        cliProject.Descendants("OutputType").Single().Value.Should().Be("Exe");
    }

    private static string FindProjectRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "publish.bat")) &&
                File.Exists(Path.Combine(current.FullName, "MakouReactor.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate MakouReactor.CSharp project root.");
    }
}

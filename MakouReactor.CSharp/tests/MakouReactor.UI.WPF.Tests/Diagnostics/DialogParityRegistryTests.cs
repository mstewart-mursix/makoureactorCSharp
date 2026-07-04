using System.IO;

using FluentAssertions;

using MakouReactor.UI.WPF.Diagnostics;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Diagnostics;

public sealed class DialogParityRegistryTests
{
    [Fact]
    public void every_tracked_dialog_has_source_dependency_and_wpf_mapping()
    {
        DialogParityRegistry.Entries.Should().NotBeEmpty();

        foreach (var entry in DialogParityRegistry.Entries)
        {
            entry.Name.Should().NotBeNullOrWhiteSpace();
            entry.OriginalSources.Should().NotBeEmpty(entry.Name);
            entry.DataDependencies.Should().NotBeEmpty(entry.Name);
            entry.WpfSurface.Should().NotBeNullOrWhiteSpace(entry.Name);
            entry.ViewModel.Should().NotBeNullOrWhiteSpace(entry.Name);
            entry.State.Should().NotBeNullOrWhiteSpace(entry.Name);
            entry.EmptyState.Should().NotBeNullOrWhiteSpace(entry.Name);
            entry.ReadOnlyDisplay.Should().NotBeNullOrWhiteSpace(entry.Name);
            entry.EditingPolicy.Should().NotBeNullOrWhiteSpace(entry.Name);
            entry.DirtyStatePolicy.Should().NotBeNullOrWhiteSpace(entry.Name);
            entry.CloseCancelPolicy.Should().NotBeNullOrWhiteSpace(entry.Name);
            entry.TestCoverage.Should().NotBeEmpty(entry.Name);
        }
    }

    [Fact]
    public void original_source_files_are_present()
    {
        var repoRoot = FindRepoRoot();

        foreach (var source in DialogParityRegistry.Entries.SelectMany(static entry => entry.OriginalSources))
        {
            if (source.EndsWith("ScriptEditorWidgets", StringComparison.Ordinal))
            {
                Directory.Exists(Path.Combine(repoRoot, source)).Should().BeTrue(source);
                continue;
            }

            File.Exists(Path.Combine(repoRoot, source)).Should().BeTrue(source);
        }
    }

    [Fact]
    public void wpf_dialog_surfaces_are_present()
    {
        var repoRoot = FindRepoRoot();
        var surfaces = new[]
        {
            "MakouReactor.CSharp/src/MakouReactor.UI.WPF/Texts/TextManagerDialog.xaml",
            "MakouReactor.CSharp/src/MakouReactor.UI.WPF/Walkmesh/WalkmeshManagerDialog.xaml",
            "MakouReactor.CSharp/src/MakouReactor.UI.WPF/Background/BackgroundDialog.xaml",
            "MakouReactor.CSharp/src/MakouReactor.UI.WPF/Variables/VariableManagerDialog.xaml",
            "MakouReactor.CSharp/src/MakouReactor.UI.WPF/Search/SearchDialog.xaml",
            "MakouReactor.CSharp/src/MakouReactor.UI.WPF/AI/LLMSceneDialog.xaml",
            "MakouReactor.CSharp/src/MakouReactor.UI.WPF/Models/ModelManagerDialog.xaml",
            "MakouReactor.CSharp/src/MakouReactor.UI.WPF/Models/PlayStationModelManagerDialog.xaml",
            "MakouReactor.CSharp/src/MakouReactor.UI.WPF/Models/AnimationSelectorDialog.xaml",
            "MakouReactor.CSharp/src/MakouReactor.UI.WPF/Encounters/EncounterDialog.xaml",
            "MakouReactor.CSharp/src/MakouReactor.UI.WPF/Tutorials/TutorialsDialog.xaml",
            "MakouReactor.CSharp/src/MakouReactor.UI.WPF/Misc/MiscellaneousDialog.xaml",
            "MakouReactor.CSharp/src/MakouReactor.UI.WPF/Batch/BatchProcessingDialog.xaml",
            "MakouReactor.CSharp/src/MakouReactor.UI.WPF/Archive/ArchiveManagerView.xaml",
            "MakouReactor.CSharp/src/MakouReactor.UI.WPF/Scripts/RawOpcodeDialog.xaml",
            "MakouReactor.CSharp/src/MakouReactor.UI.WPF/PlayStation/PlayStationSectionManagerDialog.xaml",
        };

        foreach (var surface in surfaces)
            File.Exists(Path.Combine(repoRoot, surface)).Should().BeTrue(surface);
    }

    [Fact]
    public void registered_test_coverage_files_are_present()
    {
        var repoRoot = FindRepoRoot();
        var testRoot = Path.Combine(repoRoot, "MakouReactor.CSharp", "tests", "MakouReactor.UI.WPF.Tests");
        var testFiles = Directory
            .EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories)
            .Select(static file => Path.GetFileName(file)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var testFile in DialogParityRegistry.Entries.SelectMany(static entry => entry.TestCoverage).Distinct())
            testFiles.Contains(testFile).Should().BeTrue(testFile);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "11_full_gui_port_workplan.md")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}

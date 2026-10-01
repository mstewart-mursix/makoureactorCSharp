using FluentAssertions;

using MakouReactor.CLI;
using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.CLI;

public sealed class CliProgramTests
{
    private static readonly object ConsoleLock = new();

    [Fact]
    public async Task generate_dry_run_keeps_standalone_cli_generation_flow()
    {
        var result = await RunCliAsync(
            "generate",
            "--prompt",
            "Cloud enters a reactor corridor",
            "--dry-run",
            "--width",
            "320",
            "--height",
            "240");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("# Backend:");
        result.StdOut.Should().Contain("# --- System prompt ---");
        result.StdOut.Should().Contain("# --- User prompt ---");
        result.StdOut.Should().Contain("Cloud enters a reactor corridor");
    }

    [Fact]
    public async Task validate_command_keeps_standalone_cli_plan_validation_flow()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "plan_valid_full.json");

        var result = await RunCliAsync("validate", fixture);

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Plan 'Test Scene'");
        result.StdErr.Should().Contain("Validation:");
    }

    [Fact]
    public async Task inspect_reports_ok_archive()
    {
        var path = CreateArchive(("md1stin", FieldPC.CreateEmpty("md1stin").SaveCompressed()));
        try
        {
            var result = await RunCliAsync("inspect", path);

            result.ExitCode.Should().Be(0, result.StdOut + result.StdErr);
            result.StdOut.Should().Contain("fields: 1").And.Contain("1 ok, 0 with section errors, 0 failed");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task inspect_names_the_field_that_fails_to_open()
    {
        var bad = LzsCompression.CompressWithHeader(new byte[100]);
        var path = CreateArchive(
            ("md1stin", FieldPC.CreateEmpty("md1stin").SaveCompressed()),
            ("brokenfld", bad));
        try
        {
            var result = await RunCliAsync("inspect", path);

            result.ExitCode.Should().Be(2);
            result.StdOut.Should().Contain("brokenfld: FAILED to open").And.Contain("1 failed");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task inspect_reports_unreadable_archive()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mr_bad_{Guid.NewGuid():N}.lgp");
        File.WriteAllBytes(path, new byte[64]);
        try
        {
            var result = await RunCliAsync("inspect", path);

            result.ExitCode.Should().Be(1);
            result.StdErr.Should().Contain("Archive open failed");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task apply_dry_run_describes_changes_and_writes_nothing()
    {
        var path = CreateArchive(("md1stin", FieldPC.CreateEmpty("md1stin").SaveCompressed()));
        var before = File.ReadAllBytes(path);
        try
        {
            var plan = Path.Combine(AppContext.BaseDirectory, "Fixtures", "plan_valid_full.json");

            var result = await RunCliAsync("apply", "--archive", path, "--field", "md1stin", "--plan", plan, "--dry-run");

            result.ExitCode.Should().Be(0, result.StdOut + result.StdErr);
            result.StdOut.Should().Contain("Changes to the field:").And.Contain("Dry run: nothing was written.");
            File.ReadAllBytes(path).Should().Equal(before);
            SafeArchiveWriter.ListBackups(path).Should().BeEmpty();
        }
        finally { DeleteArchive(path); }
    }

    [Fact]
    public async Task apply_writes_dialogue_with_backup_and_restore_reverts_it()
    {
        var path = CreateArchive(("md1stin", FieldPC.CreateEmpty("md1stin").SaveCompressed()));
        var before = File.ReadAllBytes(path);
        try
        {
            var plan = Path.Combine(AppContext.BaseDirectory, "Fixtures", "plan_valid_full.json");

            var apply = await RunCliAsync("apply", "--archive", path, "--field", "md1stin", "--plan", plan);

            apply.ExitCode.Should().Be(0, apply.StdOut + apply.StdErr);
            apply.StdOut.Should().Contain("Written. Backup:");
            var field = FieldArchive.Open(path).OpenField("md1stin");
            field.ScriptsAndTexts!.TextCount.Should().BeGreaterThan(2);
            SafeArchiveWriter.ListBackups(path).Should().HaveCount(1);

            var list = await RunCliAsync("restore", "--archive", path, "--list");
            list.StdOut.Should().Contain(".mr-backup-");

            var restore = await RunCliAsync("restore", "--archive", path);
            restore.ExitCode.Should().Be(0, restore.StdErr);
            File.ReadAllBytes(path).Should().Equal(before);
        }
        finally { DeleteArchive(path); }
    }

    [Fact]
    public async Task apply_refuses_a_plan_with_validation_errors_and_leaves_the_archive_alone()
    {
        var path = CreateArchive(("md1stin", FieldPC.CreateEmpty("md1stin").SaveCompressed()));
        var before = File.ReadAllBytes(path);
        try
        {
            var plan = Path.Combine(AppContext.BaseDirectory, "Fixtures", "plan_invalid_missing_actor.json");

            var result = await RunCliAsync("apply", "--archive", path, "--field", "md1stin", "--plan", plan);

            result.ExitCode.Should().Be(2);
            result.StdErr.Should().Contain("validation errors");
            File.ReadAllBytes(path).Should().Equal(before);
            SafeArchiveWriter.ListBackups(path).Should().BeEmpty();
        }
        finally { DeleteArchive(path); }
    }

    [Fact]
    public async Task apply_rejects_unknown_walkmesh_mode_and_missing_plan()
    {
        var path = CreateArchive(("md1stin", FieldPC.CreateEmpty("md1stin").SaveCompressed()));
        try
        {
            var plan = Path.Combine(AppContext.BaseDirectory, "Fixtures", "plan_valid_full.json");

            var badMode = await RunCliAsync("apply", "--archive", path, "--field", "md1stin", "--plan", plan, "--walkmesh-mode", "bogus");
            var noPlan = await RunCliAsync("apply", "--archive", path, "--field", "md1stin", "--plan", "nope.json");

            badMode.ExitCode.Should().Be(1);
            badMode.StdErr.Should().Contain("--walkmesh-mode");
            noPlan.ExitCode.Should().Be(1);
            noPlan.StdErr.Should().Contain("File not found");
        }
        finally { DeleteArchive(path); }
    }

    private static void DeleteArchive(string path)
    {
        File.Delete(path);
        var directory = Path.GetDirectoryName(path)!;
        foreach (var leftover in Directory.GetFiles(directory, Path.GetFileName(path) + ".*"))
            File.Delete(leftover);
    }

    private static string CreateArchive(params (string Name, byte[] Data)[] files)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mr_inspect_{Guid.NewGuid():N}.lgp");
        LgpArchive.Create(path, files.Select(f => new LgpArchiveFile(f.Name, f.Data)));
        return path;
    }

    private static Task<CliResult> RunCliAsync(params string[] args)
    {
        lock (ConsoleLock)
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);
            try
            {
                var exitCode = Program.Main(args).GetAwaiter().GetResult();
                return Task.FromResult(new CliResult(exitCode, stdout.ToString(), stderr.ToString()));
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }
    }

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr);
}

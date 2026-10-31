using FluentAssertions;

using MakouReactor.CLI;

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
    public async Task generate_dry_run_with_archive_field_includes_field_context()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mr_cli_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            MakouReactor.Core.IO.FieldArchive.OpenPcDirectory(dir).CreatePcField("testmap");

            var result = await RunCliAsync(
                "generate", "--prompt", "Cloud enters", "--dry-run",
                "--archive", dir, "--field", "testmap");

            result.ExitCode.Should().Be(0, result.StdErr);
            result.StdOut.Should().Contain("Field Context:");
            result.StdOut.Should().Contain("Field: testmap");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task generate_rejects_field_without_archive_and_missing_field()
    {
        var lone = await RunCliAsync("generate", "--prompt", "x", "--dry-run", "--field", "testmap");
        lone.ExitCode.Should().Be(1);
        lone.StdErr.Should().Contain("--archive and --field must be used together");

        var dir = Path.Combine(Path.GetTempPath(), "mr_cli_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var missing = await RunCliAsync("generate", "--prompt", "x", "--dry-run", "--archive", dir, "--field", "nope");
            missing.ExitCode.Should().Be(1);
            missing.StdErr.Should().Contain("Could not open field 'nope'");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private const string ApplyPlan =
        "{\"meta\":{\"title\":\"Greeting\",\"model\":\"m\",\"version\":\"1.0\"}," +
        "\"actors\":[{\"id\":\"cloud\",\"position\":{\"x\":100,\"y\":100}}]," +
        "\"dialog\":[{\"speakerId\":\"cloud\",\"text\":\"Let's move.\"}]}";

    private static (string Dir, string Archive, string Plan) ApplyFixture(string planJson = ApplyPlan)
    {
        var dir = Path.Combine(Path.GetTempPath(), "mr_apply_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var archive = Path.Combine(dir, "flevel.lgp");
        MakouReactor.Core.IO.LgpArchive.Create(archive, new[]
        {
            new MakouReactor.Core.IO.LgpArchiveFile("testmap",
                MakouReactor.Core.Models.FieldPC.CreateEmpty("testmap").SaveCompressed()),
        });
        var plan = Path.Combine(dir, "plan.json");
        File.WriteAllText(plan, planJson);
        return (dir, archive, plan);
    }

    [Fact]
    public async Task apply_dry_run_prints_summary_and_writes_nothing()
    {
        var (dir, archive, plan) = ApplyFixture();
        try
        {
            var before = File.ReadAllBytes(archive);

            var result = await RunCliAsync("apply", "--archive", archive, "--field", "testmap", "--plan", plan, "--dry-run");

            result.ExitCode.Should().Be(0, result.StdErr);
            result.StdOut.Should().NotBeNullOrWhiteSpace();
            result.StdErr.Should().Contain("Dry run");
            File.ReadAllBytes(archive).Should().Equal(before);
            Directory.GetFiles(dir, "flevel.lgp.mr-backup-*").Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task apply_writes_with_backup_and_revert_restores()
    {
        var (dir, archive, plan) = ApplyFixture();
        try
        {
            var before = File.ReadAllBytes(archive);

            var applied = await RunCliAsync("apply", "--archive", archive, "--field", "testmap", "--plan", plan);

            applied.ExitCode.Should().Be(0, applied.StdErr);
            applied.StdErr.Should().Contain("Backup:");
            Directory.GetFiles(dir, "flevel.lgp.mr-backup-*").Should().ContainSingle();
            File.ReadAllBytes(archive).Should().NotEqual(before);
            MakouReactor.Core.IO.FieldArchive.Open(archive).OpenField("testmap").ScriptsAndTexts!.Texts
                .Should().Contain(t => t.Value.Contains("Let's move."));

            var listed = await RunCliAsync("revert", "--archive", archive, "--list");
            listed.StdOut.Should().Contain("mr-backup-");

            var reverted = await RunCliAsync("revert", "--archive", archive);
            reverted.ExitCode.Should().Be(0, reverted.StdErr);
            File.ReadAllBytes(archive).Should().Equal(before);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task apply_refuses_plan_with_validation_errors()
    {
        var duplicate = ApplyPlan.Replace(
            "[{\"id\":\"cloud\",\"position\":{\"x\":100,\"y\":100}}]",
            "[{\"id\":\"cloud\",\"position\":{\"x\":100,\"y\":100}},{\"id\":\"cloud\",\"position\":{\"x\":150,\"y\":150}}]");
        var (dir, archive, plan) = ApplyFixture(duplicate);
        try
        {
            var before = File.ReadAllBytes(archive);

            var result = await RunCliAsync("apply", "--archive", archive, "--field", "testmap", "--plan", plan);

            result.ExitCode.Should().Be(2);
            result.StdErr.Should().Contain("Duplicate actor id");
            File.ReadAllBytes(archive).Should().Equal(before);
            Directory.GetFiles(dir, "flevel.lgp.mr-backup-*").Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task revert_without_backups_fails_cleanly()
    {
        var (dir, archive, _) = ApplyFixture();
        try
        {
            var result = await RunCliAsync("revert", "--archive", archive);

            result.ExitCode.Should().Be(1);
            result.StdErr.Should().Contain("No backups");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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

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

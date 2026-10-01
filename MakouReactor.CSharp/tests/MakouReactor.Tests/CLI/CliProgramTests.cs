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

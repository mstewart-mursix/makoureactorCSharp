using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using MakouReactor.AI;
using MakouReactor.AI.Backends;
using MakouReactor.AI.Config;
using MakouReactor.AI.Parsing;
using MakouReactor.AI.Validation;
using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

namespace MakouReactor.CLI;

/// <summary>
/// Command-line front end for the scene generation pipeline.
/// <c>generate</c> runs prompt → LLM → parse → validate and prints/writes the plan JSON;
/// <c>validate</c> checks an existing plan file; <c>doctor</c> checks the Codex CLI setup.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var root = new RootCommand("Makou Reactor CLI — LLM scene generation for FFVII fields");
        root.AddCommand(BuildGenerateCommand());
        root.AddCommand(BuildValidateCommand());
        root.AddCommand(BuildDoctorCommand());
        return await root.InvokeAsync(args);
    }

    // -----------------------------------------------------------------------
    // generate
    // -----------------------------------------------------------------------

    private static Command BuildGenerateCommand()
    {
        var promptOpt = new Option<string>("--prompt", "High-level scene description (characters, motivations, setting)") { IsRequired = true };
        var configOpt = new Option<string?>("--config", "Path to llm.config.json (defaults to search in cwd/app dir)");
        var widthOpt = new Option<int>("--width", () => 320, "Field width hint in pixels");
        var heightOpt = new Option<int>("--height", () => 240, "Field height hint in pixels");
        var outOpt = new Option<string?>("--out", "Write the generated scene-plan JSON to this file (default: stdout)");
        var dryRunOpt = new Option<bool>("--dry-run", "Print the composed prompt without calling the LLM");
        var noDialogOpt = new Option<bool>("--no-dialog", "Skip dialogue generation");
        var noLayoutOpt = new Option<bool>("--no-layout", "Skip layout generation");
        var noScriptsOpt = new Option<bool>("--no-scripts", "Skip event script generation");
        var archiveOpt = new Option<string?>("--archive", "flevel.lgp (or field directory) to read field context from; requires --field");
        var fieldOpt = new Option<string?>("--field", "Field name inside --archive whose walkmesh, groups, dialog and exits are described to the model");

        var cmd = new Command("generate", "Generate a scene plan from a prompt via the configured LLM backend");
        cmd.AddOption(promptOpt);
        cmd.AddOption(configOpt);
        cmd.AddOption(widthOpt);
        cmd.AddOption(heightOpt);
        cmd.AddOption(outOpt);
        cmd.AddOption(dryRunOpt);
        cmd.AddOption(noDialogOpt);
        cmd.AddOption(noLayoutOpt);
        cmd.AddOption(noScriptsOpt);
        cmd.AddOption(archiveOpt);
        cmd.AddOption(fieldOpt);

        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var parse = ctx.ParseResult;
            var config = LLMConfigLoader.Load(parse.GetValueForOption(configOpt));

            var scenePrompt = new ScenePrompt
            {
                UserText = parse.GetValueForOption(promptOpt)!,
                GenDialog = !parse.GetValueForOption(noDialogOpt),
                GenLayout = !parse.GetValueForOption(noLayoutOpt),
                GenScripts = !parse.GetValueForOption(noScriptsOpt),
            };

            var width = parse.GetValueForOption(widthOpt);
            var height = parse.GetValueForOption(heightOpt);

            if (!TryLoadField(parse.GetValueForOption(archiveOpt), parse.GetValueForOption(fieldOpt), out var field))
            {
                ctx.ExitCode = 1;
                return;
            }

            using var service = new SceneGenerationService(config);

            if (parse.GetValueForOption(dryRunOpt))
            {
                var request = service.BuildRequest(scenePrompt, width, height, field);
                Console.WriteLine($"# Backend: {config.Backend}");
                Console.WriteLine("# --- System prompt ---");
                Console.WriteLine(request.SystemPrompt);
                Console.WriteLine("# --- User prompt ---");
                Console.WriteLine(request.UserPrompt);
                ctx.ExitCode = 0;
                return;
            }

            Console.Error.WriteLine($"Generating via '{config.Backend}' backend...");
            var result = await service.GenerateAsync(scenePrompt, field, width, height);
            PrintAttempts(result);

            if (!result.Ok)
            {
                Console.Error.WriteLine($"Generation failed: {result.Error}");
                ctx.ExitCode = 1;
                return;
            }

            PrintIssues(result.Validation!);

            var outPath = parse.GetValueForOption(outOpt);
            if (outPath != null)
            {
                await File.WriteAllTextAsync(outPath, result.RawJson);
                Console.Error.WriteLine($"Scene plan written to {outPath}");
            }
            else
            {
                Console.WriteLine(result.RawJson);
            }

            ctx.ExitCode = result.Validation!.HasErrors ? 2 : 0;
        });

        return cmd;
    }

    /// <summary>
    /// Load the field named by <c>--field</c> from <c>--archive</c>. Both absent is fine (no field
    /// context); one without the other, or a load failure, is reported and returns false.
    /// </summary>
    private static bool TryLoadField(string? archivePath, string? fieldName, out Field? field)
    {
        field = null;
        if (archivePath == null && fieldName == null)
            return true;

        if (archivePath == null || string.IsNullOrWhiteSpace(fieldName))
        {
            Console.Error.WriteLine("--archive and --field must be used together.");
            return false;
        }

        try
        {
            var archive = Directory.Exists(archivePath)
                ? FieldArchive.OpenPcDirectory(archivePath)
                : FieldArchive.Open(archivePath);
            field = archive.OpenField(fieldName);
            Console.Error.WriteLine($"Using field context from '{fieldName}' in {archivePath}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                       or KeyNotFoundException or ArgumentException or InvalidOperationException
                                       or NotSupportedException)
        {
            Console.Error.WriteLine($"Could not open field '{fieldName}' from {archivePath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Report automatic repair attempts (silent when the first answer was accepted).</summary>
    private static void PrintAttempts(SceneGenerationResult result)
    {
        if (result.Attempts.Count <= 1)
            return;

        foreach (var attempt in result.Attempts)
            Console.Error.WriteLine($"  {attempt.Summary}");

        var final = result.Attempts[^1];
        Console.Error.WriteLine(final.Accepted
            ? $"Succeeded after {result.RepairCount} repair attempt(s)."
            : $"Still failing after {result.RepairCount} repair attempt(s).");
    }

    // -----------------------------------------------------------------------
    // validate
    // -----------------------------------------------------------------------

    private static Command BuildValidateCommand()
    {
        var fileArg = new Argument<string>("file", "Scene-plan JSON file to validate");
        var cmd = new Command("validate", "Parse and validate an existing scene-plan JSON file");
        cmd.AddArgument(fileArg);

        cmd.SetHandler((InvocationContext ctx) =>
        {
            var path = ctx.ParseResult.GetValueForArgument(fileArg);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"File not found: {path}");
                ctx.ExitCode = 1;
                return;
            }

            var parsed = ScenePlanParser.Parse(File.ReadAllBytes(path));
            if (!parsed.Ok)
            {
                Console.Error.WriteLine($"Parse failed: {parsed.Error}");
                ctx.ExitCode = 1;
                return;
            }

            var validation = ScenePlanValidator.Validate(parsed.Plan, field: null);
            PrintIssues(validation);
            Console.WriteLine($"Plan '{parsed.Plan.Meta?.Title}': {parsed.Plan.Actors.Count} actors, " +
                              $"{parsed.Plan.Dialog.Count} dialog lines, {parsed.Plan.Events.Count} events");
            ctx.ExitCode = validation.HasErrors ? 2 : 0;
        });

        return cmd;
    }

    // -----------------------------------------------------------------------
    // doctor
    // -----------------------------------------------------------------------

    private static Command BuildDoctorCommand()
    {
        var configOpt = new Option<string?>("--config", "Path to llm.config.json");
        var cmd = new Command("doctor", "Check the LLM backend configuration (Codex CLI presence, config values)");
        cmd.AddOption(configOpt);

        cmd.SetHandler((InvocationContext ctx) =>
        {
            var config = LLMConfigLoader.Load(ctx.ParseResult.GetValueForOption(configOpt));
            Console.WriteLine($"Backend:          {config.Backend}");

            if (config.Backend == "http")
            {
                Console.WriteLine($"Endpoint:         {config.Endpoint}");
                Console.WriteLine($"Model:            {config.Model}");
                ctx.ExitCode = 0;
                return;
            }

            Console.WriteLine($"Codex executable: {config.CodexExecutable}");
            Console.WriteLine($"Codex model:      {(string.IsNullOrEmpty(config.CodexModel) ? "(CLI default)" : config.CodexModel)}");

            var resolved = CodexCliBackend.ResolveExecutable(config.CodexExecutable);
            if (resolved == null)
            {
                Console.WriteLine("Status:           NOT FOUND — install with 'npm i -g @openai/codex', then run 'codex login'");
                ctx.ExitCode = 1;
                return;
            }

            Console.WriteLine($"Resolved path:    {resolved}");
            Console.WriteLine("Status:           OK (run 'codex login status' to verify subscription auth)");
            ctx.ExitCode = 0;
        });

        return cmd;
    }

    // -----------------------------------------------------------------------
    // Shared output helpers
    // -----------------------------------------------------------------------

    private static void PrintIssues(ValidationResult validation)
    {
        foreach (var issue in validation.Issues)
            Console.Error.WriteLine($"  {issue}");

        Console.Error.WriteLine(validation.HasErrors
            ? $"Validation: {validation.Issues.Count} issue(s), including errors"
            : $"Validation: {validation.Issues.Count} issue(s), no errors");
    }
}

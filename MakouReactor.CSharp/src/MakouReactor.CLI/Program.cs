using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using MakouReactor.AI;
using MakouReactor.AI.Backends;
using MakouReactor.AI.Config;
using MakouReactor.AI.Mapping;
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
        root.AddCommand(BuildInspectCommand());
        root.AddCommand(BuildApplyCommand());
        root.AddCommand(BuildRestoreCommand());
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
        var archiveOpt = new Option<string?>("--archive", "flevel.lgp to read field context from (use with --field)");
        var fieldOpt = new Option<string?>("--field", "Field name inside --archive to describe to the model");
        var dryRunOpt = new Option<bool>("--dry-run", "Print the composed prompt without calling the LLM");
        var noDialogOpt = new Option<bool>("--no-dialog", "Skip dialogue generation");
        var noLayoutOpt = new Option<bool>("--no-layout", "Skip layout generation");
        var noScriptsOpt = new Option<bool>("--no-scripts", "Skip event script generation");

        var cmd = new Command("generate", "Generate a scene plan from a prompt via the configured LLM backend");
        cmd.AddOption(promptOpt);
        cmd.AddOption(configOpt);
        cmd.AddOption(widthOpt);
        cmd.AddOption(heightOpt);
        cmd.AddOption(outOpt);
        cmd.AddOption(archiveOpt);
        cmd.AddOption(fieldOpt);
        cmd.AddOption(dryRunOpt);
        cmd.AddOption(noDialogOpt);
        cmd.AddOption(noLayoutOpt);
        cmd.AddOption(noScriptsOpt);

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

            Field? field = null;
            var archivePath = parse.GetValueForOption(archiveOpt);
            var fieldName = parse.GetValueForOption(fieldOpt);
            if (archivePath != null || fieldName != null)
            {
                if (archivePath == null || fieldName == null)
                {
                    Console.Error.WriteLine("--archive and --field must be used together.");
                    ctx.ExitCode = 1;
                    return;
                }

                try
                {
                    field = FieldArchive.Open(archivePath).OpenField(fieldName);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Could not open field '{fieldName}': {ex.Message}");
                    ctx.ExitCode = 1;
                    return;
                }
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

            if (!result.Ok)
            {
                Console.Error.WriteLine($"Generation failed: {result.Error}");
                ctx.ExitCode = 1;
                return;
            }

            if (result.Attempts.Count > 1 && result.Attempts[^1].Length == 0)
                Console.Error.WriteLine($"Succeeded after {result.Attempts.Count - 1} repair attempt(s).");
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
    // inspect
    // -----------------------------------------------------------------------

    private static Command BuildInspectCommand()
    {
        var archiveArg = new Argument<string>("archive", "Path to flevel.lgp (or another PC field LGP)");
        var fieldOpt = new Option<string?>("--field", "Only inspect this field (default: every field)");
        var cmd = new Command("inspect", "Open an archive and report exactly which fields/sections fail to parse");
        cmd.AddArgument(archiveArg);
        cmd.AddOption(fieldOpt);

        cmd.SetHandler((InvocationContext ctx) =>
        {
            var path = ctx.ParseResult.GetValueForArgument(archiveArg);
            var only = ctx.ParseResult.GetValueForOption(fieldOpt);
            ctx.ExitCode = Inspect(path, only, Console.Out, Console.Error);
        });

        return cmd;
    }

    /// <summary>
    /// Opens <paramref name="path"/> and writes a parse report. Returns 0 when everything
    /// parsed, 1 when the archive cannot be opened, 2 when some field or section failed.
    /// </summary>
    internal static int Inspect(string path, string? onlyField, TextWriter output, TextWriter error)
    {
        FieldArchive archive;
        try
        {
            archive = FieldArchive.Open(path);
        }
        catch (Exception ex)
        {
            error.WriteLine($"Archive open failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        output.WriteLine($"Archive: {path}");
        output.WriteLine($"Entries: {archive.ArchiveEntries.Count}, fields: {archive.FieldEntries.Count}");

        var fields = archive.FieldEntries
            .Where(f => onlyField == null || f.Name.Equals(onlyField, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (onlyField != null && fields.Count == 0)
        {
            error.WriteLine($"Field not found: {onlyField}");
            return 1;
        }

        int failedFields = 0, partialFields = 0;
        foreach (var entry in fields)
        {
            try
            {
                var field = archive.OpenField(entry.Name);
                if (field.SectionErrors.Count > 0)
                {
                    partialFields++;
                    foreach (var (section, message) in field.SectionErrors)
                        output.WriteLine($"  {entry.Name}: section {section} failed: {message}");
                }
            }
            catch (Exception ex)
            {
                failedFields++;
                output.WriteLine($"  {entry.Name}: FAILED to open: {ex.GetType().Name}: {ex.Message}");
            }
        }

        output.WriteLine($"Result: {fields.Count - failedFields - partialFields} ok, " +
                         $"{partialFields} with section errors, {failedFields} failed");
        return failedFields + partialFields == 0 ? 0 : 2;
    }

    // -----------------------------------------------------------------------
    // apply / restore
    // -----------------------------------------------------------------------

    private static Command BuildApplyCommand()
    {
        var archiveOpt = new Option<string>("--archive", "flevel.lgp to modify") { IsRequired = true };
        var fieldOpt = new Option<string>("--field", "Field inside the archive") { IsRequired = true };
        var planOpt = new Option<string>("--plan", "Scene-plan JSON produced by 'generate'") { IsRequired = true };
        var dryRunOpt = new Option<bool>("--dry-run", "Print what would change without writing anything");
        var walkmeshOpt = new Option<string>("--walkmesh-mode", () => "ignore", "ignore | merge | replace");
        var cmd = new Command("apply", "Apply a scene plan to a field (backup, verify, atomic replace)");
        cmd.AddOption(archiveOpt);
        cmd.AddOption(fieldOpt);
        cmd.AddOption(planOpt);
        cmd.AddOption(dryRunOpt);
        cmd.AddOption(walkmeshOpt);

        cmd.SetHandler((InvocationContext ctx) =>
        {
            var parse = ctx.ParseResult;
            ctx.ExitCode = Apply(
                parse.GetValueForOption(archiveOpt)!,
                parse.GetValueForOption(fieldOpt)!,
                parse.GetValueForOption(planOpt)!,
                parse.GetValueForOption(dryRunOpt),
                parse.GetValueForOption(walkmeshOpt)!,
                Console.Out,
                Console.Error);
        });

        return cmd;
    }

    /// <summary>Returns 0 on success, 1 for usage/IO problems, 2 for a plan that fails validation.</summary>
    internal static int Apply(string archive, string field, string planPath, bool dryRun, string walkmeshMode,
                              TextWriter output, TextWriter error)
    {
        if (!Enum.TryParse<WalkmeshMode>(walkmeshMode, ignoreCase: true, out var mode))
        {
            error.WriteLine($"Unknown --walkmesh-mode '{walkmeshMode}' (use ignore, merge or replace).");
            return 1;
        }

        if (!File.Exists(planPath))
        {
            error.WriteLine($"File not found: {planPath}");
            return 1;
        }

        var parsed = ScenePlanParser.Parse(File.ReadAllBytes(planPath));
        if (!parsed.Ok)
        {
            error.WriteLine($"Parse failed: {parsed.Error}");
            return 1;
        }

        var outcome = ScenePlanApplier.ApplyToArchive(archive, field, parsed.Plan, dryRun, mode);
        if (outcome.Validation != null)
        {
            foreach (var issue in outcome.Validation.Issues)
                error.WriteLine($"  {issue}");
        }

        if (!outcome.Ok)
        {
            error.WriteLine(outcome.Error);
            return outcome.Validation?.HasErrors == true ? 2 : 1;
        }

        foreach (var note in outcome.Layout?.Notes ?? [])
            output.WriteLine($"layout: {note}");

        output.WriteLine(outcome.Summary);
        if (outcome.DryRun)
        {
            output.WriteLine("Dry run: nothing was written.");
        }
        else if (outcome.Write != null)
        {
            output.WriteLine($"Written. Backup: {outcome.Write.BackupPath}");
            if (outcome.Write.PrunedBackups > 0)
                output.WriteLine($"Pruned {outcome.Write.PrunedBackups} old backup(s).");
        }

        return 0;
    }

    private static Command BuildRestoreCommand()
    {
        var archiveOpt = new Option<string>("--archive", "flevel.lgp to restore") { IsRequired = true };
        var listOpt = new Option<bool>("--list", "List backups instead of restoring");
        var cmd = new Command("restore", "Restore the newest backup made by 'apply' (or list backups)");
        cmd.AddOption(archiveOpt);
        cmd.AddOption(listOpt);

        cmd.SetHandler((InvocationContext ctx) =>
        {
            var archive = ctx.ParseResult.GetValueForOption(archiveOpt)!;
            if (ctx.ParseResult.GetValueForOption(listOpt))
            {
                var backups = SafeArchiveWriter.ListBackups(archive);
                foreach (var backup in backups)
                    Console.WriteLine($"{backup.CreatedUtc:yyyy-MM-dd HH:mm:ss}Z  {backup.Path}");
                if (backups.Count == 0)
                    Console.WriteLine("No backups found.");
                ctx.ExitCode = 0;
                return;
            }

            try
            {
                var restored = SafeArchiveWriter.RestoreLatestBackup(archive);
                Console.WriteLine($"Restored {restored.Path} ({restored.CreatedUtc:yyyy-MM-dd HH:mm:ss}Z).");
                ctx.ExitCode = 0;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                Console.Error.WriteLine($"Restore failed: {ex.Message}");
                ctx.ExitCode = 1;
            }
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

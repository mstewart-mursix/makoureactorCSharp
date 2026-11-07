using MakouReactor.AI.Backends;
using MakouReactor.AI.Mapping;
using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

namespace MakouReactor.AI.Batch;

/// <summary>Options for <see cref="BatchRunner.RunAsync"/>.</summary>
public sealed class BatchOptions
{
    /// <summary>PC <c>flevel.lgp</c> (or, without <see cref="Apply"/>, a loose PC field directory).</summary>
    public required string ArchivePath { get; init; }

    /// <summary>Directory for <c>&lt;field&gt;.plan.json</c> files and <see cref="BatchReport.ReportFileName"/>; created if missing.</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>Also apply each plan that passes validation, through <see cref="ScenePlanApplyPipeline"/>.</summary>
    public bool Apply { get; init; }

    /// <summary>Optional progress sink (one line per step); called synchronously on the runner's thread.</summary>
    public Action<string>? Log { get; init; }
}

/// <summary>
/// Runs a batch script: for each entry, sequentially (one LLM request at a time), load the field
/// from the archive, generate a scene plan via <see cref="SceneGenerationService.GenerateAsync"/>,
/// write <c>&lt;out&gt;/&lt;field&gt;.plan.json</c> and, with <see cref="BatchOptions.Apply"/>, apply
/// passing plans to the archive. A failing entry is recorded and the batch continues. Finally
/// <c>&lt;out&gt;/batch-report.json</c> is written.
/// </summary>
public sealed class BatchRunner
{
    private readonly Func<SceneGenerationService> _serviceFactory;

    /// <summary>
    /// Create a runner using <paramref name="config"/>. When <paramref name="backend"/> is given it is
    /// used for every request (and not disposed); otherwise the configured backend is created per run.
    /// </summary>
    public BatchRunner(LLMConfig config, ILLMBackend? backend = null)
        : this(() => new SceneGenerationService(config, backend))
    {
        ArgumentNullException.ThrowIfNull(config);
    }

    /// <summary>Create a runner that obtains one <see cref="SceneGenerationService"/> per run from <paramref name="serviceFactory"/>.</summary>
    public BatchRunner(Func<SceneGenerationService> serviceFactory)
    {
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
    }

    /// <summary>
    /// Run <paramref name="entries"/> in order. Never throws for per-entry problems; see
    /// <see cref="BatchReport.Entries"/> and <see cref="BatchReport.ExitCode"/>. If the archive cannot
    /// be opened (or <see cref="BatchOptions.Apply"/> targets a loose directory) or the output
    /// directory cannot be created, no entry runs and <see cref="BatchReport.InputError"/> is set.
    /// Cancellation aborts the in-flight request and stops before the next entry.
    /// </summary>
    public async Task<BatchReport> RunAsync(IReadOnlyList<BatchEntry> entries, BatchOptions options,
                                            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);

        var started = DateTimeOffset.Now;
        var inputError = CheckInputs(options);
        var report = new BatchReport
        {
            ArchivePath = options.ArchivePath,
            OutputDirectory = options.OutputDirectory,
            ApplyRequested = options.Apply,
            StartedAt = started,
            FinishedAt = started,
            InputError = inputError,
        };
        if (inputError.Length > 0)
            return report;

        // Keep every backup this batch makes (plus the usual history), so the pre-batch archive
        // is not pruned away by the batch's own applies.
        var writer = options.Apply
            ? new SafeArchiveWriter(options.ArchivePath, SafeArchiveWriter.DefaultBackupsToKeep + entries.Count)
            : null;
        var planNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        using (var service = _serviceFactory())
        using (cancellationToken.Register(service.CancelAll))
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    report.Cancelled = true;
                    options.Log?.Invoke("Cancelled; remaining entries skipped.");
                    break;
                }

                var entry = entries[i];
                options.Log?.Invoke($"[{i + 1}/{entries.Count}] {entry.Field}: starting...");
                BatchEntryResult result;
                try
                {
                    result = await RunEntryAsync(i + 1, entry, options, service, writer, planNames);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // One entry's unexpected failure must not stop the batch.
                    result = Result(i + 1, entry, BatchEntryStatus.Failed, $"Unexpected error: {ex.Message}");
                }

                report.Entries.Add(result);
                options.Log?.Invoke($"[{i + 1}/{entries.Count}] {entry.Field}: {BatchReport.StatusName(result.Status)}" +
                                    (result.Error.Length > 0 ? $" - {result.Error}" : string.Empty));
            }

            if (cancellationToken.IsCancellationRequested && report.Entries.Count < entries.Count)
                report.Cancelled = true;
        }

        report.FinishedAt = DateTimeOffset.Now;
        WriteReport(report);
        return report;
    }

    private static string CheckInputs(BatchOptions options)
    {
        var archivePath = options.ArchivePath;
        var isDirectory = Directory.Exists(archivePath);
        if (!isDirectory && !File.Exists(archivePath))
            return $"Archive not found: {archivePath}";
        if (isDirectory && options.Apply)
            return "--apply writes through a backup of an .lgp archive; loose field directories cannot be applied to.";

        try
        {
            _ = OpenArchive(archivePath);
        }
        catch (Exception ex) when (IsLoadException(ex))
        {
            return $"Could not open archive {archivePath}: {ex.Message}";
        }

        try
        {
            Directory.CreateDirectory(options.OutputDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"Could not create output directory {options.OutputDirectory}: {ex.Message}";
        }
        return string.Empty;
    }

    private static async Task<BatchEntryResult> RunEntryAsync(int index, BatchEntry entry, BatchOptions options,
                                                              SceneGenerationService service, SafeArchiveWriter? writer,
                                                              Dictionary<string, int> planNames)
    {
        // Allocated up front so the name depends only on the script, not on earlier outcomes.
        var planFile = PlanFileName(entry.Field, planNames);

        // Re-open per entry so a field applied earlier in the batch is read back in its new state.
        FieldPC field;
        try
        {
            field = OpenArchive(options.ArchivePath).OpenField(entry.Field);
        }
        catch (FileNotFoundException)
        {
            return Result(index, entry, BatchEntryStatus.UnknownField, $"Field '{entry.Field}' not found in {options.ArchivePath}");
        }
        catch (Exception ex) when (IsLoadException(ex))
        {
            return Result(index, entry, BatchEntryStatus.Failed, $"Could not open field '{entry.Field}': {ex.Message}");
        }

        var generated = await service.GenerateAsync(new ScenePrompt { UserText = entry.Prompt }, field);
        if (!generated.Ok || generated.Plan == null || generated.Validation == null)
        {
            return Result(index, entry, BatchEntryStatus.Failed,
                          $"Generation failed: {generated.Error}", repairCount: generated.RepairCount);
        }

        var issues = generated.Validation.Issues;
        var errors = issues.Where(i => i.Level == Severity.Error).ToList();
        var warnings = issues.Count(i => i.Level == Severity.Warn);
        var infos = issues.Count(i => i.Level == Severity.Info);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(options.OutputDirectory, planFile), generated.RawJson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result(index, entry, BatchEntryStatus.Failed, $"Could not write plan: {ex.Message}",
                          generated.RepairCount, errors.Count, warnings, infos);
        }

        BatchEntryResult Make(BatchEntryStatus status, string error, bool applied = false, string? backup = null) => new()
        {
            Index = index,
            Field = entry.Field,
            Prompt = entry.Prompt,
            Seed = entry.Seed,
            Status = status,
            RepairCount = generated.RepairCount,
            ErrorCount = errors.Count,
            WarningCount = warnings,
            InfoCount = infos,
            PlanFile = planFile,
            Applied = applied,
            BackupPath = backup,
            Error = error,
        };

        if (errors.Count > 0)
            return Make(BatchEntryStatus.ValidationErrors, ValidationSummary(errors));

        if (writer == null)
            return Make(BatchEntryStatus.Ok, string.Empty);

        options.Log?.Invoke($"[{index}] {entry.Field}: applying...");
        var check = ScenePlanApplyPipeline.Check(generated.Plan, field);
        if (check.Validation.HasErrors)
        {
            var applyErrors = check.Validation.Issues.Where(i => i.Level == Severity.Error).ToList();
            return Make(BatchEntryStatus.ValidationErrors, "Not applied: " + ValidationSummary(applyErrors));
        }
        if (!check.CanApply)
            return Make(BatchEntryStatus.Failed, $"Preview failed: {check.Preview?.Error}");

        var commit = ScenePlanApplyPipeline.Commit(generated.Plan, field, writer);
        return commit.Ok
            ? Make(BatchEntryStatus.Ok, string.Empty, applied: true, backup: commit.Write!.Backup?.Path)
            : Make(BatchEntryStatus.Failed, commit.Error);
    }

    private static string ValidationSummary(IReadOnlyList<Issue> errors)
    {
        var first = errors[0];
        var message = string.IsNullOrEmpty(first.Path) ? first.Message : $"{first.Path}: {first.Message}";
        return errors.Count == 1
            ? $"1 validation error: {message}"
            : $"{errors.Count} validation errors, first: {message}";
    }

    /// <summary><c>&lt;field&gt;.plan.json</c>, with <c>-2</c>, <c>-3</c>... for the 2nd, 3rd... entry naming the same field; unsafe characters replaced.</summary>
    private static string PlanFileName(string fieldName, Dictionary<string, int> used)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(fieldName.Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray());
        used[safe] = used.TryGetValue(safe, out var count) ? count + 1 : 1;
        return used[safe] == 1 ? $"{safe}.plan.json" : $"{safe}-{used[safe]}.plan.json";
    }

    private static FieldArchive OpenArchive(string path) =>
        Directory.Exists(path) ? FieldArchive.OpenPcDirectory(path) : FieldArchive.Open(path);

    private static bool IsLoadException(Exception ex) =>
        ex is IOException or InvalidDataException or UnauthorizedAccessException or KeyNotFoundException
            or ArgumentException or InvalidOperationException or NotSupportedException;

    private static BatchEntryResult Result(int index, BatchEntry entry, BatchEntryStatus status, string error,
                                           int repairCount = 0, int errorCount = 0, int warningCount = 0, int infoCount = 0) => new()
    {
        Index = index,
        Field = entry.Field,
        Prompt = entry.Prompt,
        Seed = entry.Seed,
        Status = status,
        RepairCount = repairCount,
        ErrorCount = errorCount,
        WarningCount = warningCount,
        InfoCount = infoCount,
        Error = error,
    };

    private static void WriteReport(BatchReport report)
    {
        var path = Path.GetFullPath(Path.Combine(report.OutputDirectory, BatchReport.ReportFileName));
        try
        {
            File.WriteAllText(path, report.ToJson());
            report.ReportPath = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report.ReportError = $"Could not write {path}: {ex.Message}";
        }
    }
}

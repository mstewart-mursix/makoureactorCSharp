using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MakouReactor.AI.Batch;

/// <summary>Outcome of one batch entry.</summary>
public enum BatchEntryStatus
{
    /// <summary>Generated a plan with no validation errors (and applied it, with <c>--apply</c>).</summary>
    Ok,

    /// <summary>Generated a plan, but it still has validation errors after repair; never applied.</summary>
    ValidationErrors,

    /// <summary>Generation, plan writing, preview or apply failed; see <see cref="BatchEntryResult.Error"/>.</summary>
    Failed,

    /// <summary>The entry names a field that is not in the archive (bad input; nothing was generated).</summary>
    UnknownField,
}

/// <summary>Per-entry line of a <see cref="BatchReport"/>.</summary>
public sealed class BatchEntryResult
{
    /// <summary>1-based position of the entry in the batch script.</summary>
    public int Index { get; init; }

    /// <summary>Field name as given in the script.</summary>
    public string Field { get; init; } = string.Empty;

    /// <summary>The scene prompt as given in the script.</summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>The script's seed, recorded for reference only (backends do not support seeding).</summary>
    public int? Seed { get; init; }

    /// <summary>Overall status of the entry.</summary>
    public BatchEntryStatus Status { get; init; }

    /// <summary>Repair re-prompts the generator needed (<see cref="SceneGenerationResult.RepairCount"/>).</summary>
    public int RepairCount { get; init; }

    /// <summary>Validation issues of severity Error.</summary>
    public int ErrorCount { get; init; }

    /// <summary>Validation issues of severity Warn.</summary>
    public int WarningCount { get; init; }

    /// <summary>Validation issues of severity Info.</summary>
    public int InfoCount { get; init; }

    /// <summary>File name of the written plan inside the output directory; null when no plan was written.</summary>
    public string? PlanFile { get; init; }

    /// <summary>True when the plan was written into the archive (<c>--apply</c> only).</summary>
    public bool Applied { get; init; }

    /// <summary>Backup created by the archive writer before this entry's apply; null when not applied.</summary>
    public string? BackupPath { get; init; }

    /// <summary>Failure or validation summary; empty for <see cref="BatchEntryStatus.Ok"/>.</summary>
    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// Summary of a <see cref="BatchRunner"/> run, written as <c>batch-report.json</c> and printed as a table.
/// </summary>
public sealed class BatchReport
{
    /// <summary>File name of the JSON report inside the output directory.</summary>
    public const string ReportFileName = "batch-report.json";

    /// <summary>Exit code: every entry succeeded.</summary>
    public const int ExitOk = 0;

    /// <summary>Exit code: bad input (missing archive, malformed script, unknown field) or an IO failure writing the report.</summary>
    public const int ExitBadInput = 1;

    /// <summary>Exit code: at least one entry had validation errors or failed (or the run was cancelled).</summary>
    public const int ExitEntryProblems = 2;

    /// <summary>Archive the fields were read from (and, with apply, written to).</summary>
    public string ArchivePath { get; init; } = string.Empty;

    /// <summary>Directory holding the plan files and the JSON report.</summary>
    public string OutputDirectory { get; init; } = string.Empty;

    /// <summary>True when the run applied passing plans to the archive.</summary>
    public bool ApplyRequested { get; init; }

    /// <summary>When the run started.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>When the run finished.</summary>
    public DateTimeOffset FinishedAt { get; set; }

    /// <summary>Set when the run could not start (archive missing or unreadable, output directory not creatable).</summary>
    public string InputError { get; init; } = string.Empty;

    /// <summary>True when cancellation stopped the run before every entry was processed.</summary>
    public bool Cancelled { get; set; }

    /// <summary>Processed entries, in script order.</summary>
    public List<BatchEntryResult> Entries { get; } = new();

    /// <summary>Full path of the written JSON report; null when it was not written.</summary>
    public string? ReportPath { get; set; }

    /// <summary>Set when the JSON report could not be written.</summary>
    public string ReportError { get; set; } = string.Empty;

    /// <summary>Number of entries with the given status.</summary>
    public int Count(BatchEntryStatus status) => Entries.Count(e => e.Status == status);

    /// <summary>Number of entries whose plan was applied to the archive.</summary>
    public int AppliedCount => Entries.Count(e => e.Applied);

    /// <summary>
    /// Process exit code: <see cref="ExitBadInput"/> for an input error, an unknown field or a report
    /// write failure; otherwise <see cref="ExitEntryProblems"/> when any entry had validation errors or
    /// failed, or the run was cancelled; otherwise <see cref="ExitOk"/>.
    /// </summary>
    public int ExitCode
    {
        get
        {
            if (InputError.Length > 0 || ReportError.Length > 0 || Count(BatchEntryStatus.UnknownField) > 0)
                return ExitBadInput;
            if (Cancelled || Entries.Any(e => e.Status != BatchEntryStatus.Ok))
                return ExitEntryProblems;
            return ExitOk;
        }
    }

    /// <summary>The status as written in the JSON report and table (snake_case).</summary>
    public static string StatusName(BatchEntryStatus status) => status switch
    {
        BatchEntryStatus.Ok => "ok",
        BatchEntryStatus.ValidationErrors => "validation_errors",
        BatchEntryStatus.Failed => "failed",
        BatchEntryStatus.UnknownField => "unknown_field",
        _ => status.ToString(),
    };

    /// <summary>Serialize the report as indented JSON (the <c>batch-report.json</c> format).</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("archive", ArchivePath);
            writer.WriteString("outputDirectory", OutputDirectory);
            writer.WriteBoolean("apply", ApplyRequested);
            writer.WriteString("startedAt", StartedAt);
            writer.WriteString("finishedAt", FinishedAt);
            writer.WriteBoolean("cancelled", Cancelled);
            writer.WriteNumber("exitCode", ExitCode);

            writer.WriteStartObject("totals");
            writer.WriteNumber("entries", Entries.Count);
            writer.WriteNumber("ok", Count(BatchEntryStatus.Ok));
            writer.WriteNumber("validationErrors", Count(BatchEntryStatus.ValidationErrors));
            writer.WriteNumber("failed", Count(BatchEntryStatus.Failed));
            writer.WriteNumber("unknownField", Count(BatchEntryStatus.UnknownField));
            writer.WriteNumber("applied", AppliedCount);
            writer.WriteEndObject();

            writer.WriteStartArray("entries");
            foreach (var entry in Entries)
            {
                writer.WriteStartObject();
                writer.WriteNumber("index", entry.Index);
                writer.WriteString("field", entry.Field);
                writer.WriteString("prompt", entry.Prompt);
                if (entry.Seed is { } seed)
                    writer.WriteNumber("seed", seed);
                else
                    writer.WriteNull("seed");
                writer.WriteString("status", StatusName(entry.Status));
                writer.WriteNumber("repairCount", entry.RepairCount);
                writer.WriteStartObject("issues");
                writer.WriteNumber("errors", entry.ErrorCount);
                writer.WriteNumber("warnings", entry.WarningCount);
                writer.WriteNumber("infos", entry.InfoCount);
                writer.WriteEndObject();
                WriteNullableString(writer, "planFile", entry.PlanFile);
                writer.WriteBoolean("applied", entry.Applied);
                WriteNullableString(writer, "backup", entry.BackupPath);
                WriteNullableString(writer, "error", entry.Error.Length > 0 ? entry.Error : null);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Human-readable table of the entries plus a totals line, for console output.</summary>
    public string FormatTable()
    {
        var headers = new List<string> { "#", "Field", "Status", "Repairs", "Errors", "Warnings" };
        if (ApplyRequested)
            headers.Add("Applied");
        headers.Add("Message");

        var rows = Entries.Select(e =>
        {
            var row = new List<string>
            {
                e.Index.ToString(CultureInfo.InvariantCulture),
                e.Field,
                StatusName(e.Status),
                e.RepairCount.ToString(CultureInfo.InvariantCulture),
                e.ErrorCount.ToString(CultureInfo.InvariantCulture),
                e.WarningCount.ToString(CultureInfo.InvariantCulture),
            };
            if (ApplyRequested)
                row.Add(e.Applied ? "yes" : "no");
            row.Add(OneLine(e.Error, 100));
            return row;
        }).ToList();

        var widths = headers.Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length))).ToArray();
        var sb = new StringBuilder();
        AppendRow(sb, headers, widths);
        AppendRow(sb, widths.Select(w => new string('-', w)).ToList(), widths);
        foreach (var row in rows)
            AppendRow(sb, row, widths);

        sb.Append(CultureInfo.InvariantCulture,
            $"{Entries.Count} entries: {Count(BatchEntryStatus.Ok)} ok, {Count(BatchEntryStatus.ValidationErrors)} with validation errors, " +
            $"{Count(BatchEntryStatus.Failed)} failed, {Count(BatchEntryStatus.UnknownField)} unknown field");
        if (ApplyRequested)
            sb.Append(CultureInfo.InvariantCulture, $"; {AppliedCount} applied");
        if (Cancelled)
            sb.Append("; cancelled");
        sb.AppendLine();
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, IReadOnlyList<string> cells, int[] widths)
    {
        var line = new StringBuilder();
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
                line.Append("  ");
            line.Append(i == cells.Count - 1 ? cells[i] : cells[i].PadRight(widths[i]));
        }
        sb.AppendLine(line.ToString().TrimEnd());
    }

    private static string OneLine(string text, int max)
    {
        var flat = text.ReplaceLineEndings(" ");
        return flat.Length <= max ? flat : flat[..(max - 3)] + "...";
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value == null)
            writer.WriteNull(name);
        else
            writer.WriteString(name, value);
    }
}

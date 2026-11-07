using System.Text;
using System.Text.Json;

using FluentAssertions;

using MakouReactor.AI;
using MakouReactor.AI.Backends;
using MakouReactor.AI.Batch;
using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Batch;

/// <summary>Phase 8: <see cref="BatchRunner"/> and <see cref="BatchScript"/> with a scripted fake backend.</summary>
public sealed class BatchRunnerTests : IDisposable
{
    private const string GoodJson =
        "{\"meta\":{\"title\":\"Greeting\",\"model\":\"fake\",\"version\":\"1.0\"}," +
        "\"actors\":[{\"id\":\"cloud\",\"position\":{\"x\":100,\"y\":100}}]," +
        "\"dialog\":[{\"speakerId\":\"cloud\",\"text\":\"Let's move.\"}]}";

    /// <summary>Parses, but fails validation: duplicate actor id is an Error.</summary>
    private const string DuplicateActorJson =
        "{\"meta\":{\"title\":\"Greeting\",\"model\":\"fake\",\"version\":\"1.0\"}," +
        "\"actors\":[{\"id\":\"cloud\",\"position\":{\"x\":100,\"y\":100}}," +
        "{\"id\":\"cloud\",\"position\":{\"x\":150,\"y\":150}}]," +
        "\"dialog\":[{\"speakerId\":\"cloud\",\"text\":\"Duplicate.\"}]}";

    private readonly string _dir;
    private readonly string _archive;
    private readonly string _out;

    public BatchRunnerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mr_batch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _archive = Path.Combine(_dir, "flevel.lgp");
        _out = Path.Combine(_dir, "out");
        LgpArchive.Create(_archive, new[]
        {
            new LgpArchiveFile("testmap", FieldPC.CreateEmpty("testmap").SaveCompressed()),
            new LgpArchiveFile("othermap", FieldPC.CreateEmpty("othermap").SaveCompressed()),
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Returns scripted responses in order; records every request.</summary>
    private sealed class ScriptedBackend : ILLMBackend
    {
        private readonly Queue<LLMRawResult> _responses;
        public List<LLMRequest> Requests { get; } = new();
        public bool Disposed { get; private set; }

        public ScriptedBackend(params LLMRawResult[] responses) => _responses = new Queue<LLMRawResult>(responses);

        public Task<LLMRawResult> RequestScenePlanAsync(LLMRequest req)
        {
            Requests.Add(req);
            return Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : new LLMRawResult { Ok = false, Error = "no scripted response left" });
        }

        public void CancelAll() { }
        public void Dispose() => Disposed = true;
    }

    private static LLMRawResult Raw(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(bytes); } catch (JsonException) { }
        return new LLMRawResult { Ok = true, Raw = bytes, Json = doc };
    }

    private static LLMRawResult TransportFailure(string error) => new() { Ok = false, Error = error };

    private static BatchRunner Runner(ScriptedBackend backend, int maxRepairAttempts = 0) =>
        new(new LLMConfig { MaxRepairAttempts = maxRepairAttempts }, backend);

    private BatchOptions Options(bool apply = false) => new() { ArchivePath = _archive, OutputDirectory = _out, Apply = apply };

    private static BatchEntry Entry(string field, string prompt = "Cloud greets the party", int? seed = null) =>
        new() { Field = field, Prompt = prompt, Seed = seed };

    private string[] Backups() => Directory.GetFiles(_dir, "flevel.lgp.mr-backup-*");

    private JsonElement ReadReport()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_out, BatchReport.ReportFileName)));
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task all_entries_ok_write_plans_and_report_with_exit_code_0()
    {
        var backend = new ScriptedBackend(Raw(GoodJson), Raw(GoodJson));

        var report = await Runner(backend).RunAsync(
            new[] { Entry("testmap", "Cloud greets", seed: 42), Entry("othermap", "Tifa waves") }, Options());

        report.ExitCode.Should().Be(0);
        report.Entries.Should().HaveCount(2).And.OnlyContain(e => e.Status == BatchEntryStatus.Ok);
        backend.Requests.Should().HaveCount(2);
        backend.Requests[0].UserPrompt.Should().Contain("Cloud greets").And.Contain("Field: testmap");
        backend.Requests[1].UserPrompt.Should().Contain("Tifa waves").And.Contain("Field: othermap");
        backend.Disposed.Should().BeFalse("an injected backend belongs to the caller");

        File.ReadAllText(Path.Combine(_out, "testmap.plan.json")).Should().Be(GoodJson);
        File.ReadAllText(Path.Combine(_out, "othermap.plan.json")).Should().Be(GoodJson);
        Backups().Should().BeEmpty("nothing is applied without --apply");

        var json = ReadReport();
        json.GetProperty("exitCode").GetInt32().Should().Be(0);
        json.GetProperty("totals").GetProperty("ok").GetInt32().Should().Be(2);
        var first = json.GetProperty("entries")[0];
        first.GetProperty("field").GetString().Should().Be("testmap");
        first.GetProperty("status").GetString().Should().Be("ok");
        first.GetProperty("seed").GetInt32().Should().Be(42);
        first.GetProperty("repairCount").GetInt32().Should().Be(0);
        first.GetProperty("planFile").GetString().Should().Be("testmap.plan.json");
        first.GetProperty("issues").GetProperty("errors").GetInt32().Should().Be(0);
        first.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("entries")[1].GetProperty("seed").ValueKind.Should().Be(JsonValueKind.Null);

        var table = report.FormatTable();
        table.Should().Contain("Field").And.Contain("Status").And.Contain("testmap").And.Contain("othermap");
        table.Should().Contain("2 entries: 2 ok");
    }

    [Fact]
    public async Task failing_and_invalid_entries_do_not_stop_the_batch()
    {
        var backend = new ScriptedBackend(TransportFailure("codex exited with code 1"), Raw(DuplicateActorJson), Raw(GoodJson));

        var report = await Runner(backend).RunAsync(
            new[] { Entry("testmap"), Entry("othermap"), Entry("testmap", "Second scene") }, Options());

        report.ExitCode.Should().Be(2);
        report.Entries.Select(e => e.Status).Should().Equal(
            BatchEntryStatus.Failed, BatchEntryStatus.ValidationErrors, BatchEntryStatus.Ok);
        backend.Requests.Should().HaveCount(3);

        report.Entries[0].Error.Should().Contain("codex exited with code 1");
        report.Entries[0].PlanFile.Should().BeNull();
        report.Entries[1].ErrorCount.Should().BeGreaterThan(0);
        report.Entries[1].Error.Should().Contain("Duplicate actor id");
        report.Entries[1].PlanFile.Should().Be("othermap.plan.json", "plans with validation errors are kept for inspection");
        report.Entries[2].PlanFile.Should().Be("testmap-2.plan.json", "a repeated field gets a distinct plan file");

        File.Exists(Path.Combine(_out, "testmap.plan.json")).Should().BeFalse();
        File.Exists(Path.Combine(_out, "othermap.plan.json")).Should().BeTrue();
        File.Exists(Path.Combine(_out, "testmap-2.plan.json")).Should().BeTrue();

        var totals = ReadReport().GetProperty("totals");
        totals.GetProperty("failed").GetInt32().Should().Be(1);
        totals.GetProperty("validationErrors").GetInt32().Should().Be(1);
        totals.GetProperty("ok").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task repair_count_is_reported()
    {
        var backend = new ScriptedBackend(Raw(DuplicateActorJson), Raw(GoodJson));

        var report = await Runner(backend, maxRepairAttempts: 2).RunAsync(new[] { Entry("testmap") }, Options());

        report.ExitCode.Should().Be(0);
        report.Entries.Single().RepairCount.Should().Be(1);
        ReadReport().GetProperty("entries")[0].GetProperty("repairCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task unknown_field_is_reported_per_entry_and_exits_1()
    {
        var backend = new ScriptedBackend(Raw(GoodJson));

        var report = await Runner(backend).RunAsync(new[] { Entry("nosuchmap"), Entry("testmap") }, Options());

        report.ExitCode.Should().Be(1);
        report.Entries[0].Status.Should().Be(BatchEntryStatus.UnknownField);
        report.Entries[0].Error.Should().Contain("nosuchmap");
        report.Entries[1].Status.Should().Be(BatchEntryStatus.Ok, "later entries still run");
        backend.Requests.Should().ContainSingle("no request is sent for an unknown field");

        var entry = ReadReport().GetProperty("entries")[0];
        entry.GetProperty("status").GetString().Should().Be("unknown_field");
        entry.GetProperty("error").GetString().Should().Contain("nosuchmap");
    }

    [Fact]
    public async Task apply_writes_passing_plans_with_backups_and_skips_failing_ones()
    {
        var otherBefore = FieldArchive.Open(_archive).OpenField("othermap").SaveDecompressed();
        var backend = new ScriptedBackend(Raw(GoodJson), Raw(DuplicateActorJson), TransportFailure("timeout"));

        var report = await Runner(backend).RunAsync(
            new[] { Entry("testmap"), Entry("othermap"), Entry("othermap", "Retry") }, Options(apply: true));

        report.ExitCode.Should().Be(2);
        report.Entries.Select(e => e.Status).Should().Equal(
            BatchEntryStatus.Ok, BatchEntryStatus.ValidationErrors, BatchEntryStatus.Failed);
        report.Entries.Select(e => e.Applied).Should().Equal(true, false, false);
        report.Entries[0].BackupPath.Should().NotBeNullOrEmpty();
        File.Exists(report.Entries[0].BackupPath).Should().BeTrue();
        report.Entries[1].BackupPath.Should().BeNull();

        Backups().Should().ContainSingle("only the passing entry was written");
        var archive = FieldArchive.Open(_archive);
        archive.OpenField("testmap").ScriptsAndTexts!.Texts.Should().Contain(t => t.Value.Contains("Let's move."));
        archive.OpenField("othermap").SaveDecompressed().Should().Equal(otherBefore);

        var json = ReadReport();
        json.GetProperty("apply").GetBoolean().Should().BeTrue();
        json.GetProperty("totals").GetProperty("applied").GetInt32().Should().Be(1);
        json.GetProperty("entries")[0].GetProperty("backup").GetString().Should().Be(report.Entries[0].BackupPath);
        report.FormatTable().Should().Contain("Applied").And.Contain("1 applied");
    }

    [Fact]
    public async Task apply_reads_earlier_batch_writes_for_a_repeated_field()
    {
        var backend = new ScriptedBackend(Raw(GoodJson), Raw(GoodJson));

        var report = await Runner(backend).RunAsync(
            new[] { Entry("testmap"), Entry("testmap", "Again") }, Options(apply: true));

        report.ExitCode.Should().Be(0, report.FormatTable());
        Backups().Should().HaveCount(2);
        FieldArchive.Open(_archive).OpenField("testmap").ScriptsAndTexts!.Texts
            .Count(t => t.Value.Contains("Let's move.")).Should().Be(2, "the second apply builds on the first");
    }

    [Fact]
    public async Task missing_archive_is_an_input_error_and_runs_nothing()
    {
        var backend = new ScriptedBackend(Raw(GoodJson));
        var options = new BatchOptions { ArchivePath = Path.Combine(_dir, "missing.lgp"), OutputDirectory = _out };

        var report = await Runner(backend).RunAsync(new[] { Entry("testmap") }, options);

        report.ExitCode.Should().Be(1);
        report.InputError.Should().Contain("Archive not found");
        report.Entries.Should().BeEmpty();
        backend.Requests.Should().BeEmpty();
        File.Exists(Path.Combine(_out, BatchReport.ReportFileName)).Should().BeFalse();
    }

    [Fact]
    public void script_parses_entries_and_optional_seed()
    {
        var parsed = BatchScript.Parse(
            "[ { \"field\": \"md1stin\", \"prompt\": \"Barret briefs the team\", \"seed\": 7 },\n" +
            "  // comments and trailing commas are tolerated\n" +
            "  { \"field\": \"md1_1\", \"prompt\": \"Guards patrol\" }, ]");

        parsed.Ok.Should().BeTrue(parsed.Error);
        parsed.Entries.Should().HaveCount(2);
        parsed.Entries[0].Field.Should().Be("md1stin");
        parsed.Entries[0].Seed.Should().Be(7);
        parsed.Entries[1].Seed.Should().BeNull();
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("{\"field\":\"a\",\"prompt\":\"b\"}", "JSON array")]
    [InlineData("[]", "no entries")]
    [InlineData("[42]", "Entry 1: expected an object")]
    [InlineData("[{\"prompt\":\"b\"}]", "Entry 1: \"field\" must be a string")]
    [InlineData("[{\"field\":\"a\",\"prompt\":\"b\"},{\"field\":\"a\",\"prompt\":\"  \"}]", "Entry 2: \"prompt\" must not be empty")]
    [InlineData("[{\"field\":\"a\",\"prompt\":\"b\",\"seed\":1.5}]", "\"seed\" must be a 32-bit integer")]
    public void malformed_scripts_are_rejected(string json, string expectedError)
    {
        var parsed = BatchScript.Parse(json);

        parsed.Ok.Should().BeFalse();
        parsed.Error.Should().Contain(expectedError);
        parsed.Entries.Should().BeEmpty();
    }
}

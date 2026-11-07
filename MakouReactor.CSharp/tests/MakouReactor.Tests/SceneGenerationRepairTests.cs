using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using FluentAssertions;

using MakouReactor.AI;
using MakouReactor.AI.Backends;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests;

/// <summary>
/// Phase 3: the self-repair loop in <see cref="SceneGenerationService.GenerateAsync"/>.
/// </summary>
public class SceneGenerationRepairTests
{
    private const string GoodJson =
        "{\"meta\":{\"title\":\"Ambush\",\"model\":\"fake\",\"version\":\"1.0\"}," +
        "\"actors\":[{\"id\":\"cloud\",\"position\":{\"x\":10,\"y\":20}}]," +
        "\"dialog\":[{\"speakerId\":\"cloud\",\"text\":\"Let's move.\"}]}";

    /// <summary>Parses, but fails validation: duplicate actor id is an Error.</summary>
    private const string DuplicateActorJson =
        "{\"meta\":{\"title\":\"Ambush\",\"model\":\"fake\",\"version\":\"1.0\"}," +
        "\"actors\":[{\"id\":\"cloud\",\"position\":{\"x\":10,\"y\":20}}," +
        "{\"id\":\"cloud\",\"position\":{\"x\":30,\"y\":40}}]," +
        "\"dialog\":[{\"speakerId\":\"cloud\",\"text\":\"Let's move.\"}]}";

    /// <summary>Valid JSON, but missing the required meta object.</summary>
    private const string MissingMetaJson = "{\"unexpected\":true}";

    /// <summary>Returns a scripted sequence of responses; the last one repeats.</summary>
    private sealed class ScriptedBackend : ILLMBackend
    {
        private readonly Queue<LLMRawResult> _responses;
        private readonly LLMRawResult _last;
        public List<LLMRequest> Requests { get; } = new();

        public ScriptedBackend(params LLMRawResult[] responses)
        {
            _responses = new Queue<LLMRawResult>(responses);
            _last = responses[^1];
        }

        public Task<LLMRawResult> RequestScenePlanAsync(LLMRequest req)
        {
            Requests.Add(req);
            return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : _last);
        }

        public void CancelAll() { }
        public void Dispose() { }
    }

    private static LLMRawResult Raw(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(bytes); } catch (JsonException) { }
        return new LLMRawResult { Ok = true, Raw = bytes, Json = doc };
    }

    private static ScenePrompt Prompt() => new() { UserText = "Cloud gets ambushed in a reactor corridor" };

    private static SceneGenerationService Service(ScriptedBackend backend, int maxRepairAttempts = 2) =>
        new(new LLMConfig { MaxRepairAttempts = maxRepairAttempts }, backend);

    [Fact]
    public async Task first_good_response_needs_no_repair()
    {
        var backend = new ScriptedBackend(Raw(GoodJson));

        var result = await Service(backend).GenerateAsync(Prompt());

        result.Ok.Should().BeTrue(result.Error);
        backend.Requests.Should().HaveCount(1);
        result.Attempts.Should().ContainSingle().Which.Accepted.Should().BeTrue();
        result.RepairCount.Should().Be(0);
    }

    [Fact]
    public async Task parse_failure_then_good_response_succeeds_after_one_repair()
    {
        var backend = new ScriptedBackend(Raw(MissingMetaJson), Raw(GoodJson));

        var result = await Service(backend).GenerateAsync(Prompt());

        result.Ok.Should().BeTrue(result.Error);
        result.Plan!.Meta!.Title.Should().Be("Ambush");
        result.Attempts.Should().HaveCount(2);
        result.Attempts[0].Accepted.Should().BeFalse();
        result.Attempts[0].Errors.Should().ContainSingle().Which.Should().Contain("meta");
        result.Attempts[1].Accepted.Should().BeTrue();
        result.RepairCount.Should().Be(1);
    }

    [Fact]
    public async Task always_bad_fails_after_max_attempts_with_last_error()
    {
        var backend = new ScriptedBackend(Raw("not json at all"), Raw(MissingMetaJson));

        var result = await Service(backend, maxRepairAttempts: 2).GenerateAsync(Prompt());

        result.Ok.Should().BeFalse();
        backend.Requests.Should().HaveCount(3);
        result.Attempts.Should().HaveCount(3);
        result.Attempts.Should().OnlyContain(a => !a.Accepted);
        result.Error.Should().Contain("meta");
        result.RawJson.Should().Contain("unexpected");
    }

    [Fact]
    public async Task repair_prompt_contains_parser_error_previous_output_and_instruction()
    {
        var backend = new ScriptedBackend(Raw(MissingMetaJson), Raw(GoodJson));

        var result = await Service(backend).GenerateAsync(Prompt());

        var original = backend.Requests[0];
        var repair = backend.Requests[1];
        var parserError = result.Attempts[0].Errors.Single();

        repair.SystemPrompt.Should().Be(original.SystemPrompt);
        repair.UserPrompt.Should().StartWith(original.UserPrompt);
        repair.UserPrompt.Should().Contain(MissingMetaJson);
        repair.UserPrompt.Should().Contain("Fix instructions:");
        repair.UserPrompt.Should().Contain("- " + parserError);
        repair.UserPrompt.Should().EndWith("Return the corrected complete JSON object only.");
        repair.Model.Should().Be(original.Model);
        repair.TimeoutMs.Should().Be(original.TimeoutMs);
    }

    [Fact]
    public async Task validation_error_triggers_repair_with_path_based_message()
    {
        var backend = new ScriptedBackend(Raw(DuplicateActorJson), Raw(GoodJson));

        var result = await Service(backend).GenerateAsync(Prompt());

        result.Ok.Should().BeTrue();
        result.Validation!.HasErrors.Should().BeFalse();
        result.Attempts.Should().HaveCount(2);
        backend.Requests[1].UserPrompt.Should().Contain("actors[1].id: Duplicate actor id 'cloud'");
    }

    [Fact]
    public async Task persistent_validation_errors_return_last_plan_with_errors()
    {
        var backend = new ScriptedBackend(Raw(DuplicateActorJson));

        var result = await Service(backend, maxRepairAttempts: 1).GenerateAsync(Prompt());

        result.Ok.Should().BeTrue("a parsed plan with validation errors is still returned for inspection");
        result.Plan.Should().NotBeNull();
        result.Validation!.HasErrors.Should().BeTrue();
        backend.Requests.Should().HaveCount(2);
        result.Attempts.Should().HaveCount(2).And.OnlyContain(a => !a.Accepted);
    }

    [Fact]
    public async Task parsed_plan_is_kept_when_a_later_repair_is_unparseable()
    {
        var backend = new ScriptedBackend(Raw(DuplicateActorJson), Raw(MissingMetaJson));

        var result = await Service(backend, maxRepairAttempts: 1).GenerateAsync(Prompt());

        result.Ok.Should().BeTrue();
        result.Plan!.Actors.Should().HaveCount(2);
        result.RawJson.Should().Be(DuplicateActorJson);
        result.Attempts.Should().HaveCount(2);
    }

    [Fact]
    public async Task zero_max_repair_attempts_disables_repair()
    {
        var backend = new ScriptedBackend(Raw(MissingMetaJson), Raw(GoodJson));

        var result = await Service(backend, maxRepairAttempts: 0).GenerateAsync(Prompt());

        result.Ok.Should().BeFalse();
        backend.Requests.Should().HaveCount(1);
        result.Attempts.Should().HaveCount(1);
    }

    [Fact]
    public async Task warnings_alone_do_not_trigger_repair()
    {
        // Far outside the default 320x240 bounds: a Warn, not an Error.
        var outOfBounds = GoodJson.Replace("\"x\":10,\"y\":20", "\"x\":99999,\"y\":99999");
        var backend = new ScriptedBackend(Raw(outOfBounds), Raw(GoodJson));

        var result = await Service(backend).GenerateAsync(Prompt());

        result.Ok.Should().BeTrue();
        result.Validation!.HasErrors.Should().BeFalse();
        backend.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task transport_failure_is_not_retried()
    {
        var backend = new ScriptedBackend(LLMRawResult.Failure(0, "Codex CLI exited with code 1: not logged in"), Raw(GoodJson));

        var result = await Service(backend).GenerateAsync(Prompt());

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("not logged in");
        backend.Requests.Should().HaveCount(1);
        result.Attempts.Should().ContainSingle().Which.Errors.Should().ContainSingle().Which.Should().Contain("not logged in");
    }

    [Fact]
    public async Task transport_failure_during_repair_keeps_previous_plan()
    {
        var backend = new ScriptedBackend(Raw(DuplicateActorJson), LLMRawResult.Failure(0, "Request cancelled"));

        var result = await Service(backend).GenerateAsync(Prompt());

        result.Ok.Should().BeTrue();
        result.Plan!.Actors.Should().HaveCount(2);
        result.Validation!.HasErrors.Should().BeTrue();
        backend.Requests.Should().HaveCount(2);
        result.Attempts.Should().HaveCount(2);
        result.Attempts[1].Errors.Should().ContainSingle().Which.Should().Be("Request cancelled");
    }

    [Fact]
    public void repair_request_truncates_long_output_and_caps_error_list()
    {
        var original = new LLMRequest { SystemPrompt = "sys", UserPrompt = "user", Model = "m" };
        var longOutput = new string('x', SceneGenerationService.MaxEchoedOutputChars + 500);
        var errors = Enumerable.Range(0, SceneGenerationService.MaxListedErrors + 5)
            .Select(i => $"actors[{i}].id: Empty actor id")
            .ToList();

        var repair = SceneGenerationService.BuildRepairRequest(original, longOutput, errors);

        repair.UserPrompt.Should().Contain("...[truncated]");
        repair.UserPrompt.Should().NotContain(longOutput);
        repair.UserPrompt.Should().Contain($"actors[{SceneGenerationService.MaxListedErrors - 1}].id");
        repair.UserPrompt.Should().NotContain($"actors[{SceneGenerationService.MaxListedErrors}].id");
        repair.UserPrompt.Should().Contain("...and 5 more error(s)");
    }

    [Fact]
    public void attempt_summary_is_human_readable()
    {
        new GenerationAttempt { Number = 1 }.Summary.Should().Be("Attempt 1: ok");
        new GenerationAttempt { Number = 2, Errors = new[] { "a", "b" } }.Summary.Should().Be("Attempt 2: a; b");
    }
}

using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using FluentAssertions;

using MakouReactor.AI;
using MakouReactor.AI.Backends;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests;

public class SceneGenerationServiceTests
{
    private const string SceneJson =
        "{\"meta\":{\"title\":\"Ambush\",\"model\":\"fake\",\"version\":\"1.0\"}," +
        "\"actors\":[{\"id\":\"cloud\",\"position\":{\"x\":10,\"y\":20}}]," +
        "\"dialog\":[{\"speakerId\":\"cloud\",\"text\":\"Let's move.\"}]}";

    private sealed class FakeBackend : ILLMBackend
    {
        private readonly LLMRawResult _result;
        public LLMRequest? LastRequest;

        public FakeBackend(LLMRawResult result) => _result = result;

        public Task<LLMRawResult> RequestScenePlanAsync(LLMRequest req)
        {
            LastRequest = req;
            return Task.FromResult(_result);
        }

        public void CancelAll() { }
        public void Dispose() { }
    }

    private static LLMRawResult RawFrom(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return new LLMRawResult { Ok = true, Raw = bytes, Json = JsonDocument.Parse(bytes) };
    }

    private static ScenePrompt Prompt() => new() { UserText = "Cloud gets ambushed in a reactor corridor" };

    [Fact]
    public async Task generates_plan_from_bare_json_backend()
    {
        var service = new SceneGenerationService(new LLMConfig(), new FakeBackend(RawFrom(SceneJson)));

        var result = await service.GenerateAsync(Prompt());

        result.Ok.Should().BeTrue(result.Error);
        result.Plan!.Meta!.Title.Should().Be("Ambush");
        result.Plan.Actors.Should().ContainSingle(a => a.Id == "cloud");
        result.Validation.Should().NotBeNull();
        result.Validation!.HasErrors.Should().BeFalse();
    }

    [Fact]
    public async Task generates_plan_from_openai_envelope_backend()
    {
        var envelope = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content = SceneJson } } }
        });
        var service = new SceneGenerationService(new LLMConfig { Backend = "http" }, new FakeBackend(RawFrom(envelope)));

        var result = await service.GenerateAsync(Prompt());

        result.Ok.Should().BeTrue(result.Error);
        result.Plan!.Meta!.Title.Should().Be("Ambush");
    }

    [Fact]
    public async Task backend_failure_propagates()
    {
        var failure = LLMRawResult.Failure(0, "Codex CLI exited with code 1: not logged in");
        var service = new SceneGenerationService(new LLMConfig(), new FakeBackend(failure));

        var result = await service.GenerateAsync(Prompt());

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("not logged in");
    }

    [Fact]
    public async Task parse_failure_keeps_raw_json_for_diagnostics()
    {
        var service = new SceneGenerationService(new LLMConfig(), new FakeBackend(RawFrom("{\"unexpected\":true}")));

        var result = await service.GenerateAsync(Prompt());

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("meta");
        result.RawJson.Should().Contain("unexpected");
    }

    [Fact]
    public void codex_request_uses_codex_model_and_long_timeout()
    {
        var config = new LLMConfig { Backend = "codex", CodexModel = "gpt-5-codex", Model = "qwen/qwen3-coder-30b" };
        var service = new SceneGenerationService(config, new FakeBackend(RawFrom(SceneJson)));

        var request = service.BuildRequest(Prompt());

        request.Model.Should().Be("gpt-5-codex");
        request.TimeoutMs.Should().Be(300_000);
    }

    [Fact]
    public void http_request_uses_http_model_endpoint_and_key()
    {
        var config = new LLMConfig
        {
            Backend = "http",
            Endpoint = "http://localhost:1234/v1/chat/completions",
            ApiKey = "lm-studio",
            Model = "qwen/qwen3-coder-30b"
        };
        var service = new SceneGenerationService(config, new FakeBackend(RawFrom(SceneJson)));

        var request = service.BuildRequest(Prompt());

        request.Model.Should().Be("qwen/qwen3-coder-30b");
        request.EndpointUrl.Should().Be("http://localhost:1234/v1/chat/completions");
        request.ApiKey.Should().Be("lm-studio");
        request.TimeoutMs.Should().Be(60_000);
    }

    [Fact]
    public void explicit_timeout_overrides_defaults()
    {
        var config = new LLMConfig { TimeoutMs = 12_345 };
        var service = new SceneGenerationService(config, new FakeBackend(RawFrom(SceneJson)));

        service.BuildRequest(Prompt()).TimeoutMs.Should().Be(12_345);
    }

    private sealed class ScriptedBackend : ILLMBackend
    {
        private readonly Queue<LLMRawResult> _results;
        public List<LLMRequest> Requests { get; } = new();

        public ScriptedBackend(params LLMRawResult[] results) => _results = new Queue<LLMRawResult>(results);

        public Task<LLMRawResult> RequestScenePlanAsync(LLMRequest req)
        {
            Requests.Add(req);
            return Task.FromResult(_results.Count > 1 ? _results.Dequeue() : _results.Peek());
        }

        public void CancelAll() { }
        public void Dispose() { }
    }

    [Fact]
    public async Task repairs_unparseable_output_on_second_attempt()
    {
        var backend = new ScriptedBackend(RawFrom("{\"unexpected\":true}"), RawFrom(SceneJson));
        var service = new SceneGenerationService(new LLMConfig { MaxRepairAttempts = 2 }, backend);

        var result = await service.GenerateAsync(Prompt());

        result.Ok.Should().BeTrue(result.Error);
        result.Attempts.Should().HaveCount(2);
        result.Attempts[0].Should().Contain("meta");
        result.Attempts[1].Should().BeEmpty();
        backend.Requests[1].UserPrompt.Should().Contain("Fix instructions:")
            .And.Contain(result.Attempts[0])
            .And.Contain("unexpected")
            .And.EndWith("Return the corrected complete JSON object only.");
    }

    [Fact]
    public async Task fails_with_last_error_after_max_repair_attempts()
    {
        var backend = new ScriptedBackend(RawFrom("{\"unexpected\":true}"));
        var service = new SceneGenerationService(new LLMConfig { MaxRepairAttempts = 2 }, backend);

        var result = await service.GenerateAsync(Prompt());

        result.Ok.Should().BeFalse();
        result.Attempts.Should().HaveCount(3);
        backend.Requests.Should().HaveCount(3);
        result.Error.Should().Contain("meta");
    }

    [Fact]
    public async Task zero_repair_attempts_calls_backend_once()
    {
        var backend = new ScriptedBackend(RawFrom("{\"unexpected\":true}"));
        var service = new SceneGenerationService(new LLMConfig { MaxRepairAttempts = 0 }, backend);

        var result = await service.GenerateAsync(Prompt());

        result.Ok.Should().BeFalse();
        backend.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task transport_failure_is_not_retried()
    {
        var backend = new ScriptedBackend(LLMRawResult.Failure(0, "not logged in"));
        var service = new SceneGenerationService(new LLMConfig { MaxRepairAttempts = 2 }, backend);

        var result = await service.GenerateAsync(Prompt());

        result.Ok.Should().BeFalse();
        backend.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task validation_errors_trigger_repair_then_return_plan_with_issues_if_unfixed()
    {
        const string badJson =
            "{\"meta\":{\"title\":\"X\",\"model\":\"fake\",\"version\":\"1.0\"}," +
            "\"actors\":[{\"id\":\"cloud\"}]," +
            "\"dialog\":[{\"speakerId\":\"ghost\",\"text\":\"Boo\"}]}";
        var backend = new ScriptedBackend(RawFrom(badJson));
        var service = new SceneGenerationService(new LLMConfig { MaxRepairAttempts = 1 }, backend);

        var result = await service.GenerateAsync(Prompt());

        backend.Requests.Should().HaveCount(2);
        backend.Requests[1].UserPrompt.Should().Contain("ghost");
        result.Ok.Should().BeTrue();
        result.Validation!.HasErrors.Should().BeTrue();
        result.Attempts.Should().HaveCount(2);
    }

    [Fact]
    public async Task generate_sends_field_context_to_backend_when_field_given()
    {
        var backend = new FakeBackend(RawFrom(SceneJson));
        var service = new SceneGenerationService(new LLMConfig(), backend);

        await service.GenerateAsync(Prompt(), new MeshField("md1stin", MeshField.Square(0, 100)));

        backend.LastRequest!.UserPrompt.Should().Contain("Field Context:").And.Contain("Field: md1stin");
    }

    [Fact]
    public async Task generate_without_field_has_no_field_context()
    {
        var backend = new FakeBackend(RawFrom(SceneJson));
        var service = new SceneGenerationService(new LLMConfig(), backend);

        await service.GenerateAsync(Prompt());

        backend.LastRequest!.UserPrompt.Should().NotContain("Field Context:");
    }
}

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
}

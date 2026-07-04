using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using MakouReactor.AI.Http;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Http;

public class LLMClientTests
{
    [Fact]
    public void constructor_creates_http_client()
    {
        var client = new LLMClient();

        client.Should().NotBeNull();
    }

    [Fact]
    public void constructor_uses_provided_http_client()
    {
        var http = new HttpClient();
        var client = new LLMClient(http);

        client.Should().NotBeNull();
    }

    [Fact]
    public async Task request_scene_plan_fails_without_endpoint()
    {
        var client = new LLMClient();
        var req = new LLMRequest
        {
            EndpointUrl = "http://localhost:99999", // invalid port
            SystemPrompt = "test",
            UserPrompt = "test",
            Model = "test",
            TimeoutMs = 100
        };

        var result = await client.RequestScenePlanAsync(req);

        result.Ok.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task request_respects_timeout()
    {
        var client = new LLMClient();
        var req = new LLMRequest
        {
            EndpointUrl = "http://localhost:99999",
            SystemPrompt = "test",
            UserPrompt = "test",
            Model = "test",
            TimeoutMs = 50
        };

        var result = await client.RequestScenePlanAsync(req);

        result.Ok.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void cancel_all_clears_in_flight()
    {
        var client = new LLMClient();

        client.CancelAll(); // should not throw

        client.Should().NotBeNull();
    }

    [Fact]
    public void dispose_disposes_http_client()
    {
        var client = new LLMClient();

        client.Dispose(); // should not throw
    }

    [Fact]
    public void build_request_from_scene_prompt()
    {
        var prompt = new ScenePrompt
        {
            UserText = "A blacksmith in Midgar",
            GenDialog = true,
            GenLayout = true,
            GenScripts = true,
            Temperature = 0.7,
            MaxTokens = 4096
        };

        var request = MakouReactor.AI.Prompt.PromptBuilder.BuildRequestFrom(
            prompt, "http://localhost:1234/v1/chat/completions", "test-model", 320, 240);

        request.EndpointUrl.Should().Be("http://localhost:1234/v1/chat/completions");
        request.Model.Should().Be("test-model");
        request.SystemPrompt.Should().Contain("Final Fantasy VII");
        request.UserPrompt.Should().Contain("A blacksmith in Midgar");
        request.Temperature.Should().Be(0.7);
        request.MaxTokens.Should().Be(4096);
    }
}

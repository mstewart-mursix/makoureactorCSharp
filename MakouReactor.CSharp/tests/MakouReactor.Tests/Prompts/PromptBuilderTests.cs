using FluentAssertions;

using MakouReactor.AI.Prompt;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Prompt;

public class PromptBuilderTests
{
    [Fact]
    public void system_prompt_returns_non_empty()
    {
        var prompt = PromptBuilder.SystemPrompt();

        prompt.Should().NotBeNullOrWhiteSpace();
        prompt.Should().Contain("Final Fantasy VII");
        prompt.Should().Contain("Makou Reactor");
    }

    [Fact]
    public void schema_text_returns_non_empty()
    {
        var schema = PromptBuilder.SchemaText();

        schema.Should().NotBeNullOrWhiteSpace();
        schema.Should().Contain("ScenePlanJSON");
        schema.Should().Contain("EventStep");
    }

    [Fact]
    public void user_prompt_includes_user_text()
    {
        var prompt = new ScenePrompt { UserText = "A blacksmith in Midgar" };
        var userPrompt = PromptBuilder.UserPrompt(prompt, 320, 240);

        userPrompt.Should().Contain("A blacksmith in Midgar");
    }

    [Fact]
    public void user_prompt_includes_field_size()
    {
        var prompt = new ScenePrompt { UserText = "test" };
        var userPrompt = PromptBuilder.UserPrompt(prompt, 640, 480);

        userPrompt.Should().Contain("width=640");
        userPrompt.Should().Contain("height=480");
    }

    [Fact]
    public void user_prompt_includes_generation_flags()
    {
        var prompt = new ScenePrompt
        {
            UserText = "test",
            GenDialog = true,
            GenLayout = false,
            GenScripts = true
        };
        var userPrompt = PromptBuilder.UserPrompt(prompt, 320, 240);

        userPrompt.Should().Contain("genDialog: true");
        userPrompt.Should().Contain("genLayout: false");
        userPrompt.Should().Contain("genScripts: true");
    }

    [Fact]
    public void user_prompt_includes_schema()
    {
        var prompt = new ScenePrompt { UserText = "test" };
        var userPrompt = PromptBuilder.UserPrompt(prompt, 320, 240);

        userPrompt.Should().Contain("ScenePlanJSON");
    }

    [Fact]
    public void build_request_from_scene_prompt()
    {
        var scenePrompt = new ScenePrompt
        {
            UserText = "Slum area",
            Temperature = 0.5,
            MaxTokens = 1024
        };

        var request = PromptBuilder.BuildRequestFrom(scenePrompt, "http://localhost:1234", "model-x", 320, 240);

        request.EndpointUrl.Should().Be("http://localhost:1234");
        request.Model.Should().Be("model-x");
        request.Temperature.Should().Be(0.5);
        request.MaxTokens.Should().Be(1024);
        request.Stream.Should().BeFalse();
        request.SystemPrompt.Should().Contain("Final Fantasy VII");
        request.UserPrompt.Should().Contain("Slum area");
    }

    [Fact]
    public void build_request_uses_system_prompt()
    {
        var scenePrompt = new ScenePrompt { UserText = "test" };

        var request = PromptBuilder.BuildRequestFrom(scenePrompt, "http://localhost", "m", 320, 240);

        request.SystemPrompt.Should().Be(PromptBuilder.SystemPrompt());
    }
}

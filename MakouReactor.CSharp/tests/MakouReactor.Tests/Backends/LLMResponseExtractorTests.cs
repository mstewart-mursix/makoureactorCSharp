using System.Text;
using System.Text.Json;

using FluentAssertions;

using MakouReactor.AI.Backends;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Backends;

public class LLMResponseExtractorTests
{
    private const string SceneJson = "{\"meta\":{\"title\":\"T\",\"model\":\"m\",\"version\":\"1.0\"}}";

    private static LLMRawResult FromJson(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return new LLMRawResult { Ok = true, Raw = bytes, Json = JsonDocument.Parse(bytes) };
    }

    [Fact]
    public void extracts_content_from_openai_envelope()
    {
        var envelope = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content = SceneJson } } }
        });

        var bytes = LLMResponseExtractor.ExtractScenePlanJson(FromJson(envelope), out var error);

        error.Should().BeEmpty();
        Encoding.UTF8.GetString(bytes!).Should().Be(SceneJson);
    }

    [Fact]
    public void extracts_fenced_content_from_envelope()
    {
        var envelope = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content = $"```json\n{SceneJson}\n```" } } }
        });

        var bytes = LLMResponseExtractor.ExtractScenePlanJson(FromJson(envelope), out var error);

        error.Should().BeEmpty();
        Encoding.UTF8.GetString(bytes!).Should().Be(SceneJson);
    }

    [Fact]
    public void passes_through_bare_scene_plan_json()
    {
        var bytes = LLMResponseExtractor.ExtractScenePlanJson(FromJson(SceneJson), out var error);

        error.Should().BeEmpty();
        Encoding.UTF8.GetString(bytes!).Should().Be(SceneJson);
    }

    [Fact]
    public void empty_choices_reports_error()
    {
        var envelope = "{\"choices\":[]}";

        var bytes = LLMResponseExtractor.ExtractScenePlanJson(FromJson(envelope), out var error);

        bytes.Should().BeNull();
        error.Should().Contain("no choices");
    }

    [Fact]
    public void missing_content_reports_error()
    {
        var envelope = "{\"choices\":[{\"message\":{\"role\":\"assistant\"}}]}";

        var bytes = LLMResponseExtractor.ExtractScenePlanJson(FromJson(envelope), out var error);

        bytes.Should().BeNull();
        error.Should().Contain("no message content");
    }

    [Fact]
    public void empty_raw_reports_error()
    {
        var raw = new LLMRawResult { Ok = true };

        var bytes = LLMResponseExtractor.ExtractScenePlanJson(raw, out var error);

        bytes.Should().BeNull();
        error.Should().Contain("Empty");
    }
}

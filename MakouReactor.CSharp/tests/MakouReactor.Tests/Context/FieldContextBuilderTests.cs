using FluentAssertions;

using MakouReactor.AI.Prompt;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Context;

public class FieldContextBuilderTests
{
    [Fact]
    public void describe_includes_name_walkmesh_bounds_and_ascii_grid()
    {
        var field = new MeshField("md1stin", MeshField.LShape());

        var text = FieldContextBuilder.Describe(field);

        text.Should().StartWith("Field: md1stin");
        text.Should().Contain("Walkable bounds: x 0..200, y 0..300 (4 triangles)");
        text.Should().Contain("walkmesh units");
        text.Should().Contain("16x12");
        text.Split('\n').Count(l => l.Length == 16 && l.All(c => c is '#' or '.')).Should().Be(12);
    }

    [Fact]
    public void describe_without_walkmesh_says_so()
    {
        var text = FieldContextBuilder.Describe(new MeshField("empty"));

        text.Should().Contain("Walkmesh: none available");
    }

    [Fact]
    public void describe_is_deterministic()
    {
        var field = new MeshField("a", MeshField.LShape());

        FieldContextBuilder.Describe(field).Should().Be(FieldContextBuilder.Describe(field));
    }

    [Fact]
    public void describe_is_capped_and_keeps_required_sections()
    {
        var field = new MeshField("tiny", MeshField.LShape());

        var text = FieldContextBuilder.Describe(field, maxChars: 300);

        text.Length.Should().BeLessThanOrEqualTo(300);
        text.Should().StartWith("Field: tiny");
    }

    [Fact]
    public void describe_includes_existing_dialog_and_groups_from_a_real_field()
    {
        var field = FieldPC.CreateEmpty("md1stin");

        var text = FieldContextBuilder.Describe(field);

        text.Should().Contain("Existing dialog");
        text.Should().Contain("Hello world!");
    }

    [Fact]
    public void user_prompt_includes_context_only_when_provided()
    {
        var prompt = new ScenePrompt { UserText = "Cloud arrives" };

        var without = PromptBuilder.UserPrompt(prompt, 320, 240);
        var with = PromptBuilder.UserPrompt(prompt, 320, 240, "Field: md1stin");

        without.Should().NotContain("Field Context:");
        with.Should().Contain("Field Context:").And.Contain("Field: md1stin")
            .And.Contain("Place all positions inside the walkable region")
            .And.Contain("Do not reuse existing group names.");
    }
}

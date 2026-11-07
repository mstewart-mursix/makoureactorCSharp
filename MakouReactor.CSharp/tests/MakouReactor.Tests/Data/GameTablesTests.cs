using FluentAssertions;

using MakouReactor.AI.Parsing;
using MakouReactor.AI.Validation;
using MakouReactor.Core.Data;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Data;

public class GameTablesTests
{
    [Fact]
    public void music_table_matches_cpp_reference_shape()
    {
        GameTables.MusicTracks.Should().HaveCount(100);
        GameTables.MusicTracks.Select(t => t.Id).Should().Equal(Enumerable.Range(0, 100));
        GameTables.MusicTracks[35].Should().Be(new MusicTrack(35, "tifa", "Tifa's Theme"));
        GameTables.MusicTracks[13].Name.Should().Be("earis");
    }

    [Theory]
    [InlineData("tifa", 35)]
    [InlineData("TIFA", 35)]
    [InlineData("Tifa's Theme", 35)]
    [InlineData("tifas-theme", 35)]
    [InlineData("35", 35)]
    [InlineData("Aerith's Theme", 13)]
    public void find_music_matches_name_title_or_id(string query, int expectedId)
    {
        var result = GameTables.FindMusic(query);

        result.Found.Should().BeTrue();
        result.Match!.Id.Should().Be(expectedId);
    }

    [Fact]
    public void find_music_suggests_close_names_when_missing()
    {
        var result = GameTables.FindMusic("Tifas Thme");

        result.Found.Should().BeFalse();
        result.Suggestions.Should().NotBeEmpty();
        result.Suggestions[0].Id.Should().Be(35);
    }

    [Fact]
    public void find_music_gives_nothing_for_unrelated_query()
    {
        var result = GameTables.FindMusic("zzzzzzzzzzzzzzzz");

        result.Found.Should().BeFalse();
        result.Suggestions.Should().BeEmpty();
    }

    [Fact]
    public void edit_distance_basics()
    {
        GameTables.EditDistance("kitten", "sitting").Should().Be(3);
        GameTables.EditDistance("", "abc").Should().Be(3);
        GameTables.EditDistance("same", "same").Should().Be(0);
    }

    [Theory]
    [InlineData("tifa", false)]
    [InlineData("Tifa's Theme", false)]
    [InlineData("tifas thme", true)]
    public void validator_warns_on_unknown_music_with_suggestion(string track, bool expectWarning)
    {
        var json = "{\"meta\":{\"title\":\"T\",\"model\":\"m\",\"version\":\"1.0\"}," +
                   "\"events\":[{\"id\":\"e\",\"trigger\":\"on_enter\",\"steps\":[" +
                   $"{{\"type\":\"play_music\",\"track\":\"{track}\"}}]}}]}}";
        var parsed = ScenePlanParser.Parse(System.Text.Encoding.UTF8.GetBytes(json));
        parsed.Ok.Should().BeTrue(parsed.Error);

        var issues = ScenePlanValidator.Validate(parsed.Plan, null).Issues
            .Where(i => i.Path == "events[0].steps[0].track")
            .ToList();

        if (expectWarning)
            issues.Should().ContainSingle().Which.Should().Match<Issue>(i =>
                i.Level == Severity.Warn && i.Message.Contains("did you mean 'tifa' (Tifa's Theme)"));
        else
            issues.Should().BeEmpty();
    }
}

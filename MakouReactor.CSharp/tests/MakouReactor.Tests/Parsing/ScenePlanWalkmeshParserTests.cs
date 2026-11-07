using System.Text;

using FluentAssertions;

using MakouReactor.AI.Parsing;

using Xunit;

namespace MakouReactor.Tests.Parsing;

public class ScenePlanWalkmeshParserTests
{
    private static byte[] Json(string version, string layout) => Encoding.UTF8.GetBytes(
        "{\"meta\":{\"title\":\"T\",\"model\":\"m\",\"version\":\"" + version + "\"}" +
        (layout.Length > 0 ? ",\"layout\":" + layout : string.Empty) + "}");

    [Fact]
    public void version_1_0_without_walkmesh_parses_and_has_no_walkmesh()
    {
        var result = ScenePlanParser.Parse(Json("1.0", "{\"spawnPoint\":{\"x\":1,\"y\":2}}"));

        result.Ok.Should().BeTrue(result.Error);
        result.Plan.Layout.Walkmesh.Should().BeNull();
    }

    [Fact]
    public void version_1_1_walkmesh_regions_parse()
    {
        var layout = "{\"walkmesh\":{\"regions\":[{\"id\":\"hall\",\"polygon\":[" +
                     "{\"x\":0,\"y\":0},{\"x\":100,\"y\":0},{\"x\":100,\"y\":50}]}]}}";

        var result = ScenePlanParser.Parse(Json("1.1", layout));

        result.Ok.Should().BeTrue(result.Error);
        var region = result.Plan.Layout.Walkmesh!.Regions.Should().ContainSingle().Subject;
        region.Id.Should().Be("hall");
        region.Polygon.Should().HaveCount(3);
        region.Polygon[2].Should().Be(new MakouReactor.Core.Models.Point(100, 50));
    }

    [Theory]
    [InlineData("{\"walkmesh\":[]}", "layout.walkmesh")]
    [InlineData("{\"walkmesh\":{\"regions\":{}}}", "layout.walkmesh.regions")]
    [InlineData("{\"walkmesh\":{\"regions\":[{\"polygon\":[]}]}}", "layout.walkmesh.regions[0].id")]
    [InlineData("{\"walkmesh\":{\"regions\":[{\"id\":\"a\"}]}}", "layout.walkmesh.regions[0].polygon")]
    [InlineData("{\"walkmesh\":{\"regions\":[{\"id\":\"a\",\"polygon\":[{\"x\":1}]}]}}", "layout.walkmesh.regions[0].polygon[0]")]
    public void malformed_walkmesh_reports_the_exact_path(string layout, string expectedPath)
    {
        var result = ScenePlanParser.Parse(Json("1.1", layout));

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain(expectedPath);
    }

    [Fact]
    public void fractional_and_large_numbers_are_rounded_instead_of_crashing()
    {
        var layout = "{\"spawnPoint\":{\"x\":12.5,\"y\":-7.4},\"props\":[{\"id\":\"p\",\"position\":{\"x\":1e12,\"y\":3.6}}]}";

        var result = ScenePlanParser.Parse(Json("1.0", layout));

        result.Ok.Should().BeTrue(result.Error);
        result.Plan.Layout.SpawnPoint.Should().Be(new MakouReactor.Core.Models.Point(12, -7));
        result.Plan.Layout.Props[0].Position.Should().Be(new MakouReactor.Core.Models.Point(int.MaxValue, 4));
    }
}

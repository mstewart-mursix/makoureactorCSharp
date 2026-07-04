using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.Core.Services;

using Xunit;

namespace MakouReactor.Tests.Search;

public sealed class FieldSearchServiceTests
{
    [Fact]
    public void search_current_field_finds_text_contents()
    {
        var section = BuildSection();

        var results = new FieldSearchService().SearchCurrentField("md1stin", section, "reactor");

        results.Should().ContainSingle(result =>
            result.Kind == "Text" &&
            result.TextIndex == 0 &&
            result.Location == "Text 0");
    }

    [Fact]
    public void search_current_field_finds_group_names()
    {
        var section = BuildSection();

        var results = new FieldSearchService().SearchCurrentField("md1stin", section, "cloud");

        results.Should().Contain(result =>
            result.Kind == "Group" &&
            result.GroupIndex == 0 &&
            result.GroupName == "cloud");
    }

    [Fact]
    public void search_current_field_finds_opcode_by_name_and_hex_id()
    {
        var section = BuildSection();
        var service = new FieldSearchService();

        var nameResults = service.SearchCurrentField("md1stin", section, "wait");
        var idResults = service.SearchCurrentField("md1stin", section, "0x24");

        nameResults.Should().ContainSingle(result =>
            result.Kind == "Opcode" &&
            result.ScriptIndex == 0 &&
            result.OpcodeOffset == 0);
        idResults.Should().ContainSingle(result =>
            result.Kind == "Opcode" &&
            result.Match.StartsWith("WAIT", StringComparison.Ordinal));
    }

    [Fact]
    public void search_current_field_finds_variable_references()
    {
        var section = BuildSection();

        var results = new FieldSearchService().SearchCurrentField("md1stin", section, "0x44");

        results.Should().ContainSingle(result =>
            result.Kind == "Variable" &&
            result.GroupIndex == 0 &&
            result.ScriptIndex == 1 &&
            result.OpcodeOffset == 0 &&
            result.Match.Contains("bank 0x3", StringComparison.OrdinalIgnoreCase) &&
            result.Match.Contains("address 0x44", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void empty_query_returns_no_results()
    {
        new FieldSearchService().SearchCurrentField("md1stin", BuildSection(), " ")
            .Should().BeEmpty();
    }

    private static Section1File BuildSection()
    {
        var section = new Section1File();
        section.Texts.Add(new FF7String("Reactor gate"));
        section.Texts.Add(new FF7String("Nothing here"));

        var group = new GrpScript("cloud");
        group.SetScript(0, new Script([0x24, 0x10, 0x00, 0x00]));
        group.SetScript(1, new Script([0x95, 0x30, 0x44]));
        section.GrpScripts.Add(group);

        return section;
    }
}

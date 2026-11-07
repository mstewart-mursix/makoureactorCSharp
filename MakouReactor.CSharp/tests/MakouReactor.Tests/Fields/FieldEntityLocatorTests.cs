using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.Core.Services;

using Xunit;

namespace MakouReactor.Tests.Fields;

public class FieldEntityLocatorTests
{
    // CHAR 0 (A1 00, makes the group a model per GrpScript.DetectType), then
    // XYZI: A5, banks[2], x(s16), y(s16), z(s16), i(u16); x=100 (0x64), y=-50 (0xFFCE), z=0, i=3
    private static readonly byte[] ConstantXyzi = [0xA1, 0x00, 0xA5, 0x00, 0x00, 0x64, 0x00, 0xCE, 0xFF, 0x00, 0x00, 0x03, 0x00, 0x00];

    // XYI with X in bank 1 (B1(banks[0]) = 1): position only known at run time
    private static readonly byte[] VariableXyi = [0xA6, 0x10, 0x00, 0x02, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00];

    // XYI: x=-20, y=300, i=1, then RET
    private static readonly byte[] ConstantXyi = [0xA6, 0x00, 0x00, 0xEC, 0xFF, 0x2C, 0x01, 0x01, 0x00, 0x00];

    private static Section1File Section(params (string Name, GrpScriptType Type, byte[] Init)[] groups)
    {
        var section = new Section1File();
        foreach (var (name, type, init) in groups)
        {
            var group = new GrpScript(name);
            group.SetScript(0, new Script(init)); // detects Model from CHAR; keep the caller's type otherwise
            if (type != GrpScriptType.Model)
                group.Type = type;
            section.GrpScripts.Add(group);
        }
        return section;
    }

    [Fact]
    public void finds_constant_placements_and_skips_variables()
    {
        var section = Section(
            ("barret", GrpScriptType.Model, ConstantXyzi),
            ("ghost", GrpScriptType.Model, VariableXyi),
            ("door", GrpScriptType.Location, ConstantXyi),
            ("dir", GrpScriptType.Director, [0x00]));

        var found = FieldEntityLocator.Locate(section);

        found.Should().HaveCount(2);
        found[0].Should().Be(new FieldEntityPosition(0, "barret", 100, -50, "XYZI", true));
        found[1].Should().Be(new FieldEntityPosition(2, "door", -20, 300, "XYI", false));
    }

    [Fact]
    public void null_section_is_empty()
    {
        FieldEntityLocator.Locate(null).Should().BeEmpty();
    }
}

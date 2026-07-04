using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.ViewModels;

public sealed class VariableManagerViewModelTests
{
    [Fact]
    public void view_model_scans_variable_references()
    {
        var viewModel = new VariableManagerViewModel("md1stin", BuildSection());

        viewModel.FieldName.Should().Be("md1stin");
        viewModel.References.Should().HaveCount(3);
        viewModel.SelectedReference.Should().Be(viewModel.References[0]);
        viewModel.HasSelectedReference.Should().BeTrue();
        viewModel.StatusText.Should().Be("3 of 3 variable references shown.");
    }

    [Fact]
    public void query_filters_references_by_opcode_bank_address_or_location()
    {
        var viewModel = new VariableManagerViewModel("md1stin", BuildSection());

        viewModel.Query = "inc";

        viewModel.References.Should().ContainSingle();
        viewModel.References[0].OpcodeName.Should().Be("INC");
        viewModel.StatusText.Should().Be("1 of 3 variable references shown.");

        viewModel.Query = "0x20";
        viewModel.References.Should().ContainSingle(reference => reference.AddressHex == "0x20");

        viewModel.ClearFilter();
        viewModel.References.Should().HaveCount(3);
    }

    private static Section1File BuildSection()
    {
        var section = new Section1File();
        var group = new GrpScript("cloud");
        group.SetScript(0, new Script([0x80, 0x21, 0x10, 0x20]));
        group.SetScript(1, new Script([0x95, 0x30, 0x44]));
        section.GrpScripts.Add(group);
        return section;
    }
}

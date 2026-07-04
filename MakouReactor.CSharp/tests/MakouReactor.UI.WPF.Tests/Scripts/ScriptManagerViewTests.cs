using System;
using System.Threading;
using System.Windows;

using FluentAssertions;

using MakouReactor.UI.WPF.Scripts;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Scripts;

public sealed class ScriptManagerViewTests
{
    [Fact]
    public void script_manager_view_exposes_original_panels_columns_and_empty_state()
    {
        RunSta(() =>
        {
            var view = new ScriptManagerView();

            view.GroupColumnHeaders.Should().Equal("Group", "Type");
            view.ScriptColumnHeaders.Should().Equal("Script", "Size");
            view.OpcodeColumnHeaders.Should().Equal(
                "Offset",
                "Opcode",
                "Id",
                "Size",
                "Arguments",
                "Warning",
                "Raw bytes");
            view.TreeButtonsEnabled.Should().BeFalse();
            view.PlaceholderVisibility.Should().Be(Visibility.Visible);
            view.PlaceholderText.Should().Be("Open an archive and select a field to inspect scripts.");
        });
    }

    [Fact]
    public void script_manager_view_switches_between_flat_opcode_list_and_tree_mode()
    {
        RunSta(() =>
        {
            var view = new ScriptManagerView();
            view.OpcodeTreeSource = new[]
            {
                new ScriptTreeItem(
                    "Main (2 opcode(s))",
                    children:
                    [
                        new ScriptTreeItem("Condition - 0x0000: IFUB"),
                        new ScriptTreeItem("Return - 0x0005: RET"),
                    ]),
            };

            view.OpcodeTreeRootCount.Should().Be(1);
            view.IsTreeMode.Should().BeFalse();

            view.IsTreeMode = true;

            view.IsTreeMode.Should().BeTrue();
            view.OpcodeTreeRootCount.Should().Be(1);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }
}

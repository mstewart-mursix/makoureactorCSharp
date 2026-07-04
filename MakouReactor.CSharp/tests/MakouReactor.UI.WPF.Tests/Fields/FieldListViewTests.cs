using System;
using System.Threading;

using FluentAssertions;

using MakouReactor.UI.WPF.Fields;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Fields;

public sealed class FieldListViewTests
{
    [Fact]
    public void field_list_view_exposes_original_columns_and_toolbar()
    {
        RunSta(() =>
        {
            var view = new FieldListView();

            view.ColumnHeaders.Should().Equal("File", "Id", "Status");
            view.HasCreateDeleteToolbar.Should().BeTrue();
            view.SearchText.Should().Be("Search...");
        });
    }

    [Fact]
    public void field_list_view_exposes_delete_and_rename_action_state()
    {
        RunSta(() =>
        {
            var view = new FieldListView();

            view.CreateFieldActionEnabled.Should().BeFalse();
            view.DeleteFieldActionEnabled.Should().BeFalse();
            view.RenameFieldActionEnabled.Should().BeFalse();

            view.CreateFieldActionEnabled = true;
            view.DeleteFieldActionEnabled = true;
            view.RenameFieldActionEnabled = true;

            view.CreateFieldActionEnabled.Should().BeTrue();
            view.DeleteFieldActionEnabled.Should().BeTrue();
            view.RenameFieldActionEnabled.Should().BeTrue();
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

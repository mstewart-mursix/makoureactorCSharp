using System;
using System.Threading;
using System.Windows;

using FluentAssertions;

using MakouReactor.UI.WPF.Archive;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Archive;

public sealed class ArchiveManagerViewTests
{
    [Fact]
    public void archive_manager_view_exposes_original_columns_commands_and_preview()
    {
        RunSta(() =>
        {
            var view = new ArchiveManagerView();

            view.ColumnHeaders.Should().Equal("Name", "Path", "Directory", "Size");
            view.ExtractCurrentEnabled.Should().BeFalse();
            view.ExtractAllEnabled.Should().BeFalse();
            view.ReplaceCurrentEnabled.Should().BeFalse();
            view.AddEnabled.Should().BeFalse();
            view.RemoveCurrentEnabled.Should().BeFalse();
            view.RenameCurrentEnabled.Should().BeFalse();
            view.PreviewText.Should().Be("Archive preview will appear here.");
            view.PreviewImageVisibility.Should().Be(Visibility.Collapsed);
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

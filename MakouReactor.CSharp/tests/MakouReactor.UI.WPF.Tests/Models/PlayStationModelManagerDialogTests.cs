using System;
using System.Threading;
using System.Windows.Controls;

using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.Models;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Models;

public sealed class PlayStationModelManagerDialogTests
{
    [Fact]
    public void playstation_model_manager_lists_model_loader_records()
    {
        RunSta(() =>
        {
            var loader = FieldModelLoaderPS.Open(
            [
                12, 0, 1, 0,
                1, 2, 3, 4, 5, 6, 7, 8,
            ]);

            var dialog = new PlayStationModelManagerDialog("MD1STIN", loader);
            try
            {
                dialog.ModelCount.Should().Be(1);
                dialog.SelectedModelId.Should().Be(0);
                dialog.FindName("ApplyModelLoaderButton").Should().BeOfType<Button>()
                    .Which.IsEnabled.Should().BeFalse();
                dialog.FindName("ExportAnimationButton").Should().BeOfType<Button>()
                    .Which.IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
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

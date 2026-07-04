using System;
using System.Threading;

using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.Models;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Models;

public sealed class AnimationSelectorDialogTests
{
    [Fact]
    public void animation_selector_lists_model_animations_and_selects_requested_animation()
    {
        RunSta(() =>
        {
            var model = new FieldModelInfoPC(
                0,
                "Cloud",
                "AAAA",
                7,
                512,
                new RgbColor(0x10, 0x20, 0x30),
                [
                    new FieldModelAnimationInfoPC(0, "idle", 1),
                    new FieldModelAnimationInfoPC(1, "walk", 2),
                ]);

            var dialog = new AnimationSelectorDialog("md1stin", model, selectedAnimationId: 1);
            try
            {
                dialog.AnimationCount.Should().Be(2);
                dialog.SelectedAnimationId.Should().Be(1);
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

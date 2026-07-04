using System.Threading;
using System.Windows;
using System.Windows.Controls;

using FluentAssertions;

using MakouReactor.UI.WPF.Batch;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Batch;

public sealed class BatchProcessingDialogTests
{
    [Fact]
    public void dialog_enables_apply_when_supported_operation_is_selected()
    {
        RunSta(() =>
        {
            var dialog = new BatchProcessingDialog(isPcArchive: true);
            try
            {
                Find<Button>(dialog, "ApplyButton").IsEnabled.Should().BeFalse();

                Find<CheckBox>(dialog, "CleanUnusedTextsCheckBox").IsChecked = true;

                Find<Button>(dialog, "ApplyButton").IsEnabled.Should().BeTrue();
                Find<CheckBox>(dialog, "AutosizeTextWindowsCheckBox").IsEnabled.Should().BeTrue();
                Find<CheckBox>(dialog, "DisableBattlesCheckBox").IsEnabled.Should().BeTrue();
                Find<CheckBox>(dialog, "CleanModelLoaderCheckBox").IsEnabled.Should().BeTrue();
                Find<CheckBox>(dialog, "RemoveUnusedBackgroundCheckBox").IsEnabled.Should().BeTrue();
                Find<CheckBox>(dialog, "ResizeBackgroundsCheckBox").IsEnabled.Should().BeTrue();
                Find<CheckBox>(dialog, "RepairBackgroundsCheckBox").IsEnabled.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void dialog_hides_pc_only_operations_for_non_pc_archives()
    {
        RunSta(() =>
        {
            var dialog = new BatchProcessingDialog(isPcArchive: false);
            try
            {
                Find<CheckBox>(dialog, "CleanModelLoaderCheckBox").Visibility.Should().Be(Visibility.Collapsed);
                Find<CheckBox>(dialog, "RemoveUnusedBackgroundCheckBox").Visibility.Should().Be(Visibility.Collapsed);
                Find<CheckBox>(dialog, "RepairBackgroundsCheckBox").Visibility.Should().Be(Visibility.Collapsed);
                Find<CheckBox>(dialog, "ResizeBackgroundsCheckBox").Visibility.Should().Be(Visibility.Collapsed);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static T Find<T>(FrameworkElement root, string name)
        where T : class
    {
        root.FindName(name).Should().BeAssignableTo<T>();
        return (T)root.FindName(name);
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

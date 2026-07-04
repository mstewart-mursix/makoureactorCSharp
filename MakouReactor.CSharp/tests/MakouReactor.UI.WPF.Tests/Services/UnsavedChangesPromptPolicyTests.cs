using FluentAssertions;

using MakouReactor.UI.WPF.Services;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Services;

public sealed class UnsavedChangesPromptPolicyTests
{
    [Fact]
    public void unmodified_field_continues_without_prompt_result()
    {
        UnsavedChangesPromptPolicy.Decide(
                isModified: false,
                hasArchive: false,
                DialogResultChoice.None)
            .Should().Be(UnsavedChangesAction.Continue);
    }

    [Theory]
    [InlineData(DialogResultChoice.Cancel)]
    [InlineData(DialogResultChoice.None)]
    public void cancel_or_closed_prompt_blocks_navigation(DialogResultChoice choice)
    {
        UnsavedChangesPromptPolicy.Decide(
                isModified: true,
                hasArchive: true,
                choice)
            .Should().Be(UnsavedChangesAction.Cancel);
    }

    [Fact]
    public void discard_choice_continues_without_saving()
    {
        UnsavedChangesPromptPolicy.Decide(
                isModified: true,
                hasArchive: true,
                DialogResultChoice.No)
            .Should().Be(UnsavedChangesAction.Continue);
    }

    [Fact]
    public void save_choice_saves_when_archive_is_available()
    {
        UnsavedChangesPromptPolicy.Decide(
                isModified: true,
                hasArchive: true,
                DialogResultChoice.Yes)
            .Should().Be(UnsavedChangesAction.Save);
    }

    [Fact]
    public void save_choice_saves_when_standalone_field_is_current_target()
    {
        UnsavedChangesPromptPolicy.Decide(
                isModified: true,
                hasArchive: false,
                DialogResultChoice.Yes)
            .Should().Be(UnsavedChangesAction.Save);
    }
}

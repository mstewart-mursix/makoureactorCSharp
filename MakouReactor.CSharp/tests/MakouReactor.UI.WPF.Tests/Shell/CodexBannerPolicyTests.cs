using FluentAssertions;

using MakouReactor.AI.Backends;
using MakouReactor.UI.WPF.Shell;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Shell;

public sealed class CodexBannerPolicyTests
{
    [Fact]
    public void ready_hides_banner()
    {
        CodexBannerPolicy.Describe(new CodexSetupStatus(CodexSetupState.Ready, "Logged in", "codex"))
            .Visible.Should().BeFalse();
    }

    [Fact]
    public void not_installed_offers_install_command()
    {
        var content = CodexBannerPolicy.Describe(new CodexSetupStatus(CodexSetupState.NotInstalled, "missing", null));

        content.Visible.Should().BeTrue();
        content.ShowCopyInstall.Should().BeTrue();
        content.ShowLogin.Should().BeFalse();
        content.Message.Should().Contain(CodexSetupChecker.InstallCommand);
    }

    [Fact]
    public void not_logged_in_offers_login()
    {
        var content = CodexBannerPolicy.Describe(new CodexSetupStatus(CodexSetupState.NotLoggedIn, "Not logged in.", "codex"));

        content.Visible.Should().BeTrue();
        content.ShowLogin.Should().BeTrue();
        content.ShowCopyInstall.Should().BeFalse();
        content.Message.Should().Contain("Not logged in.");
    }

    [Fact]
    public void check_failure_is_shown_without_actions()
    {
        var content = CodexBannerPolicy.Describe(new CodexSetupStatus(CodexSetupState.CheckFailed, "timed out", "codex"));

        content.Visible.Should().BeTrue();
        content.ShowLogin.Should().BeFalse();
        content.ShowCopyInstall.Should().BeFalse();
        content.Message.Should().Contain("timed out");
    }
}

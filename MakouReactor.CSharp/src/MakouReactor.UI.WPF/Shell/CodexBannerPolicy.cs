using MakouReactor.AI.Backends;

namespace MakouReactor.UI.WPF.Shell;

/// <summary>What the startup Codex banner shows for a given check result.</summary>
/// <param name="Visible">Whether the banner is shown at all (hidden when Codex is ready).</param>
/// <param name="Message">Banner text.</param>
/// <param name="ShowCopyInstall">Offer to copy the npm install command.</param>
/// <param name="ShowLogin">Offer to launch <c>codex login</c> in a terminal.</param>
public sealed record CodexBannerContent(bool Visible, string Message, bool ShowCopyInstall, bool ShowLogin);

/// <summary>Maps a <see cref="CodexSetupStatus"/> to banner content. Pure, so it is unit-testable.</summary>
public static class CodexBannerPolicy
{
    /// <summary>Banner content for <paramref name="status"/>.</summary>
    public static CodexBannerContent Describe(CodexSetupStatus status) => status.State switch
    {
        CodexSetupState.Ready => new(false, string.Empty, false, false),
        CodexSetupState.NotInstalled => new(true,
            $"LLM scene generation needs the Codex CLI. Install it with: {CodexSetupChecker.InstallCommand}",
            ShowCopyInstall: true, ShowLogin: false),
        CodexSetupState.NotLoggedIn => new(true,
            $"Codex CLI is installed but not logged in. {status.Detail}",
            ShowCopyInstall: false, ShowLogin: true),
        _ => new(true, $"Could not check the Codex CLI: {status.Detail}", ShowCopyInstall: false, ShowLogin: false),
    };
}

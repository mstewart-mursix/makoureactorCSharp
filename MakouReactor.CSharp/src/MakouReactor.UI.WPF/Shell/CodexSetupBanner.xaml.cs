using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using MakouReactor.AI.Backends;

namespace MakouReactor.UI.WPF.Shell;

/// <summary>
/// Dismissible banner reporting whether the Codex CLI is installed and logged in. The check runs
/// asynchronously and never blocks the window.
/// </summary>
public partial class CodexSetupBanner : UserControl
{
    private string _executable = "codex";

    public CodexSetupBanner()
    {
        InitializeComponent();
    }

    /// <summary>Run the setup check for <paramref name="executable"/> and update the banner.</summary>
    public async Task CheckAsync(string executable)
    {
        _executable = string.IsNullOrWhiteSpace(executable) ? "codex" : executable;
        RecheckButton.IsEnabled = false;
        try
        {
            var status = await CodexSetupChecker.CheckAsync(_executable);
            Show(CodexBannerPolicy.Describe(status));
        }
        finally
        {
            RecheckButton.IsEnabled = true;
        }
    }

    /// <summary>Apply banner content (also used directly by tests).</summary>
    public void Show(CodexBannerContent content)
    {
        MessageText.Text = content.Message;
        CopyInstallButton.Visibility = content.ShowCopyInstall ? Visibility.Visible : Visibility.Collapsed;
        LoginButton.Visibility = content.ShowLogin ? Visibility.Visible : Visibility.Collapsed;
        Visibility = content.Visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CopyInstallButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(CodexSetupChecker.InstallCommand);
            MessageText.Text = $"Copied '{CodexSetupChecker.InstallCommand}'. Run it in a terminal, then click Recheck.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageText.Text = $"Could not access the clipboard. Run: {CodexSetupChecker.InstallCommand}";
        }
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var psi = CodexSetupChecker.BuildLoginStartInfo(_executable);
        if (psi == null)
        {
            MessageText.Text = "Codex CLI not found. Install it first.";
            return;
        }

        try
        {
            Process.Start(psi)?.Dispose();
            MessageText.Text = "Finish signing in in the terminal window, then click Recheck.";
        }
        catch (Win32Exception ex)
        {
            MessageText.Text = $"Could not open a terminal for 'codex login': {ex.Message}";
        }
    }

    private async void RecheckButton_Click(object sender, RoutedEventArgs e) => await CheckAsync(_executable);

    private void DismissButton_Click(object sender, RoutedEventArgs e) => Visibility = Visibility.Collapsed;
}

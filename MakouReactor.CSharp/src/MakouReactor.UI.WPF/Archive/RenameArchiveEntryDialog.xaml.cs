using System.Windows;

namespace MakouReactor.UI.WPF.Archive;

public partial class RenameArchiveEntryDialog : Window
{
    public RenameArchiveEntryDialog(string currentArchivePath)
    {
        InitializeComponent();
        ArchivePathBox.Text = currentArchivePath;
        ArchivePathBox.SelectAll();
        ArchivePathBox.Focus();
    }

    public string NewArchivePath { get; private set; } = string.Empty;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var value = ArchivePathBox.Text.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(value))
        {
            StatusText.Text = "Enter an archive-local path.";
            return;
        }

        if (value.Contains("..", StringComparison.Ordinal))
        {
            StatusText.Text = "Parent directory segments are not allowed.";
            return;
        }

        NewArchivePath = value;
        DialogResult = true;
    }
}

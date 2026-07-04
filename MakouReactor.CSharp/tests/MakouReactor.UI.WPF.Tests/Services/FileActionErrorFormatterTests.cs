using System.IO;

using FluentAssertions;

using MakouReactor.UI.WPF.Services;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Services;

public sealed class FileActionErrorFormatterTests
{
    [Fact]
    public void format_includes_action_path_and_exception_details()
    {
        var error = FileActionErrorFormatter.Format(
            "Open",
            new FileNotFoundException("Archive was not found."),
            "C:\\ff7\\flevel.lgp");

        error.Title.Should().Be("Open failed");
        error.StatusText.Should().Be("Open failed.");
        error.Message.Should().Contain("Open could not be completed.");
        error.Message.Should().Contain("Path: C:\\ff7\\flevel.lgp");
        error.Message.Should().Contain("FileNotFoundException: Archive was not found.");
    }

    [Fact]
    public void format_adds_permission_guidance_for_access_denied_errors()
    {
        var error = FileActionErrorFormatter.Format(
            "Save",
            new UnauthorizedAccessException("Access denied."));

        error.Message.Should().Contain("Check file permissions");
    }

    [Fact]
    public void format_unwraps_single_inner_aggregate_exceptions()
    {
        var error = FileActionErrorFormatter.Format(
            "Extract all",
            new AggregateException(new IOException("File is locked.")));

        error.Message.Should().Contain("IOException: File is locked.");
        error.Message.Should().Contain("not locked by another process");
    }
}

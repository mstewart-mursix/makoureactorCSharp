using System.IO;

namespace MakouReactor.UI.WPF.Services;

public sealed record FileActionError(
    string Title,
    string Message,
    string StatusText);

public static class FileActionErrorFormatter
{
    public static FileActionError Format(string action, Exception exception, string? targetPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(exception);

        var root = Unwrap(exception);
        var message = BuildMessage(action, root, targetPath);
        return new FileActionError(
            $"{action} failed",
            message,
            $"{action} failed.");
    }

    private static Exception Unwrap(Exception exception)
    {
        return exception is AggregateException { InnerExceptions.Count: 1 } aggregate
            ? Unwrap(aggregate.InnerExceptions[0])
            : exception;
    }

    private static string BuildMessage(string action, Exception exception, string? targetPath)
    {
        var lines = new List<string>
        {
            $"{action} could not be completed.",
        };

        if (!string.IsNullOrWhiteSpace(targetPath))
            lines.Add($"Path: {targetPath}");

        lines.Add($"{exception.GetType().Name}: {exception.Message}");

        if (exception is UnauthorizedAccessException)
            lines.Add("Check file permissions and whether the file is read-only or in use.");
        else if (exception is IOException)
            lines.Add("Check that the source and destination are available and not locked by another process.");

        return string.Join(Environment.NewLine, lines);
    }
}

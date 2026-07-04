using System.Text.RegularExpressions;

namespace MakouReactor.UI.WPF.Texts;

public sealed record TextLineValidationIssue(int LineNumber, int VisibleLength, int MaxVisibleLength);

public static partial class TextLineValidator
{
    public const int DefaultMaxVisibleLineLength = 100;

    public static IReadOnlyList<TextLineValidationIssue> Validate(
        string text,
        int maxVisibleLineLength = DefaultMaxVisibleLineLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxVisibleLineLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxVisibleLineLength));

        var logicalText = NewPageTokenRegex().Replace(text.Replace("\r\n", "\n", StringComparison.Ordinal), "\n");
        var lines = logicalText.Split('\n');
        var issues = new List<TextLineValidationIssue>();
        for (var index = 0; index < lines.Length; index++)
        {
            var visibleLength = VisibleLength(lines[index]);
            if (visibleLength > maxVisibleLineLength)
                issues.Add(new TextLineValidationIssue(index + 1, visibleLength, maxVisibleLineLength));
        }

        return issues;
    }

    public static int VisibleLength(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return BraceTokenRegex().Replace(line, string.Empty).Length;
    }

    public static string Format(IReadOnlyList<TextLineValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        return issues.Count == 0
            ? string.Empty
            : $"Line {issues[0].LineNumber} is too long ({issues[0].VisibleLength}/{issues[0].MaxVisibleLength} visible characters).";
    }

    [GeneratedRegex("""\{(?:NEW|EOL|NEW PAGE|NEW PAGE 2)\}""", RegexOptions.IgnoreCase)]
    private static partial Regex NewPageTokenRegex();

    [GeneratedRegex("""\{[^}]+\}""")]
    private static partial Regex BraceTokenRegex();
}

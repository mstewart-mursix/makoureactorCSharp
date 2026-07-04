using FluentAssertions;

using MakouReactor.UI.WPF.Texts;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Texts;

public sealed class TextLineValidatorTests
{
    [Fact]
    public void validate_reports_overlong_visible_lines()
    {
        var issues = TextLineValidator.Validate(new string('A', 101));

        issues.Should().ContainSingle()
            .Which.Should().Be(new TextLineValidationIssue(1, 101, 100));
    }

    [Fact]
    public void validate_ignores_control_tokens_when_counting_visible_text()
    {
        var text = string.Concat("{CLOUD}{BLUE}", new string('A', 100), "{WHITE}");

        TextLineValidator.Validate(text).Should().BeEmpty();
        TextLineValidator.VisibleLength(text).Should().Be(100);
    }

    [Fact]
    public void validate_treats_newline_tokens_as_line_breaks()
    {
        var text = string.Concat("Short{NEW PAGE}", new string('B', 101));

        var issues = TextLineValidator.Validate(text);

        issues.Should().ContainSingle()
            .Which.LineNumber.Should().Be(2);
        TextLineValidator.Format(issues).Should().Be("Line 2 is too long (101/100 visible characters).");
    }
}

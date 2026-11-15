using Application.Common.Helpers;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Common.Helpers;

public class HtmlSanitizerTests
{
    [Theory]
    [InlineData("<script>alert(1)</script>hello", "hello")]
    [InlineData("<img src=x onerror=alert(1)>text", "text")]
    [InlineData("<p onclick=\"evil()\">hi</p>", "<p>hi</p>")]
    [InlineData("<iframe src=\"https://evil\"></iframe>ok", "ok")]
    public void Dangerous_markup_is_removed(string input, string expected)
        => HtmlSanitizer.Clean(input).Should().Be(expected);

    [Fact]
    public void Formatting_tags_survive_without_attributes()
        => HtmlSanitizer.Clean("<b>bold</b> and <i>italic</i>").Should().Be("<b>bold</b> and <i>italic</i>");

    [Fact]
    public void Javascript_links_lose_their_href()
        => HtmlSanitizer.Clean("<a href=\"javascript:alert(1)\">x</a>").Should().Be("<a>x</a>");

    [Fact]
    public void Https_links_are_kept_with_safe_rel()
    {
        var result = HtmlSanitizer.Clean("<a href=\"https://example.com/a\">x</a>");
        result.Should().Contain("href=\"https://example.com/a\"").And.Contain("noopener");
    }

    [Fact]
    public void Stray_angle_brackets_are_encoded()
        => HtmlSanitizer.Clean("1 < 2 > 0").Should().Be("1 &lt; 2 &gt; 0");

    [Fact]
    public void Empty_input_gives_empty_string()
        => HtmlSanitizer.Clean(null).Should().BeEmpty();
}

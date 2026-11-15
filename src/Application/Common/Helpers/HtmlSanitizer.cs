using System.Text;
using System.Text.RegularExpressions;

namespace Application.Common.Helpers;

/// <summary>
/// Conservative allow-list sanitizer for user-written rich text (issue descriptions, comments).
/// Only a few formatting tags survive, with NO attributes (except a validated http/https/mailto href on &lt;a&gt;).
/// Everything else — scripts, styles, iframes, event handlers, javascript: URLs — is dropped, and any stray
/// angle bracket in text is encoded, so the stored value is safe for any client (web, email, mobile).
/// </summary>
public static class HtmlSanitizer
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "p","br","b","i","u","s","strong","em","ul","ol","li","code","pre","blockquote","h1","h2","h3","h4","a","span","div"
    };

    private static readonly Regex DangerousBlocks = new(
        @"<(script|style|iframe|object|embed|svg|math|template|noscript)\b[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Comments = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Tag = new(@"<(/?)([a-zA-Z][a-zA-Z0-9]*)((?:""[^""]*""|'[^']*'|[^>""'])*)>", RegexOptions.Compiled);
    private static readonly Regex Href = new(@"\bhref\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Clean(string? html, int maxLength = 20000)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        if (html.Length > maxLength) html = html[..maxLength];

        html = Comments.Replace(html, string.Empty);
        html = DangerousBlocks.Replace(html, string.Empty);

        var sb = new StringBuilder(html.Length);
        var last = 0;
        foreach (Match m in Tag.Matches(html))
        {
            sb.Append(EncodeText(html.AsSpan(last, m.Index - last)));
            last = m.Index + m.Length;

            var closing = m.Groups[1].Value == "/";
            var name = m.Groups[2].Value.ToLowerInvariant();
            if (!Allowed.Contains(name)) continue;

            if (closing) { sb.Append("</").Append(name).Append('>'); continue; }

            if (name == "a")
            {
                var h = Href.Match(m.Groups[3].Value);
                var url = h.Success ? System.Net.WebUtility.HtmlDecode(
                    (h.Groups[1].Success ? h.Groups[1].Value : h.Groups[2].Success ? h.Groups[2].Value : h.Groups[3].Value).Trim()) : null;
                if (IsSafeUrl(url))
                    sb.Append("<a href=\"").Append(System.Net.WebUtility.HtmlEncode(url!)).Append("\" rel=\"noopener noreferrer nofollow\" target=\"_blank\">");
                else
                    sb.Append("<a>");
            }
            else sb.Append('<').Append(name).Append('>');
        }
        sb.Append(EncodeText(html.AsSpan(last)));
        return sb.ToString();
    }

    private static bool IsSafeUrl(string? url)
        => !string.IsNullOrWhiteSpace(url) && url.Length <= 2000 &&
           (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase));

    // Leave &entities; alone (they're already text); encode only the characters that could start markup.
    private static string EncodeText(ReadOnlySpan<char> text)
        => text.ToString().Replace("<", "&lt;").Replace(">", "&gt;");
}

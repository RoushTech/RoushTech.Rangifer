namespace RoushTech.Rangifer.Mail;

using System.Net;
using System.Text;
using System.Text.RegularExpressions;

public static partial class Linkify
{
    /// <summary>Encodes plain text as HTML and turns bare http(s) URLs in it into links.</summary>
    public static string ToHtml(string text)
    {
        var html = new StringBuilder();
        var position = 0;
        foreach (Match match in BareUrl().Matches(text))
        {
            var url = TrimTrailingPunctuation(match.Value);
            var encoded = WebUtility.HtmlEncode(url);
            html.Append(WebUtility.HtmlEncode(text[position..match.Index]));
            html.Append("<a href=\"").Append(encoded).Append("\" style=\"word-break:break-all;\">").Append(encoded).Append("</a>");
            position = match.Index + url.Length;
        }

        html.Append(WebUtility.HtmlEncode(text[position..]));
        return html.ToString();
    }

    // Keeps a closing parenthesis that balances one inside the URL, like a Wikipedia link.
    private static string TrimTrailingPunctuation(string url)
    {
        while (url.Length > 0 && (".,;:!?'\"".Contains(url[^1]) || (url[^1] == ')' && url.Count(c => c == '(') < url.Count(c => c == ')'))))
        {
            url = url[..^1];
        }

        return url;
    }

    [GeneratedRegex("""https?://[^\s<>"]+""", RegexOptions.IgnoreCase)]
    private static partial Regex BareUrl();
}

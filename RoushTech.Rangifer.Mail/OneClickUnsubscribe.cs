namespace RoushTech.Rangifer.Mail;

using MimeKit;

public static class OneClickUnsubscribe
{
    public const string ListUnsubscribeHeader = "List-Unsubscribe";
    public const string ListUnsubscribePostHeader = "List-Unsubscribe-Post";
    public const string ListUnsubscribePostValue = "List-Unsubscribe=One-Click";

    public static IReadOnlyDictionary<string, string> Headers(string url) => new Dictionary<string, string>
    {
        [ListUnsubscribeHeader] = $"<{Validate(url)}>",
        [ListUnsubscribePostHeader] = ListUnsubscribePostValue,
    };

    public static void AddTo(MimeMessage message, string url)
    {
        foreach (var (name, value) in Headers(url))
        {
            message.Headers.Add(name, value);
        }
    }

    // The URL is used as-is, so the link in the body still matches it byte for byte.
    private static string Validate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException($"Unsubscribe URL '{url}' isn't an absolute http(s) URL.", nameof(url));
        }

        if (url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '<' or '>'))
        {
            throw new ArgumentException("Unsubscribe URL can't contain whitespace, control characters or angle brackets.", nameof(url));
        }

        return url;
    }
}

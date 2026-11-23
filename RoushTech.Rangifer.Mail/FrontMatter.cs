namespace RoushTech.Rangifer.Mail;

public sealed record FrontMatter(string TemplateName, IReadOnlyDictionary<string, string> Values, string Body)
{
    public string Subject => Require("subject");

    public string Require(string key) =>
        Values.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"Template '{TemplateName}' is missing '{key}' in frontmatter.");

    public static FrontMatter Parse(string raw, string templateName)
    {
        if (!raw.StartsWith("---", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Template '{templateName}' is missing frontmatter.");
        }

        var end = raw.IndexOf("---", 3, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException($"Template '{templateName}' has unterminated frontmatter.");
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in raw[3..end].Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator > 0)
            {
                values[line[..separator].Trim()] = line[(separator + 1)..].Trim().Trim('"');
            }
        }

        return new FrontMatter(templateName, values, raw[(end + 3)..].TrimStart('\r', '\n'));
    }
}

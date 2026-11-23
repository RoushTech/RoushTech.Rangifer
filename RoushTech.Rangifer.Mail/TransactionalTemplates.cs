namespace RoushTech.Rangifer.Mail;

using System.Collections.Frozen;
using Microsoft.Extensions.FileProviders;

// Reads and parses every template up front so a missing or broken one fails at startup, not at send time.
public sealed class TransactionalTemplates<TEnum>
    where TEnum : struct, Enum
{
    private readonly FrozenDictionary<TEnum, TransactionalTemplate<TEnum>> _templates;

    public TransactionalTemplates(IFileProvider files)
        : this(files, Enum.GetValues<TEnum>())
    {
    }

    public TransactionalTemplates(IFileProvider files, IEnumerable<TEnum> types)
    {
        _templates = types.ToFrozenDictionary(type => type, type =>
        {
            var name = $"{type}.liquid";
            var frontMatter = FrontMatter.Parse(EmailTemplateFiles.Read(files, name), name);
            var template = new TransactionalTemplate<TEnum>(type, frontMatter.Subject, frontMatter.Body);
            ThrowIfInvalid(name, "subject", template.Subject);
            ThrowIfInvalid(name, "body", template.Body);
            return template;
        });
    }

    public IReadOnlyCollection<TEnum> Types => _templates.Keys;

    public TransactionalTemplate<TEnum> Get(TEnum type) =>
        _templates.TryGetValue(type, out var template)
            ? template
            : throw new ArgumentException($"No transactional template loaded for '{type}'.", nameof(type));

    private static void ThrowIfInvalid(string name, string part, string source)
    {
        if (!LiquidEmailRenderer.TryParse(source, out var error))
        {
            throw new InvalidOperationException($"Template '{name}' {part} doesn't parse: {error}");
        }
    }
}

public sealed record TransactionalTemplate<TEnum>(TEnum Type, string Subject, string Body)
    where TEnum : struct, Enum;

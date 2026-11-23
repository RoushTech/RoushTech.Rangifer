namespace RoushTech.Rangifer.Mail;

using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Fluid;
using Fluid.Values;
using Fluid.ViewEngine;
using Microsoft.Extensions.FileProviders;
using PreMailer.Net;

public sealed record RenderedEmail(string Subject, string Html);

/// <summary>
/// HTML output encodes every value; the only way to write markup from a value is an <see cref="HtmlFragment"/>.
/// Templates must not add <c>| escape</c>, which would encode a value twice.
/// </summary>
public sealed partial class LiquidEmailRenderer
{
    private const string LayoutKey = "$$layout";
    private const string BodyKey = "$$body";

    private static readonly FluidViewParser Parser = new();
    private static readonly HtmlEncoder HtmlValueEncoder = HtmlEncoder.Create(UnicodeRanges.All);

    private readonly TemplateOptions _options;

    public LiquidEmailRenderer(IFileProvider templateFiles, Action<TemplateOptions>? configure = null)
    {
        _options = new TemplateOptions { FileProvider = templateFiles };
        _options.Filters.AddFilter("linkify", static (input, _, _) =>
            new ValueTask<FluidValue>(new StringValue(Linkify.ToHtml(input.ToStringValue()), encode: false)));

        // Fluid's own newline_to_br marks its output for encoding, which turns markup from linkify into visible tags.
        _options.Filters.AddFilter("newline_to_br", static (input, _, _) =>
        {
            var isHtml = input is StringValue { Encode: false };
            var lines = input.ToStringValue().ReplaceLineEndings("\n").Split('\n').Select(line => isHtml ? line : HtmlValueEncoder.Encode(line));
            return new ValueTask<FluidValue>(new StringValue(string.Join("<br />\n", lines), encode: false));
        });
        _options.ValueConverters.Add(static value => value is HtmlFragment fragment ? new StringValue(fragment.Html, encode: false) : null);
        configure?.Invoke(_options);
    }

    /// <summary>The layout gets the subject as <c>{{ subject }}</c>, encoded like any other value.</summary>
    public async Task<RenderedEmail> RenderAsync(string subjectTemplate, string bodyTemplate, ExpandoObject model)
    {
        var subject = RenderSubject(subjectTemplate, model);
        var context = new TemplateContext(model, _options);
        context.SetValue("subject", subject);
        return new RenderedEmail(subject, await RenderHtmlAsync(bodyTemplate, context));
    }

    public string RenderSubject(string template, ExpandoObject model) =>
        RenderPlainText(template, model).ReplaceLineEndings(" ").Trim();

    /// <summary>Nothing is encoded. Never put the result in HTML.</summary>
    public string RenderPlainText(string template, ExpandoObject model) =>
        Parse(template, "template").Render(new TemplateContext(model, _options), NullEncoder.Default);

    public Task<string> RenderHtmlAsync(string template, ExpandoObject model) =>
        RenderHtmlAsync(template, new TemplateContext(model, _options));

    private async Task<string> RenderHtmlAsync(string template, TemplateContext context)
    {
        var body = await Parse(template, "template").RenderAsync(context, HtmlValueEncoder);
        if (!context.AmbientValues.TryGetValue(LayoutKey, out var layoutPathValue) || layoutPathValue is not string layoutPath || layoutPath.Length == 0)
        {
            return body;
        }

        context.AmbientValues[BodyKey] = body;
        var layout = Parse(EmailTemplateFiles.Read(_options.FileProvider, layoutPath), $"layout '{layoutPath}'");
        var laidOut = await layout.RenderAsync(context, HtmlValueEncoder);
        return PreMailer.MoveCssInline(laidOut, removeStyleElements: true, stripIdAndClassAttributes: true).Html;
    }

    private static IFluidTemplate Parse(string source, string description) =>
        Parser.TryParse(source, out var template, out var error)
            ? template
            : throw new InvalidOperationException($"Failed to parse Liquid {description}: {error}");

    /// <param name="error">Short enough to show to whoever wrote the template.</param>
    public static bool TryParse(string template, [NotNullWhen(false)] out string? error)
    {
        if (Parser.TryParse(template, out _, out var parseError))
        {
            error = null;
            return true;
        }

        error = ShortenParseError(parseError, template);
        return false;
    }

    // Fluid's parse errors end with the entire template source, so swap that for an excerpt around the position.
    private static string ShortenParseError(string parseError, string template)
    {
        var sourceIndex = parseError.IndexOf("Source:", StringComparison.Ordinal);
        var message = sourceIndex >= 0 ? parseError[..sourceIndex].Trim() : parseError;

        var position = PositionRegex().Match(message);
        var lines = template.Split('\n');
        var line = position.Success ? int.Parse(position.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        if (line < 1 || line > lines.Length)
        {
            return message;
        }

        var column = int.Parse(position.Groups[2].Value, CultureInfo.InvariantCulture);
        var offset = lines.Take(line - 1).Sum(l => l.Length + 1) + Math.Min(Math.Max(column - 1, 0), lines[line - 1].Length);
        var start = Math.Max(0, offset - 30);
        var excerpt = WhitespaceRegex().Replace(template.Substring(start, Math.Min(60, template.Length - start)), " ").Trim();
        return $"{message} near \"{excerpt}\"";
    }

    [GeneratedRegex(@"\((\d+):(\d+)\)")]
    private static partial Regex PositionRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

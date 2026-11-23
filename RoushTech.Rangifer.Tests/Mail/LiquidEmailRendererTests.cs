namespace RoushTech.Rangifer.Tests.Mail;

using Fluid.Values;
using NUnit.Framework;
using RoushTech.Rangifer.Mail;

public class LiquidEmailRendererTests
{
    private static readonly LiquidEmailRenderer Renderer = new(TestTemplates.Files);

    [Test]
    public async Task RenderHtmlAsync_EncodesValues()
    {
        var html = await Renderer.RenderHtmlAsync("<p>{{ name }}</p>", TemplateModel.From(new { Name = "<script>alert(1)</script>" }));

        Assert.That(html, Is.EqualTo("<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>"));
    }

    [Test]
    public async Task RenderHtmlAsync_EncodesAttributeBreakouts()
    {
        var html = await Renderer.RenderHtmlAsync("<a href=\"{{ url }}\">x</a>", TemplateModel.From(new { Url = "\" onclick=\"alert(1)" }));

        Assert.That(html, Does.Not.Contain("\" onclick"));
    }

    [Test]
    public async Task RenderHtmlAsync_LeavesNonAsciiReadable()
    {
        var html = await Renderer.RenderHtmlAsync("<p>{{ name }}</p>", TemplateModel.From(new { Name = "Café 東京" }));

        Assert.That(html, Is.EqualTo("<p>Café 東京</p>"));
    }

    [Test]
    public async Task RenderAsync_EncodesSubjectInLayout()
    {
        var template = Welcome();

        var rendered = await Renderer.RenderAsync(template.Subject, template.Body, TemplateModel.From(new { Name = "</title><a href=\"https://evil.example\">x</a>" }));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Subject, Is.EqualTo("Welcome </title><a href=\"https://evil.example\">x</a>"));
            Assert.That(rendered.Html, Does.Not.Contain("<a href"));
            Assert.That(rendered.Html, Does.Contain("&lt;/title&gt;"));
        });
    }

    [Test]
    public async Task RenderAsync_DoesNotAddSubjectToCallersModel()
    {
        var template = Welcome();
        var model = TemplateModel.From(new { Name = "Ada" });

        await Renderer.RenderAsync(template.Subject, template.Body, model);

        Assert.That(model, Does.Not.ContainKey("subject"));
    }

    [Test]
    public async Task RenderAsync_InlinesLayoutCss()
    {
        var template = Welcome();

        var rendered = await Renderer.RenderAsync(template.Subject, template.Body, TemplateModel.From(new { Name = "Ada" }));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Html, Does.Contain("<p style=\"color: #123456"));
            Assert.That(rendered.Html, Does.Not.Contain("<style>"));
            Assert.That(rendered.Html, Does.Contain("<title>Welcome Ada</title>"));
        });
    }

    [Test]
    public void RenderSubject_IsOneLineAndUnencoded()
    {
        var subject = Renderer.RenderSubject("Hi {{ name }} ", TemplateModel.From(new { Name = "Ada & Co\r\nBcc: someone@example.com" }));

        Assert.That(subject, Is.EqualTo("Hi Ada & Co Bcc: someone@example.com"));
    }

    [Test]
    public void RenderPlainText_DoesNotEncode()
    {
        var text = Renderer.RenderPlainText("{{ name }}", TemplateModel.From(new { Name = "Ada & <Co>" }));

        Assert.That(text, Is.EqualTo("Ada & <Co>"));
    }

    [Test]
    public async Task HtmlFragment_IsWrittenAsIs()
    {
        var html = await Renderer.RenderHtmlAsync("{{ link }}", TemplateModel.From(new { Link = new HtmlFragment("<a href=\"https://example.com\">Open</a>") }));

        Assert.That(html, Is.EqualTo("<a href=\"https://example.com\">Open</a>"));
    }

    [Test]
    public async Task HtmlFragment_SurvivesStoredJson()
    {
        var json = TemplateModel.ToJson(new { Link = new HtmlFragment("<b>bold</b>") });

        var html = await Renderer.RenderHtmlAsync("{{ link }}", TemplateModel.FromJson(json));

        Assert.That(html, Is.EqualTo("<b>bold</b>"));
    }

    [Test]
    public async Task EscapeFilter_EncodesTwice()
    {
        var html = await Renderer.RenderHtmlAsync("{{ name | escape }}", TemplateModel.From(new { Name = "A & B" }));

        Assert.That(html, Is.EqualTo("A &amp;amp; B"));
    }

    [Test]
    public async Task Linkify_EncodesTextAndLinksUrls()
    {
        var html = await Renderer.RenderHtmlAsync("{{ reply | linkify }}", TemplateModel.From(new { Reply = "<b>see</b> https://example.com/a?b=1&c=2." }));

        Assert.That(html, Is.EqualTo(
            "&lt;b&gt;see&lt;/b&gt; <a href=\"https://example.com/a?b=1&amp;c=2\" style=\"word-break:break-all;\">https://example.com/a?b=1&amp;c=2</a>."));
    }

    [Test]
    public async Task Linkify_ThenNewlineToBr_KeepsLinks()
    {
        var html = await Renderer.RenderHtmlAsync("{{ reply | linkify | newline_to_br }}", TemplateModel.From(new { Reply = "one\nhttps://example.com" }));

        Assert.That(html, Does.StartWith("one<br />\n<a href=\"https://example.com\""));
    }

    [Test]
    public async Task NewlineToBr_StillEncodesPlainValues()
    {
        var html = await Renderer.RenderHtmlAsync("{{ reply | newline_to_br }}", TemplateModel.From(new { Reply = "<b>one</b>\r\ntwo" }));

        Assert.That(html, Is.EqualTo("&lt;b&gt;one&lt;/b&gt;<br />\ntwo"));
    }

    [Test]
    public async Task Configure_AddsAppFilters()
    {
        var renderer = new LiquidEmailRenderer(TestTemplates.Files, options =>
            options.Filters.AddFilter("shout", static (input, _, _) => new ValueTask<FluidValue>(new StringValue(input.ToStringValue().ToUpperInvariant()))));

        var html = await renderer.RenderHtmlAsync("{{ name | shout }}", TemplateModel.From(new { Name = "<b>" }));

        Assert.That(html, Is.EqualTo("&lt;B&gt;"));
    }

    [Test]
    public void RenderHtmlAsync_MissingLayout_Throws()
    {
        Assert.That(
            () => Renderer.RenderHtmlAsync("{% layout 'Nope.liquid' %}x", TemplateModel.From(new { })),
            Throws.InvalidOperationException.With.Message.Contains("Nope.liquid"));
    }

    [Test]
    public void TryParse_ShortensError()
    {
        var template = new string('x', 500) + "{% if %}" + new string('y', 500);

        var parsed = LiquidEmailRenderer.TryParse(template, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(error, Does.Not.Contain("Source:"));
            Assert.That(error!.Length, Is.LessThan(300));
        });
    }

    [Test]
    public void TryParse_ValidTemplate()
    {
        Assert.That(LiquidEmailRenderer.TryParse("{{ a }}", out var error), Is.True);
        Assert.That(error, Is.Null);
    }

    private static TransactionalTemplate<TestTemplate> Welcome() =>
        new TransactionalTemplates<TestTemplate>(TestTemplates.Files).Get(TestTemplate.Welcome);
}

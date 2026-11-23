namespace RoushTech.Rangifer.Tests.Mail;

using NUnit.Framework;
using RoushTech.Rangifer.Mail;

public class TemplateFileTests
{
    [Test]
    public void FrontMatter_ReadsValuesAndBody()
    {
        var frontMatter = FrontMatter.Parse("---\r\nsubject: \"Hi: {{ name }}\"\r\nScope: hotel\r\n---\r\n<p>Body</p>", "Test");

        Assert.Multiple(() =>
        {
            Assert.That(frontMatter.Subject, Is.EqualTo("Hi: {{ name }}"));
            Assert.That(frontMatter.Require("scope"), Is.EqualTo("hotel"));
            Assert.That(frontMatter.Body, Is.EqualTo("<p>Body</p>"));
        });
    }

    [Test]
    public void FrontMatter_MissingKey_NamesTemplate()
    {
        var frontMatter = FrontMatter.Parse("---\nname: x\n---\nbody", "Digest");

        Assert.That(() => frontMatter.Subject, Throws.InvalidOperationException.With.Message.Contains("Digest").And.Message.Contains("subject"));
    }

    [TestCase("no frontmatter")]
    [TestCase("---\nsubject: x\nbody")]
    public void FrontMatter_Malformed_Throws(string raw)
    {
        Assert.That(() => FrontMatter.Parse(raw, "Test"), Throws.InvalidOperationException);
    }

    [Test]
    public void TransactionalTemplates_LoadsEveryMember()
    {
        var templates = new TransactionalTemplates<TestTemplate>(TestTemplates.Files);

        var welcome = templates.Get(TestTemplate.Welcome);

        Assert.Multiple(() =>
        {
            Assert.That(templates.Types, Is.EquivalentTo(new[] { TestTemplate.Welcome }));
            Assert.That(welcome.Subject, Is.EqualTo("Welcome {{ name }}"));
            Assert.That(welcome.Body, Does.StartWith("{% layout '_layout.liquid' %}"));
        });
    }

    [Test]
    public void TransactionalTemplates_TemplateThatDoesNotParse_FailsAtLoad()
    {
        Assert.That(() => new TransactionalTemplates<BrokenTemplate>(TestTemplates.Files), Throws.InvalidOperationException.With.Message.Contains("Broken.liquid"));
    }

    [Test]
    public void TransactionalTemplates_MissingFile_FailsAtLoad()
    {
        Assert.That(() => new TransactionalTemplates<MissingTemplate>(TestTemplates.Files), Throws.InvalidOperationException.With.Message.Contains("Missing.liquid"));
    }

    [Test]
    public void TransactionalTemplates_TypeNotLoaded_Throws()
    {
        var templates = new TransactionalTemplates<TestTemplate>(TestTemplates.Files, []);

        Assert.That(() => templates.Get(TestTemplate.Welcome), Throws.ArgumentException);
    }

    [Test]
    public void TemplateModel_UsesCamelCaseAndJsonTypes()
    {
        var model = (IDictionary<string, object?>)TemplateModel.From(new { FirstName = "Ada", Count = 3, Tags = new[] { "a" }, Owner = new { IsAdmin = true }, Missing = (string?)null });

        Assert.Multiple(() =>
        {
            Assert.That(model["firstName"], Is.EqualTo("Ada"));
            Assert.That(model["count"], Is.EqualTo(3d));
            Assert.That(model["tags"], Is.EqualTo(new List<object?> { "a" }));
            Assert.That(((IDictionary<string, object?>)model["owner"]!)["isAdmin"], Is.True);
            Assert.That(model["missing"], Is.Null);
        });
    }

    [Test]
    public void TemplateModel_ObjectWithOtherKeysIsNotAFragment()
    {
        var model = (IDictionary<string, object?>)TemplateModel.FromJson("""{"link":{"__html":"<b>x</b>","other":1}}""");

        Assert.That(model["link"], Is.Not.InstanceOf<HtmlFragment>());
    }
}

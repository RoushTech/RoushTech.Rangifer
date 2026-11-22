namespace RoushTech.Rangifer.Tests;

using NUnit.Framework;
using RoushTech.Rangifer.EntityFrameworkCore.Design;
using RoushTech.Rangifer.Tests.Models;

public class DesignerParserTests
{
    [Test]
    public void FullDesigner_SplitsIntoModelAndEntityPassBlocks()
    {
        var designer = DesignerParser.Parse(GeneratedDesigners.Full(3), null);
        var keys = designer.Blocks.Select(block => block.Key).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(designer.Kind, Is.EqualTo(DesignerKind.Full));
            Assert.That(designer.MigrationId, Is.EqualTo(GeneratedDesigners.MigrationId(3)));
            Assert.That(designer.ContextType, Is.EqualTo(nameof(VersionedContext)));
            Assert.That(keys[0], Is.EqualTo(DesignerParser.ModelBlockKey));
            Assert.That(keys, Does.Contain($"1:{typeof(Blog).FullName}"));
            Assert.That(keys, Does.Contain($"1:{typeof(FeaturedPost).FullName}"));
            Assert.That(keys, Does.Contain($"2:{typeof(Post).FullName}"));
            Assert.That(keys, Does.Contain($"3:{typeof(Blog).FullName}"));
            Assert.That(keys, Is.Unique);
        });
    }

    [Test]
    public void Blocks_AreStoredWithoutSurroundingIndentation()
    {
        var designer = DesignerParser.Parse(GeneratedDesigners.Full(1), null);
        var blog = designer.Blocks.Single(block => block.Key == $"1:{typeof(Blog).FullName}");

        Assert.That(blog.Text.Split('\n')[^1], Is.EqualTo("    });"));
    }

    [Test]
    public void NonMigrationFile_IsIgnored() =>
        Assert.That(DesignerParser.TryParse("namespace Resources { internal class Strings { } }", null), Is.Null);

    [Test]
    public void MultiLineLiteralInBlock_IsRejected()
    {
        var designer = GeneratedDesigners.Full(1).Replace(
            ".HasAnnotation(\"ProductVersion\"",
            ".HasAnnotation(\"Note\", @\"first\nsecond\").HasAnnotation(\"ProductVersion\"",
            StringComparison.Ordinal);

        Assert.That(() => DesignerParser.Parse(designer, null), Throws.InvalidOperationException.With.Message.Contains("multi-line literal"));
    }
}

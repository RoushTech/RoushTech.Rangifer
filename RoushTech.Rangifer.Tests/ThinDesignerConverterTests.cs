namespace RoushTech.Rangifer.Tests;

using NUnit.Framework;
using RoushTech.Rangifer.EntityFrameworkCore.Design;
using RoushTech.Rangifer.Tests.Models;

public class ThinDesignerConverterTests
{
    [Test]
    public void Convert_ChainsEveryDesignerAndRebuildsItsBlocks()
    {
        using var project = new TemporaryProject();
        var full = VersionedContext.Versions.ToDictionary(version => version, version => GeneratedDesigners.Full(version));
        foreach (var (version, text) in full)
        {
            project.WriteDesigner(version, text);
        }

        var summary = ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });

        var designers = DesignerSet.Scan(project.Directory);
        Assert.Multiple(() =>
        {
            Assert.That(summary.Converted, Is.EqualTo(full.Count));
            foreach (var (version, text) in full)
            {
                var thin = designers.Find(GeneratedDesigners.MigrationId(version))
                    ?? throw new AssertionException($"Version {version} has no designer after conversion.");
                Assert.That(thin.Kind, Is.EqualTo(DesignerKind.Thin));
                Assert.That(thin.ParentMigrationId, Is.EqualTo(version == 1 ? null : GeneratedDesigners.MigrationId(version - 1)));
                Assert.That(designers.ResolveBlocks(thin), Is.EqualTo(DesignerParser.Parse(text, null).Blocks));
            }
        });
    }

    [Test]
    public void Convert_IsIdempotent()
    {
        using var project = new TemporaryProject();
        foreach (var version in VersionedContext.Versions)
        {
            project.WriteDesigner(version, GeneratedDesigners.Full(version));
        }

        ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });
        var converted = VersionedContext.Versions.Select(version => File.ReadAllText(project.DesignerPath(version))).ToList();
        var second = ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });

        Assert.Multiple(() =>
        {
            Assert.That(second.Converted, Is.Zero);
            Assert.That(second.AlreadyThin, Is.EqualTo(VersionedContext.Versions.Count));
            Assert.That(VersionedContext.Versions.Select(version => File.ReadAllText(project.DesignerPath(version))), Is.EqualTo(converted));
        });
    }

    [Test]
    public void DryRun_WritesNothing()
    {
        using var project = new TemporaryProject();
        var text = GeneratedDesigners.Full(1);
        project.WriteDesigner(1, text);

        var summary = ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: true, _ => { });

        Assert.Multiple(() =>
        {
            Assert.That(summary.Converted, Is.EqualTo(1));
            Assert.That(File.ReadAllText(project.DesignerPath(1)), Is.EqualTo(text));
        });
    }

    [Test]
    public void Convert_RebasesWholeModelDesignerWrittenBeforeHistoryWasConverted()
    {
        using var project = new TemporaryProject();
        foreach (var version in new[] { 1, 2, 3 })
        {
            project.WriteDesigner(version, GeneratedDesigners.Full(version));
        }

        var designers = DesignerSet.Scan(project.Directory);
        var standalone = ThinDesigner.Create(designers, DesignerParser.Parse(GeneratedDesigners.Full(4), project.DesignerPath(4)), parent: null);
        project.WriteDesigner(4, standalone.Text);

        ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });

        var rebased = DesignerSet.Scan(project.Directory).Find(GeneratedDesigners.MigrationId(4));
        Assert.Multiple(() =>
        {
            Assert.That(rebased?.ParentMigrationId, Is.EqualTo(GeneratedDesigners.MigrationId(3)));
            Assert.That(rebased?.Text.Length, Is.LessThan(standalone.Text.Length));
        });
    }
}

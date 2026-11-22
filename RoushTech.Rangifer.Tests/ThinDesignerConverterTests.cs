namespace RoushTech.Rangifer.Tests;

using NUnit.Framework;
using RoushTech.Rangifer.EntityFrameworkCore;
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

    [Test]
    public void Convert_StoresEntitiesByLineAndEditsThemByLine()
    {
        using var project = WriteAllVersions();

        ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });

        var texts = VersionedContext.Versions.Select(version => File.ReadAllText(project.DesignerPath(version))).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(texts, Has.Some.Contains("delta.SetEntity("));
            Assert.That(texts, Has.Some.Contains("delta.Edit("));
        });
    }

    [Test]
    public void Convert_UpdatesDesignersThatStoreWholeEntityBlocks()
    {
        using var project = WriteAllVersions();
        ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { }, lineEdits: false);

        var update = ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });
        var again = ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });

        var designers = DesignerSet.Scan(project.Directory);
        Assert.Multiple(() =>
        {
            Assert.That(update.Updated, Is.EqualTo(VersionedContext.Versions.Count));
            Assert.That(update.Converted, Is.Zero);
            Assert.That(again.AlreadyThin, Is.EqualTo(VersionedContext.Versions.Count));
            foreach (var version in VersionedContext.Versions)
            {
                var thin = designers.Find(GeneratedDesigners.MigrationId(version))!;
                Assert.That(thin.Text, Does.Contain("delta.SetEntity(").Or.Contain("delta.Edit("), $"version {version}");
                Assert.That(designers.ResolveBlocks(thin), Is.EqualTo(DesignerParser.Parse(GeneratedDesigners.Full(version), null).Blocks), $"version {version}");
            }
        });
    }

    [Test]
    public void HandEditToAnEarlierDesigner_IsReportedByTheMigrationThatEditsByLine()
    {
        using var project = WriteAllVersions();
        ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });
        var designers = DesignerSet.Scan(project.Directory);
        var (editor, key) = designers.All()
            .SelectMany(designer => designer.Operations.Where(operation => operation.Kind == BlockOperationKind.Edit).Select(operation => (designer, operation.Key)))
            .First();
        var setter = designers.All()
            .Where(designer => string.CompareOrdinal(designer.MigrationId, editor.MigrationId) < 0)
            .Last(designer => designer.Operations.Any(operation => operation.Key == key && operation.Value is EntityBlock<string, string>));
        var firstLine = ((EntityBlock<string, string>)setter.Operations.Last(operation => operation.Key == key && operation.Value is not null).Value!).Members[0].Split('\n')[0];
        var path = setter.Path!;
        var text = File.ReadAllText(path);
        File.WriteAllText(path, text.Replace($"static b => {firstLine}", $"static b => {firstLine}.HasComment(\"edited\")", StringComparison.Ordinal));

        var edited = DesignerSet.Scan(project.Directory);

        Assert.That(
            () => edited.ResolveBlocks(edited.Find(editor.MigrationId)!),
            Throws.InvalidOperationException.With.Message.Contains(editor.MigrationId).And.Message.Contains("edited by hand"));
    }

    [Test]
    public void Scan_FindsDesignersByTheirMigrationAttribute()
    {
        using var project = new TemporaryProject();
        var renamed = Path.Combine(project.Directory, "Migrations", "20260101000001_RenamedLater.Designer.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(renamed)!);
        File.WriteAllText(renamed, GeneratedDesigners.Full(1));

        var summary = ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });

        Assert.Multiple(() =>
        {
            Assert.That(summary.Converted, Is.EqualTo(1));
            Assert.That(DesignerSet.Scan(project.Directory).Find(GeneratedDesigners.MigrationId(1))?.Kind, Is.EqualTo(DesignerKind.Thin));
        });
    }

    private static TemporaryProject WriteAllVersions()
    {
        var project = new TemporaryProject();
        foreach (var version in VersionedContext.Versions)
        {
            project.WriteDesigner(version, GeneratedDesigners.Full(version));
        }

        return project;
    }
}

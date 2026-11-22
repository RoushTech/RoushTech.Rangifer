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
        var (editor, _) = EditLineInEarlierDesigner(project, line => $"{line}.HasComment(\"edited\")");

        var edited = DesignerSet.Scan(project.Directory);

        Assert.That(
            () => edited.ResolveBlocks(edited.Find(editor.MigrationId)!),
            Throws.InvalidOperationException.With.Message.Contains(editor.MigrationId).And.Message.Contains("edited by hand"));
    }

    [Test]
    public void ReformattedEarlierDesigner_IsNotAnEdit()
    {
        using var project = WriteAllVersions();
        ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });
        var (editor, _) = EditLineInEarlierDesigner(project, line => line.Replace("(", " ( ", StringComparison.Ordinal));

        var reformatted = DesignerSet.Scan(project.Directory);

        Assert.That(() => reformatted.ResolveBlocks(reformatted.Find(editor.MigrationId)!), Throws.Nothing);
    }

    [Test]
    public void AcceptHandEdits_KeepsTheEditAndRewritesTheHashes()
    {
        using var project = WriteAllVersions();
        ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });
        var (editor, _) = EditLineInEarlierDesigner(project, line => $"{line}.HasComment(\"edited\")");

        var summary = ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { }, acceptHandEdits: true);

        var designers = DesignerSet.Scan(project.Directory);
        Assert.Multiple(() =>
        {
            Assert.That(summary.Updated, Is.EqualTo(VersionedContext.Versions.Count));
            Assert.That(designers.ResolveBlocks(designers.Find(editor.MigrationId)!).Select(block => block.Text), Has.Some.Contains("HasComment(\"edited\")"));
            Assert.That(ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: true, _ => { }).AlreadyThin, Is.EqualTo(VersionedContext.Versions.Count));
        });
    }

    [Test]
    public void Rc2TextHashes_AreAcceptedAndRewritten()
    {
        using var project = WriteAllVersions();
        ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });
        var designers = DesignerSet.Scan(project.Directory);
        foreach (var designer in designers.All().ToList())
        {
            var text = designer.Text;
            foreach (var operation in designer.Operations.Where(operation => operation.Edit is not null))
            {
                var parent = designers.ResolveChain(designers.Find(designer.ParentMigrationId!)!);
                var lines = ((EntityBlock<string, string>)parent.Single(block => block.Key == operation.Key).Value).Members;
                var textHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n\0", lines))), 0, 4);
                text = text.Replace($"\"{operation.Edit!.ParentHash}\"", $"\"{textHash}\"", StringComparison.Ordinal);
            }

            File.WriteAllText(designer.Path!, text);
        }

        var rc2 = DesignerSet.Scan(project.Directory);
        var resolved = VersionedContext.Versions.Select(version => rc2.ResolveBlocks(rc2.Find(GeneratedDesigners.MigrationId(version))!)).ToList();
        var update = ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Has.Count.EqualTo(VersionedContext.Versions.Count));
            Assert.That(update.Updated, Is.EqualTo(VersionedContext.Versions.Count));
            Assert.That(VersionedContext.Versions.Select(version => File.ReadAllText(project.DesignerPath(version))), Has.Some.Contains("\"2:"));
        });
    }

    private static (DesignerDocument Editor, string Key) EditLineInEarlierDesigner(TemporaryProject project, Func<string, string> edit)
    {
        var designers = DesignerSet.Scan(project.Directory);
        var (editor, key) = designers.All()
            .SelectMany(designer => designer.Operations.Where(operation => operation.Kind == BlockOperationKind.Edit).Select(operation => (designer, operation.Key)))
            .First();
        var setter = designers.All()
            .Where(designer => string.CompareOrdinal(designer.MigrationId, editor.MigrationId) < 0)
            .Last(designer => designer.Operations.Any(operation => operation.Key == key && operation.Value is EntityBlock<string, string>));
        var line = ((EntityBlock<string, string>)setter.Operations.Last(operation => operation.Key == key && operation.Value is not null).Value!).Members[0].Split('\n')[0];
        var text = File.ReadAllText(setter.Path!);
        File.WriteAllText(setter.Path!, text.Replace($"static b => {line}", $"static b => {edit(line)}", StringComparison.Ordinal));
        return (editor, key);
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

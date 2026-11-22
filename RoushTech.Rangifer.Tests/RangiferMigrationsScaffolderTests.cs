namespace RoushTech.Rangifer.Tests;

using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RoushTech.Rangifer.EntityFrameworkCore.Design;
using RoushTech.Rangifer.Tests.Models;

public class RangiferMigrationsScaffolderTests
{
    [Test]
    public void DesignTimeServices_ReplaceTheScaffolder()
    {
        using var context = new VersionedContext(1);
        using var services = GeneratedDesigners.CreateDesignTimeServices(context, new RangiferDesignTimeServices());
        using var scope = services.CreateScope();

        Assert.That(scope.ServiceProvider.GetRequiredService<IMigrationsScaffolder>(), Is.TypeOf<RangiferMigrationsScaffolder>());
    }

    [Test]
    public void Save_WritesDeltaAgainstThinParent()
    {
        using var project = new TemporaryProject();
        project.WriteDesigner(1, GeneratedDesigners.Full(1));
        ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });

        var designer = Save(project, version: 2, previousVersion: 1);

        Assert.Multiple(() =>
        {
            Assert.That(designer?.Kind, Is.EqualTo(DesignerKind.Thin));
            Assert.That(designer?.ParentMigrationId, Is.EqualTo(GeneratedDesigners.MigrationId(1)));
        });
    }

    [Test]
    public void Save_WritesWholeModelWhenParentIsNotThin()
    {
        using var project = new TemporaryProject();
        project.WriteDesigner(1, GeneratedDesigners.Full(1));

        var designer = Save(project, version: 2, previousVersion: 1);

        Assert.Multiple(() =>
        {
            Assert.That(designer?.Kind, Is.EqualTo(DesignerKind.Thin));
            Assert.That(designer?.ParentMigrationId, Is.Null);
            Assert.That(File.ReadAllText(project.DesignerPath(1)), Does.Contain("protected override void BuildTargetModel(ModelBuilder modelBuilder)\n        {"));
        });
    }

    [Test]
    public void Save_ParentsMigrationsFromParallelBranchesOnTheirCommonAncestor()
    {
        using var project = new TemporaryProject();
        project.WriteDesigner(1, GeneratedDesigners.Full(1));
        ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });

        using var otherBranch = new TemporaryProject();
        otherBranch.WriteDesigner(1, File.ReadAllText(project.DesignerPath(1)));
        Save(otherBranch, version: 3, previousVersion: 1);

        Save(project, version: 2, previousVersion: 1);
        project.WriteDesigner(3, File.ReadAllText(otherBranch.DesignerPath(3)));
        Save(project, version: 4, previousVersion: 3);

        var designers = DesignerSet.Scan(project.Directory);
        var parents = new Dictionary<int, int> { [2] = 1, [3] = 1, [4] = 3 };
        var reconvert = ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });

        Assert.Multiple(() =>
        {
            foreach (var (version, parentVersion) in parents)
            {
                var designer = designers.Find(GeneratedDesigners.MigrationId(version))
                    ?? throw new AssertionException($"Version {version} has no designer.");
                Assert.That(designer.ParentMigrationId, Is.EqualTo(GeneratedDesigners.MigrationId(parentVersion)), $"version {version}");
                Assert.That(designers.ResolveBlocks(designer), Is.EqualTo(DesignerParser.Parse(GeneratedDesigners.Full(version), null).Blocks), $"version {version}");
            }

            Assert.That(reconvert.Converted, Is.Zero);
            Assert.That(reconvert.AlreadyThin, Is.EqualTo(4));
        });
    }

    private static DesignerDocument? Save(TemporaryProject project, int version, int previousVersion)
    {
        using var context = new VersionedContext(version);
        using var services = GeneratedDesigners.CreateDesignTimeServices(context, new RangiferDesignTimeServices());
        using var scope = services.CreateScope();
        var scaffolder = scope.ServiceProvider.GetRequiredService<IMigrationsScaffolder>();

        scaffolder.Save(
            project.Directory,
            new ScaffoldedMigration(
                ".cs",
                GeneratedDesigners.MigrationId(previousVersion),
                GeneratedDesigners.MigrationClass(version),
                GeneratedDesigners.MigrationId(version),
                GeneratedDesigners.Full(version),
                "Migrations",
                "// snapshot",
                "VersionedContextModelSnapshot",
                "Migrations"),
            Path.Combine(project.Directory, "Migrations"),
            dryRun: false);

        return DesignerSet.Scan(project.Directory).Find(GeneratedDesigners.MigrationId(version));
    }
}

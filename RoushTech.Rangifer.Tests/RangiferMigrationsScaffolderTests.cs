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

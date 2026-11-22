namespace RoushTech.Rangifer.EntityFrameworkCore.Design;

using Microsoft.EntityFrameworkCore.Migrations.Design;

/// <summary>
/// Writes each new migration's designer as a model delta against its parent migration. When the parent
/// designer is not thin yet, the new designer stores its whole model; <c>rangifer ef convert</c> rebases it
/// once the earlier history is converted.
/// </summary>
public class RangiferMigrationsScaffolder(MigrationsScaffolderDependencies dependencies) : MigrationsScaffolder(dependencies)
{
    public override MigrationFiles Save(string projectDir, ScaffoldedMigration migration, string? outputDir, bool dryRun)
    {
        if (!string.Equals(migration.FileExtension, ".cs", StringComparison.OrdinalIgnoreCase)
            || DesignerParser.TryParse(migration.MetadataCode, null) is not { Kind: DesignerKind.Full } designer)
        {
            return base.Save(projectDir, migration, outputDir, dryRun);
        }

        var designers = DesignerSet.Scan(projectDir);
        var parent = migration.PreviousMigrationId is { } previousId ? designers.Find(previousId) : null;
        var thin = ThinDesigner.Create(designers, designer, parent is { Kind: DesignerKind.Thin } ? parent : null);

        return base.Save(
            projectDir,
            new ScaffoldedMigration(
                migration.FileExtension,
                migration.PreviousMigrationId,
                migration.MigrationCode,
                migration.MigrationId,
                thin.Text,
                migration.MigrationSubNamespace,
                migration.SnapshotCode,
                migration.SnapshotName,
                migration.SnapshotSubnamespace),
            outputDir,
            dryRun);
    }
}

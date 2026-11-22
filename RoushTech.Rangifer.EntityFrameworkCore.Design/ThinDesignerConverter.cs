namespace RoushTech.Rangifer.EntityFrameworkCore.Design;

using RoushTech.Rangifer.EntityFrameworkCore;

internal sealed record ConversionSummary(int Converted, int Updated, int AlreadyThin, int WithoutModel, long CharactersBefore, long CharactersAfter);

internal static class ThinDesignerConverter
{
    public static ConversionSummary Convert(DesignerSet designers, string? contextType, bool dryRun, Action<string> report, bool lineEdits = true)
    {
        var converted = 0;
        var updated = 0;
        var alreadyThin = 0;
        var withoutModel = 0;
        long before = 0;
        long after = 0;

        var contexts = designers.All()
            .Where(designer => contextType is null || designer.ContextType == contextType)
            .GroupBy(designer => designer.ContextType ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        foreach (var context in contexts)
        {
            var ordered = context.OrderBy(designer => designer.MigrationId, StringComparer.Ordinal).ToList();

            // Designers that store whole entity blocks are rewritten from the base so later ones can edit by line.
            var update = lineEdits && ordered.Any(StoresWholeEntityBlocks);
            var models = update
                ? ordered.Where(designer => designer.Kind == DesignerKind.Thin).ToDictionary(designer => designer.MigrationId, designers.ResolveBlocks)
                : null;

            DesignerDocument? parent = null;
            foreach (var designer in ordered)
            {
                if (designer.Kind == DesignerKind.NoModel)
                {
                    withoutModel++;
                    parent = null;
                    continue;
                }

                // A thin designer with no parent after earlier history was added by migrations add before the
                // history was converted, so it stores its whole model and is rebased here.
                if (!update && designer.Kind == DesignerKind.Thin && (designer.ParentMigrationId is not null || parent is null))
                {
                    alreadyThin++;
                    parent = designer;
                    continue;
                }

                var thin = ThinDesigner.Create(designers, designer, parent, models?.GetValueOrDefault(designer.MigrationId), lineEdits);
                if (!dryRun)
                {
                    DesignerSet.Write(thin);
                }

                designers.Replace(thin);
                report($"{designer.MigrationId}: {designer.Text.Length:N0} to {thin.Text.Length:N0} characters");
                before += designer.Text.Length;
                after += thin.Text.Length;
                if (designer.Kind == DesignerKind.Thin)
                {
                    updated++;
                }
                else
                {
                    converted++;
                }

                parent = thin;
            }
        }

        return new ConversionSummary(converted, updated, alreadyThin, withoutModel, before, after);
    }

    private static bool StoresWholeEntityBlocks(DesignerDocument designer) =>
        designer.Kind == DesignerKind.Thin
        && designer.Operations.Any(operation =>
            operation is { Kind: BlockOperationKind.Set, Value: OpaqueBlock<string, string> opaque }
            && EntityLines.TrySplit(operation.Key, opaque.Value) is not null);
}

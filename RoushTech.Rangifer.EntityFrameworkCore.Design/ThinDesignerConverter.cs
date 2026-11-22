namespace RoushTech.Rangifer.EntityFrameworkCore.Design;

internal sealed record ConversionSummary(int Converted, int AlreadyThin, int WithoutModel, long CharactersBefore, long CharactersAfter);

internal static class ThinDesignerConverter
{
    public static ConversionSummary Convert(DesignerSet designers, string? contextType, bool dryRun, Action<string> report)
    {
        var converted = 0;
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
            DesignerDocument? parent = null;
            foreach (var designer in context.OrderBy(designer => designer.MigrationId, StringComparer.Ordinal))
            {
                if (designer.Kind == DesignerKind.NoModel)
                {
                    withoutModel++;
                    parent = null;
                    continue;
                }

                // A thin designer with no parent after earlier history was added by migrations add before the
                // history was converted, so it stores its whole model and is rebased here.
                if (designer.Kind == DesignerKind.Thin && (designer.ParentMigrationId is not null || parent is null))
                {
                    alreadyThin++;
                    parent = designer;
                    continue;
                }

                var thin = ThinDesigner.Create(designers, designer, parent);
                if (!dryRun)
                {
                    DesignerSet.Write(thin);
                }

                designers.Replace(thin);
                report($"{designer.MigrationId}: {designer.Text.Length:N0} to {thin.Text.Length:N0} characters");
                before += designer.Text.Length;
                after += thin.Text.Length;
                converted++;
                parent = thin;
            }
        }

        return new ConversionSummary(converted, alreadyThin, withoutModel, before, after);
    }
}

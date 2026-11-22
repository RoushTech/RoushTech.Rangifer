namespace RoushTech.Rangifer.EntityFrameworkCore.Design;

internal static class ThinDesigner
{
    public static DesignerDocument Create(
        DesignerSet designers,
        DesignerDocument designer,
        DesignerDocument? parent,
        IReadOnlyList<ModelBlock>? blocks = null,
        bool lineEdits = true)
    {
        blocks ??= designers.ResolveBlocks(designer);
        var parentBlocks = parent is null ? null : designers.ResolveChain(parent);
        var operations = DeltaPlanner.Plan(parentBlocks, blocks, lineEdits);
        var thin = DesignerParser.Parse(ThinDesignerWriter.Render(designer, operations, parent), designer.Path);

        if (thin.Kind != DesignerKind.Thin
            || thin.MigrationId != designer.MigrationId
            || thin.Namespace != designer.Namespace
            || thin.ClassName != designer.ClassName
            || thin.ContextType != designer.ContextType
            || thin.ParentMigrationId != parent?.MigrationId
            || !DeltaPlanner.Matches(designers.ResolveChain(thin), blocks))
        {
            throw new InvalidOperationException(
                $"Rangifer's thin designer for '{designer.MigrationId}' does not rebuild the same model as the original, so it was not written.");
        }

        return thin;
    }
}

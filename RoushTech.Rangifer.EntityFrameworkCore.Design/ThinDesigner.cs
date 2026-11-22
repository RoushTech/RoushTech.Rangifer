namespace RoushTech.Rangifer.EntityFrameworkCore.Design;

internal static class ThinDesigner
{
    public static DesignerDocument Create(DesignerSet designers, DesignerDocument designer, DesignerDocument? parent)
    {
        var blocks = designers.ResolveBlocks(designer);
        var parentBlocks = parent is null ? null : designers.ResolveBlocks(parent);
        var operations = DeltaPlanner.Plan(parentBlocks, blocks);
        var thin = DesignerParser.Parse(ThinDesignerWriter.Render(designer, operations, parent), designer.Path);

        if (thin.Kind != DesignerKind.Thin
            || thin.MigrationId != designer.MigrationId
            || thin.Namespace != designer.Namespace
            || thin.ClassName != designer.ClassName
            || thin.ContextType != designer.ContextType
            || thin.ParentMigrationId != parent?.MigrationId
            || !DeltaPlanner.Matches(
                designers.ResolveBlocks(thin).Select(block => KeyValuePair.Create(block.Key, block.Text)).ToList(),
                blocks))
        {
            throw new InvalidOperationException(
                $"Rangifer's thin designer for '{designer.MigrationId}' does not rebuild the same model as the original, so it was not written.");
        }

        return thin;
    }
}

namespace RoushTech.Rangifer.EntityFrameworkCore.Design;

using Microsoft.CodeAnalysis.Text;
using RoushTech.Rangifer.EntityFrameworkCore;

internal enum DesignerKind
{
    Full,
    Thin,
    NoModel,
}

internal sealed record ModelBlock(string Key, string Text);

internal sealed record DesignerDocument
{
    public required string Text { get; init; }

    public required string? Path { get; init; }

    public required string MigrationId { get; init; }

    public required string? Namespace { get; init; }

    public required string ClassName { get; init; }

    public required string? ContextType { get; init; }

    public required DesignerKind Kind { get; init; }

    public required string NewLine { get; init; }

    public string? ParentMigrationId { get; init; }

    public IReadOnlyList<ModelBlock> Blocks { get; init; } = [];

    public IReadOnlyList<TextOperation> Operations { get; init; } = [];

    public TextSpan? ModelMembersSpan { get; init; }

    public string MemberIndent { get; init; } = string.Empty;

    public string QualifiedClassName => Namespace is null ? ClassName : $"{Namespace}.{ClassName}";
}

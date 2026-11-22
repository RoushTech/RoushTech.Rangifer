global using TextBlock = RoushTech.Rangifer.EntityFrameworkCore.Block<string, string>;
global using TextOperation = RoushTech.Rangifer.EntityFrameworkCore.BlockOperation<string, string>;

namespace RoushTech.Rangifer.EntityFrameworkCore.Design;

using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoushTech.Rangifer.EntityFrameworkCore;

internal static class EntityLines
{
    public const string BuilderName = "b";
    private const string LineIndent = "        ";

    public static TextBlock FromText(string key, string text) =>
        (TextBlock?)TrySplit(key, text) ?? new OpaqueBlock<string, string>(text);

    // Only blocks that rebuild to exactly the text EF generated are stored by line; anything else stays whole.
    public static EntityBlock<string, string>? TrySplit(string key, string text)
    {
        if (EntityKey.TryEntityName(key) is not { } name)
        {
            return null;
        }

        var statement = SyntaxFactory.ParseStatement(text);
        if (statement.ContainsDiagnostics
            || statement.FullSpan.Length != text.Length
            || DesignerParser.ReadEntityStatement(statement) is not { } entity
            || entity.Name != name
            || entity.Configuration is not SimpleLambdaExpressionSyntax { Parameter.Identifier.ValueText: BuilderName, Block.Statements: { Count: > 0 } statements }
            || statements.Any(member => member is not ExpressionStatementSyntax))
        {
            return null;
        }

        try
        {
            var block = new EntityBlock<string, string>(name, statements.Select(member => DesignerParser.Normalize(member, null)).ToList());
            return Format(block) == text ? block : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static string Text(TextBlock block) => block switch
    {
        OpaqueBlock<string, string> opaque => opaque.Value,
        EntityBlock<string, string> entity => Format(entity),
        _ => throw new InvalidOperationException($"Unknown model block {block}."),
    };

    public static string Hash(IReadOnlyList<string> members) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n\0", members))), 0, 4);

    private static string Format(EntityBlock<string, string> block)
    {
        var builder = new StringBuilder();
        builder.Append("modelBuilder.Entity(").Append(SymbolDisplay.FormatLiteral(block.EntityName, quote: true)).Append($", {BuilderName} =>\n    {{\n");
        for (var index = 0; index < block.Members.Count; index++)
        {
            if (index > 0)
            {
                builder.Append("\n\n");
            }

            builder.AppendJoin('\n', block.Members[index].Split('\n').Select(line => line.Length == 0 ? line : LineIndent + line));
        }

        return builder.Append("\n    });").ToString();
    }
}

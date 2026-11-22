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
    private const string HashVersion = "2:";

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

    // Hashes the C# tokens, not the text, so reformatting a designer never reads as an edit.
    public static string Hash(IReadOnlyList<string> members) =>
        HashVersion + Digest(members.Select(member => string.Join('\u0001', SyntaxFactory.ParseStatement(member).DescendantTokens().Select(token => token.Text))));

    // 0.1.0-rc.2 hashed the text and wrote no version prefix.
    public static bool HashMatches(IReadOnlyList<string> members, string hash) =>
        hash.StartsWith(HashVersion, StringComparison.Ordinal) ? Hash(members) == hash : Digest(members) == hash;

    public static bool IsCurrentHash(string hash) => hash.StartsWith(HashVersion, StringComparison.Ordinal);

    private static string Digest(IEnumerable<string> members) =>
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

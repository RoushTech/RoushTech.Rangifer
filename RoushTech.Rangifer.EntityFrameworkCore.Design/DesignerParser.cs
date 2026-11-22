namespace RoushTech.Rangifer.EntityFrameworkCore.Design;

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoushTech.Rangifer.EntityFrameworkCore;

internal static class DesignerParser
{
    public const string DeltaPropertyName = "RangiferDelta";
    public const string DeltaFactoryName = "CreateRangiferDelta";
    public const string ModelBlockKey = "model";
    private const string ModelBuilderName = "modelBuilder";
    private const string DeltaVariableName = "delta";

    public static DesignerDocument? TryParse(string text, string? path)
    {
        var root = CSharpSyntaxTree.ParseText(text, path: path ?? string.Empty).GetCompilationUnitRoot();
        var migrations = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Select(declaration => (Declaration: declaration, Id: ReadStringAttribute(declaration, "Migration")))
            .Where(candidate => candidate.Id is not null)
            .ToList();

        if (migrations.Count == 0)
        {
            return null;
        }

        if (migrations.Count > 1)
        {
            throw Error(path, "declares more than one migration class.");
        }

        var (declaration, migrationId) = migrations[0];
        var header = new DesignerDocument
        {
            Text = text,
            Path = path,
            MigrationId = migrationId ?? string.Empty,
            Namespace = declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString(),
            ClassName = declaration.Identifier.ValueText,
            ContextType = ReadTypeOfAttribute(declaration, "DbContext"),
            Kind = DesignerKind.NoModel,
            NewLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n",
        };

        if (FindMethod(declaration, DeltaFactoryName) is { } factory)
        {
            return ParseThin(header, declaration, factory);
        }

        if (FindMethod(declaration, "BuildTargetModel") is { Body: { } body } buildTargetModel)
        {
            return header with
            {
                Kind = DesignerKind.Full,
                Blocks = ReadBlocks(body.Statements, path),
                ModelMembersSpan = buildTargetModel.FullSpan,
                MemberIndent = IndentOf(buildTargetModel),
            };
        }

        return header;
    }

    public static DesignerDocument Parse(string text, string? path) =>
        TryParse(text, path) ?? throw Error(path, "does not declare a migration class.");

    private static DesignerDocument ParseThin(DesignerDocument header, ClassDeclarationSyntax declaration, MethodDeclarationSyntax factory)
    {
        var statements = factory.Body?.Statements ?? throw Error(header.Path, $"has no body in {DeltaFactoryName}.");
        if (statements.Count < 2
            || statements[0] is not LocalDeclarationStatementSyntax
            {
                Declaration.Variables: [{ Initializer.Value: BaseObjectCreationExpressionSyntax { ArgumentList: { } constructorArguments } }]
            }
            || statements[^1] is not ReturnStatementSyntax)
        {
            throw Error(header.Path, $"has an unrecognized {DeltaFactoryName} body.");
        }

        var arguments = constructorArguments.Arguments;
        if (arguments.Count is not (1 or 3) || ReadString(arguments[0], header.Path) != header.MigrationId)
        {
            throw Error(header.Path, "has a model delta whose migration id does not match its [Migration] attribute.");
        }

        var parentMigrationId = arguments.Count == 3 ? ReadString(arguments[1], header.Path) : null;
        var operations = new List<TextOperation>();
        foreach (var statement in statements.Skip(1).Take(statements.Count - 2))
        {
            operations.Add(ReadOperation(statement, header.Path));
        }

        var generatedMembers = declaration.Members
            .Where(member => member switch
            {
                PropertyDeclarationSyntax property => property.Identifier.ValueText == DeltaPropertyName,
                MethodDeclarationSyntax method => method.Identifier.ValueText is "BuildTargetModel" or DeltaFactoryName,
                _ => false,
            })
            .ToList();
        var span = TextSpan.FromBounds(generatedMembers.Min(member => member.FullSpan.Start), generatedMembers.Max(member => member.FullSpan.End));
        if (declaration.Members.Any(member => !generatedMembers.Contains(member) && member.FullSpan.OverlapsWith(span)))
        {
            throw Error(header.Path, "has other members between the members Rangifer generates.");
        }

        return header with
        {
            Kind = DesignerKind.Thin,
            ParentMigrationId = parentMigrationId,
            Operations = operations,
            ModelMembersSpan = span,
            MemberIndent = IndentOf(generatedMembers.OrderBy(member => member.SpanStart).First()),
        };
    }

    private static TextOperation ReadOperation(StatementSyntax statement, string? path)
    {
        var calls = new Stack<(string Name, SeparatedSyntaxList<ArgumentSyntax> Arguments)>();
        var expression = (statement as ExpressionStatementSyntax)?.Expression;
        while (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } invocation)
        {
            calls.Push((member.Name.Identifier.ValueText, invocation.ArgumentList.Arguments));
            if (member.Expression is IdentifierNameSyntax { Identifier.ValueText: DeltaVariableName })
            {
                expression = null;
                break;
            }

            expression = member.Expression;
        }

        if (expression is not null || calls.Count == 0)
        {
            throw Error(path, $"has an unrecognized statement in {DeltaFactoryName}: {statement}");
        }

        var (operation, arguments) = calls.Pop();
        if (operation == "Edit" && arguments.Count == 3)
        {
            var lines = calls.Select(call => ReadLineOperation(call.Name, call.Arguments, statement, path)).ToList();
            return TextOperation.EditMembers(
                RequireString(arguments[0], path),
                new MemberEdit<string>(ReadInt(arguments[1], path), RequireString(arguments[2], path), lines));
        }

        if (calls.Count > 0)
        {
            throw Error(path, $"has an unrecognized model delta call: {statement}");
        }

        return (operation, arguments.Count) switch
        {
            ("Remove", 1) => TextOperation.Remove(RequireString(arguments[0], path)),
            ("Move", 2) => TextOperation.Move(RequireString(arguments[0], path), ReadString(arguments[1], path)),
            ("Set", 3) when arguments[2].Expression is LambdaExpressionSyntax { Body: BlockSyntax body } =>
                TextOperation.Set(
                    RequireString(arguments[0], path),
                    ReadString(arguments[1], path),
                    new OpaqueBlock<string, string>(Normalize(body.Statements, path))),
            ("SetEntity", > 2) => ReadSetEntity(arguments, path),
            _ => throw Error(path, $"has an unrecognized model delta call: {statement}"),
        };
    }

    private static TextOperation ReadSetEntity(SeparatedSyntaxList<ArgumentSyntax> arguments, string? path)
    {
        var key = RequireString(arguments[0], path);
        var name = EntityKey.TryEntityName(key) ?? throw Error(path, $"sets '{key}' by line, but it is not an entity type block.");
        return TextOperation.Set(
            key,
            ReadString(arguments[1], path),
            new EntityBlock<string, string>(name, arguments.Skip(2).Select(argument => ReadLine(argument, path)).ToList()));
    }

    private static MemberOperation<string> ReadLineOperation(string name, SeparatedSyntaxList<ArgumentSyntax> arguments, StatementSyntax statement, string? path) =>
        (name, arguments.Count) switch
        {
            ("Replace", 2) => new(MemberOperationKind.Replace, ReadInt(arguments[0], path), ReadLine(arguments[1], path)),
            ("Insert", 2) => new(MemberOperationKind.Insert, ReadInt(arguments[0], path), ReadLine(arguments[1], path)),
            ("Remove", 1) => new(MemberOperationKind.Remove, ReadInt(arguments[0], path), null),
            _ => throw Error(path, $"has an unrecognized line edit '{name}' in: {statement}"),
        };

    // A line is written as "static b => <EF statement>", with continuation lines indented from the start of the
    // line the lambda begins on, so it reads back as the statement EF generated.
    private static string ReadLine(ArgumentSyntax argument, string? path)
    {
        if (argument.Expression is not SimpleLambdaExpressionSyntax
            {
                Parameter.Identifier.ValueText: EntityLines.BuilderName,
                ExpressionBody: { } body,
            } lambda)
        {
            throw Error(path, $"expected a 'static {EntityLines.BuilderName} => ...' line but found '{argument}'.");
        }

        if (body.DescendantTokens().Any(token => token.Text.Contains('\n', StringComparison.Ordinal)))
        {
            throw Error(path, $"contains a multi-line literal near line {Line(body)}, which cannot be re-indented safely.");
        }

        var startLine = lambda.SyntaxTree.GetText().Lines[lambda.GetLocation().GetLineSpan().StartLinePosition.Line].ToString();
        var column = startLine.Length - startLine.TrimStart().Length;
        var lines = body.ToString().ReplaceLineEndings("\n").Split('\n');
        var builder = new StringBuilder(lines[0]);
        foreach (var line in lines.Skip(1))
        {
            builder.Append('\n');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.Length < column || !line.AsSpan(0, column).IsWhiteSpace())
            {
                throw Error(path, $"has a line indented less than its model delta call near line {Line(body)}.");
            }

            builder.Append(line, column, line.Length - column);
        }

        return builder.Append(';').ToString();
    }

    private static int ReadInt(ArgumentSyntax argument, string? path) =>
        argument.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NumericLiteralExpression) && literal.Token.Value is int value
            ? value
            : throw Error(path, $"expected an integer literal but found '{argument}'.");

    private static List<ModelBlock> ReadBlocks(SyntaxList<StatementSyntax> statements, string? path)
    {
        var blocks = new List<ModelBlock>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var definedEntityTypes = new HashSet<string>(StringComparer.Ordinal);
        var modelStatements = new List<StatementSyntax>();
        var otherStatements = 0;
        var inEntityTypes = false;

        foreach (var statement in statements)
        {
            if (ReadEntityStatement(statement) is { } entity)
            {
                if (!inEntityTypes)
                {
                    AddModelBlock();
                    inEntityTypes = true;
                }

                // EF emits every entity type once to define it, then again for relationships, then for navigations.
                var pass = definedEntityTypes.Add(entity.Name) ? 1 : ConfiguresOnlyNavigations(entity.Configuration) ? 3 : 2;
                Add($"{pass}:{entity.Name}", [statement]);
            }
            else if (inEntityTypes)
            {
                Add($"other:{++otherStatements}", [statement]);
            }
            else
            {
                modelStatements.Add(statement);
            }
        }

        if (!inEntityTypes)
        {
            AddModelBlock();
        }

        return blocks;

        void AddModelBlock()
        {
            if (modelStatements.Count > 0)
            {
                Add(ModelBlockKey, modelStatements);
            }
        }

        void Add(string key, IReadOnlyList<StatementSyntax> blockStatements)
        {
            var uniqueKey = key;
            for (var occurrence = 2; !keys.Add(uniqueKey); occurrence++)
            {
                uniqueKey = $"{key}#{occurrence}";
            }

            blocks.Add(new ModelBlock(uniqueKey, Normalize(blockStatements, path)));
        }
    }

    public static (string Name, LambdaExpressionSyntax Configuration)? ReadEntityStatement(StatementSyntax statement) =>
        statement is ExpressionStatementSyntax
        {
            Expression: InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.ValueText: ModelBuilderName },
                    Name.Identifier.ValueText: "Entity",
                },
                ArgumentList.Arguments:
                    [
                    { Expression: LiteralExpressionSyntax name },
                    { Expression: LambdaExpressionSyntax lambda },
                    ],
            },
        }
            && name.IsKind(SyntaxKind.StringLiteralExpression)
            ? (name.Token.ValueText, lambda)
            : null;

    private static bool ConfiguresOnlyNavigations(LambdaExpressionSyntax configuration)
    {
        var parameter = configuration switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: [var first, ..] } => first.Identifier.ValueText,
            _ => null,
        };

        return parameter is not null
            && configuration.Body is BlockSyntax { Statements.Count: > 0 } body
            && body.Statements.All(statement =>
                statement is ExpressionStatementSyntax expression && RootCallName(expression.Expression, parameter) == "Navigation");
    }

    private static string? RootCallName(ExpressionSyntax expression, string receiver)
    {
        while (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member })
        {
            if (member.Expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == receiver)
            {
                return member.Name.Identifier.ValueText;
            }

            expression = member.Expression;
        }

        return null;
    }

    private static string Normalize(IEnumerable<StatementSyntax> statements, string? path) =>
        string.Join('\n', statements.Select(statement => Normalize(statement, path)));

    // Blocks are stored without their surrounding indentation so a block keeps the same text whether it
    // sits in EF's BuildTargetModel or inside a model delta lambda.
    public static string Normalize(StatementSyntax statement, string? path)
    {
        if (statement.DescendantTokens().Any(token => token.Text.Contains('\n', StringComparison.Ordinal)))
        {
            throw Error(path, $"contains a multi-line literal near line {Line(statement)}, which cannot be re-indented safely.");
        }

        var column = statement.GetLocation().GetLineSpan().StartLinePosition.Character;
        var lines = statement.ToString().ReplaceLineEndings("\n").Split('\n');
        var builder = new StringBuilder(lines[0]);
        foreach (var line in lines.Skip(1))
        {
            builder.Append('\n');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.Length < column || !line.AsSpan(0, column).IsWhiteSpace())
            {
                throw Error(path, $"has a line indented less than its statement near line {Line(statement)}.");
            }

            builder.Append(line, column, line.Length - column);
        }

        return builder.ToString();
    }

    private static MethodDeclarationSyntax? FindMethod(ClassDeclarationSyntax declaration, string name) =>
        declaration.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(method => method.Identifier.ValueText == name);

    private static string? ReadStringAttribute(ClassDeclarationSyntax declaration, string name) =>
        FindAttribute(declaration, name)?.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal
        && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;

    private static string? ReadTypeOfAttribute(ClassDeclarationSyntax declaration, string name) =>
        FindAttribute(declaration, name)?.ArgumentList?.Arguments.FirstOrDefault()?.Expression is TypeOfExpressionSyntax typeOf
            ? typeOf.Type.ToString()
            : null;

    private static AttributeSyntax? FindAttribute(ClassDeclarationSyntax declaration, string name) =>
        declaration.AttributeLists
            .SelectMany(list => list.Attributes)
            .FirstOrDefault(attribute =>
            {
                var attributeName = attribute.Name is QualifiedNameSyntax qualified ? qualified.Right.Identifier.ValueText : attribute.Name.ToString();
                return attributeName == name || attributeName == name + "Attribute";
            });

    private static string? ReadString(ArgumentSyntax argument, string? path) => argument.Expression switch
    {
        LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
        LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NullLiteralExpression) => null,
        _ => throw Error(path, $"expected a string literal but found '{argument}'."),
    };

    private static string RequireString(ArgumentSyntax argument, string? path) =>
        ReadString(argument, path) ?? throw Error(path, $"expected a model block key but found '{argument}'.");

    private static string IndentOf(SyntaxNode member) => new(' ', member.GetLocation().GetLineSpan().StartLinePosition.Character);

    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static InvalidOperationException Error(string? path, string message) =>
        new($"Migration designer {(string.IsNullOrEmpty(path) ? "(generated)" : $"'{path}'")} {message}");
}

namespace RoushTech.Rangifer.EntityFrameworkCore.Design;

using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using RoushTech.Rangifer.EntityFrameworkCore;

internal static class ThinDesignerWriter
{
    private const string ModelDeltaType = "global::RoushTech.Rangifer.EntityFrameworkCore.ModelDelta";
    private const string Indent = "    ";

    public static string Render(DesignerDocument designer, IReadOnlyList<TextOperation> operations, DesignerDocument? parent)
    {
        if (designer.ModelMembersSpan is not { } span)
        {
            throw new InvalidOperationException($"Migration '{designer.MigrationId}' has no target model to rewrite.");
        }

        var writer = new Writer(designer.NewLine);
        var member = designer.MemberIndent;
        var body = member + Indent;

        writer.Raw(designer.Text[..span.Start]);
        writer.Line(member, $"internal static {ModelDeltaType} {DesignerParser.DeltaPropertyName} {{ get; }} = {DesignerParser.DeltaFactoryName}();");
        writer.Blank();
        writer.Line(member, "/// <inheritdoc />");
        writer.Line(member, "protected override void BuildTargetModel(ModelBuilder modelBuilder)");
        writer.Line(body, $"=> {DesignerParser.DeltaPropertyName}.Apply(modelBuilder);");
        writer.Blank();
        writer.Line(member, $"private static {ModelDeltaType} {DesignerParser.DeltaFactoryName}()");
        writer.Line(member, "{");
        writer.Line(string.Empty, "#pragma warning disable 612, 618");
        writer.Line(body, parent is null
            ? $"var delta = new {ModelDeltaType}({Literal(designer.MigrationId)});"
            : $"var delta = new {ModelDeltaType}({Literal(designer.MigrationId)}, {Literal(parent.MigrationId)}, global::{parent.QualifiedClassName}.{DesignerParser.DeltaPropertyName});");

        foreach (var operation in operations)
        {
            switch (operation.Kind)
            {
                case BlockOperationKind.Remove:
                    writer.Line(body, $"delta.Remove({Literal(operation.Key)});");
                    break;
                case BlockOperationKind.Move:
                    writer.Line(body, $"delta.Move({Literal(operation.Key)}, {Literal(operation.After)});");
                    break;
                case BlockOperationKind.Set when operation.Value is EntityBlock<string, string> entity:
                    writer.Blank();
                    writer.Line(body, $"delta.SetEntity({Literal(operation.Key)}, {Literal(operation.After)},");
                    for (var index = 0; index < entity.Members.Count; index++)
                    {
                        WriteLine(writer, body + Indent, "static b => ", entity.Members[index], index == entity.Members.Count - 1 ? ");" : ",");
                    }

                    break;
                case BlockOperationKind.Set when operation.Value is OpaqueBlock<string, string> opaque:
                    writer.Blank();
                    writer.Line(body, $"delta.Set({Literal(operation.Key)}, {Literal(operation.After)}, static modelBuilder =>");
                    writer.Line(body, "{");
                    foreach (var line in opaque.Value.Split('\n'))
                    {
                        writer.Line(line.Length == 0 ? string.Empty : body + Indent, line);
                    }

                    writer.Line(body, "});");
                    break;
                case BlockOperationKind.Edit when operation.Edit is { } edit:
                    writer.Blank();
                    writer.Line(body, $"delta.Edit({Literal(operation.Key)}, {edit.ParentCount}, {Literal(edit.ParentHash)})");
                    for (var index = 0; index < edit.Operations.Count; index++)
                    {
                        var line = edit.Operations[index];
                        var end = index == edit.Operations.Count - 1 ? ");" : ")";
                        if (line.Value is { } value)
                        {
                            WriteLine(writer, body + Indent, $".{line.Kind}({line.Index}, static b => ", value, end);
                        }
                        else
                        {
                            writer.Line(body + Indent, $".{line.Kind}({line.Index}{end}");
                        }
                    }

                    break;
                default:
                    throw new InvalidOperationException($"Unknown model block operation '{operation.Kind}'.");
            }
        }

        writer.Blank();
        writer.Line(body, "return delta;");
        writer.Line(string.Empty, "#pragma warning restore 612, 618");
        writer.Line(member, "}");
        writer.Raw(designer.Text[span.End..]);
        return writer.ToString();
    }

    private static void WriteLine(Writer writer, string indent, string prefix, string statement, string end)
    {
        var lines = statement[..^1].Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var text = (index == 0 ? prefix : string.Empty) + lines[index] + (index == lines.Length - 1 ? end : string.Empty);
            writer.Line(lines[index].Length == 0 && index > 0 ? string.Empty : indent, text);
        }
    }

    private static string Literal(string? value) => value is null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true);

    private sealed class Writer(string newLine)
    {
        private readonly StringBuilder builder = new();

        public void Raw(string text) => builder.Append(text);

        public void Line(string indent, string text) => builder.Append(indent).Append(text).Append(newLine);

        public void Blank() => builder.Append(newLine);

        public override string ToString() => builder.ToString();
    }
}

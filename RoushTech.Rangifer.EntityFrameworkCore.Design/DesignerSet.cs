namespace RoushTech.Rangifer.EntityFrameworkCore.Design;

using System.Text;
using System.Text.RegularExpressions;
using RoushTech.Rangifer.EntityFrameworkCore;

internal sealed partial class DesignerSet
{
    private const string DesignerSuffix = ".Designer.cs";
    private static readonly string[] IgnoredDirectories = ["bin", "obj", "node_modules", ".git"];

    private readonly Dictionary<string, (string Path, string Text)> files;
    private readonly Dictionary<string, DesignerDocument?> documents = new(StringComparer.Ordinal);

    private DesignerSet(Dictionary<string, (string Path, string Text)> files)
    {
        this.files = files;
    }

    // EF identifies a migration by its [Migration] attribute, not its file name, and renamed files keep the old id.
    public static DesignerSet Scan(string projectDirectory)
    {
        var files = new Dictionary<string, (string Path, string Text)>(StringComparer.Ordinal);
        var root = Path.GetFullPath(projectDirectory);
        foreach (var path in Directory.EnumerateFiles(root, "*" + DesignerSuffix, SearchOption.AllDirectories))
        {
            var relativeDirectories = Path.GetRelativePath(root, Path.GetDirectoryName(path) ?? root)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (relativeDirectories.Any(directory => IgnoredDirectories.Contains(directory, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            if (MigrationAttributePattern().Match(text) is not { Success: true } match)
            {
                continue;
            }

            var migrationId = match.Groups["id"].Value;
            if (!files.TryAdd(migrationId, (path, text)))
            {
                throw new InvalidOperationException($"Migration '{migrationId}' has more than one designer: '{files[migrationId].Path}' and '{path}'.");
            }
        }

        return new DesignerSet(files);
    }

    public IEnumerable<DesignerDocument> All() =>
        files.Keys.Order(StringComparer.Ordinal).Select(Find).OfType<DesignerDocument>();

    public DesignerDocument? Find(string migrationId)
    {
        if (documents.TryGetValue(migrationId, out var cached))
        {
            return cached;
        }

        DesignerDocument? document = null;
        if (files.TryGetValue(migrationId, out var file))
        {
            document = DesignerParser.TryParse(file.Text, file.Path);
            if (document is not null && document.MigrationId != migrationId)
            {
                throw new InvalidOperationException($"'{file.Path}' declares migration '{document.MigrationId}', but was found as '{migrationId}'.");
            }
        }

        documents[migrationId] = document;
        return document;
    }

    public void Replace(DesignerDocument document) => documents[document.MigrationId] = document;

    public IReadOnlyList<ModelBlock> ResolveBlocks(DesignerDocument designer, bool acceptHandEdits = false) =>
        ResolveChain(designer, acceptHandEdits).Select(block => new ModelBlock(block.Key, EntityLines.Text(block.Value))).ToList();

    public IReadOnlyList<KeyValuePair<string, TextBlock>> ResolveChain(DesignerDocument designer, bool acceptHandEdits = false)
    {
        if (designer.Kind == DesignerKind.Full)
        {
            return Opaque(designer.Blocks);
        }

        var chain = new Stack<DesignerDocument>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = designer;
        while (true)
        {
            if (!visited.Add(current.MigrationId))
            {
                throw new InvalidOperationException($"The model delta chain of '{designer.MigrationId}' loops back to '{current.MigrationId}'.");
            }

            if (current.Kind == DesignerKind.NoModel)
            {
                throw new InvalidOperationException($"Migration '{current.MigrationId}' has no target model, so '{designer.MigrationId}' cannot build on it.");
            }

            if (current.Kind == DesignerKind.Full)
            {
                break;
            }

            chain.Push(current);
            if (current.ParentMigrationId is not { } parentId)
            {
                break;
            }

            current = Find(parentId)
                ?? throw new InvalidOperationException($"Migration '{current.MigrationId}' builds on '{parentId}', whose designer was not found.");
        }

        var sequence = new BlockSequence<string, string>(
            current.Kind == DesignerKind.Full ? Opaque(current.Blocks) : [],
            acceptHandEdits ? null : EntityLines.HashMatches);
        foreach (var link in chain)
        {
            foreach (var operation in link.Operations)
            {
                try
                {
                    sequence.Apply(operation);
                }
                catch (InvalidOperationException exception)
                {
                    throw new InvalidOperationException($"Migration '{link.MigrationId}' does not apply on top of its parent: {exception.Message}", exception);
                }
            }
        }

        return sequence.Blocks;
    }

    public static void Write(DesignerDocument document)
    {
        if (document.Path is not { } path)
        {
            throw new InvalidOperationException($"Migration '{document.MigrationId}' has no designer path to write to.");
        }

        File.WriteAllText(path, document.Text, new UTF8Encoding(HasUtf8Bom(path)));
    }

    private static List<KeyValuePair<string, TextBlock>> Opaque(IEnumerable<ModelBlock> blocks) =>
        blocks.Select(block => KeyValuePair.Create(block.Key, (TextBlock)new OpaqueBlock<string, string>(block.Text))).ToList();

    private static bool HasUtf8Bom(string path)
    {
        Span<byte> preamble = stackalloc byte[3];
        using var stream = File.OpenRead(path);
        return stream.Read(preamble) == 3 && preamble.SequenceEqual(Encoding.UTF8.Preamble);
    }

    [GeneratedRegex(@"\[\s*(?:global::)?(?:Microsoft\.EntityFrameworkCore\.Migrations\.)?Migration(?:Attribute)?\s*\(\s*""(?<id>(?:[^""\\]|\\.)*)""\s*\)\s*\]")]
    private static partial Regex MigrationAttributePattern();
}

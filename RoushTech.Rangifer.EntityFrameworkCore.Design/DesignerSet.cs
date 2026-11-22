namespace RoushTech.Rangifer.EntityFrameworkCore.Design;

using System.Text;
using System.Text.RegularExpressions;

internal sealed partial class DesignerSet
{
    private const string DesignerSuffix = ".Designer.cs";
    private static readonly string[] IgnoredDirectories = ["bin", "obj", "node_modules", ".git"];

    private readonly Dictionary<string, string> paths;
    private readonly Dictionary<string, DesignerDocument?> documents = new(StringComparer.Ordinal);

    private DesignerSet(Dictionary<string, string> paths)
    {
        this.paths = paths;
    }

    public static DesignerSet Scan(string projectDirectory)
    {
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        var root = Path.GetFullPath(projectDirectory);
        foreach (var path in Directory.EnumerateFiles(root, "*" + DesignerSuffix, SearchOption.AllDirectories))
        {
            var relativeDirectories = Path.GetRelativePath(root, Path.GetDirectoryName(path) ?? root)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (relativeDirectories.Any(directory => IgnoredDirectories.Contains(directory, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            var migrationId = Path.GetFileName(path)[..^DesignerSuffix.Length];
            if (MigrationIdPattern().IsMatch(migrationId) && !paths.TryAdd(migrationId, path))
            {
                throw new InvalidOperationException($"Migration '{migrationId}' has more than one designer: '{paths[migrationId]}' and '{path}'.");
            }
        }

        return new DesignerSet(paths);
    }

    public IEnumerable<DesignerDocument> All() =>
        paths.Keys.Order(StringComparer.Ordinal).Select(Find).OfType<DesignerDocument>();

    public DesignerDocument? Find(string migrationId)
    {
        if (documents.TryGetValue(migrationId, out var cached))
        {
            return cached;
        }

        DesignerDocument? document = null;
        if (paths.TryGetValue(migrationId, out var path))
        {
            document = DesignerParser.TryParse(File.ReadAllText(path), path);
            if (document is not null && document.MigrationId != migrationId)
            {
                throw new InvalidOperationException($"'{path}' declares migration '{document.MigrationId}', which does not match its file name.");
            }
        }

        documents[migrationId] = document;
        return document;
    }

    public void Replace(DesignerDocument document) => documents[document.MigrationId] = document;

    public IReadOnlyList<ModelBlock> ResolveBlocks(DesignerDocument designer)
    {
        if (designer.Kind == DesignerKind.Full)
        {
            return designer.Blocks;
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

        var blocks = current.Kind == DesignerKind.Full ? current.Blocks : [];
        var operations = chain.SelectMany(link => link.Operations);
        return DeltaPlanner.Replay(blocks, operations).Blocks.Select(block => new ModelBlock(block.Key, block.Value)).ToList();
    }

    public static void Write(DesignerDocument document)
    {
        if (document.Path is not { } path)
        {
            throw new InvalidOperationException($"Migration '{document.MigrationId}' has no designer path to write to.");
        }

        File.WriteAllText(path, document.Text, new UTF8Encoding(HasUtf8Bom(path)));
    }

    private static bool HasUtf8Bom(string path)
    {
        Span<byte> preamble = stackalloc byte[3];
        using var stream = File.OpenRead(path);
        return stream.Read(preamble) == 3 && preamble.SequenceEqual(Encoding.UTF8.Preamble);
    }

    [GeneratedRegex(@"^\d{14}_")]
    private static partial Regex MigrationIdPattern();
}

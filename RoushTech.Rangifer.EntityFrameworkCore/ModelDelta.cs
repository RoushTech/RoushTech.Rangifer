namespace RoushTech.Rangifer.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// The model-building blocks a migration changed relative to its parent migration. Thin migration
/// designers build their target model by replaying this chain from its base instead of storing a
/// full copy of the model.
/// </summary>
public sealed class ModelDelta
{
    private readonly ModelDelta? parent;
    private readonly List<BlockOperation<Action<ModelBuilder>>> operations = [];
    private readonly Lazy<IReadOnlyList<KeyValuePair<string, Action<ModelBuilder>>>> blocks;

    public ModelDelta(string migrationId)
    {
        MigrationId = migrationId;
        blocks = new Lazy<IReadOnlyList<KeyValuePair<string, Action<ModelBuilder>>>>(Resolve);
    }

    public ModelDelta(string migrationId, string parentMigrationId, ModelDelta parent)
        : this(migrationId)
    {
        if (!string.Equals(parent.MigrationId, parentMigrationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Migration '{migrationId}' expects the model delta of '{parentMigrationId}' as its parent, but was given '{parent.MigrationId}'.");
        }

        this.parent = parent;
    }

    public string MigrationId { get; }

    public void Set(string key, string? after, Action<ModelBuilder> build) =>
        Add(BlockOperation<Action<ModelBuilder>>.Set(key, after, build));

    public void Move(string key, string? after) => Add(BlockOperation<Action<ModelBuilder>>.Move(key, after));

    public void Remove(string key) => Add(BlockOperation<Action<ModelBuilder>>.Remove(key));

    public void Apply(ModelBuilder modelBuilder)
    {
        foreach (var (_, build) in blocks.Value)
        {
            build(modelBuilder);
        }
    }

    private void Add(BlockOperation<Action<ModelBuilder>> operation)
    {
        if (blocks.IsValueCreated)
        {
            throw new InvalidOperationException($"The model delta for migration '{MigrationId}' has already been resolved.");
        }

        operations.Add(operation);
    }

    private IReadOnlyList<KeyValuePair<string, Action<ModelBuilder>>> Resolve()
    {
        var sequence = parent is null
            ? new BlockSequence<Action<ModelBuilder>>()
            : new BlockSequence<Action<ModelBuilder>>(parent.blocks.Value);

        foreach (var operation in operations)
        {
            try
            {
                sequence.Apply(operation);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"The model delta for migration '{MigrationId}' does not apply on top of '{parent?.MigrationId}': {exception.Message}",
                    exception);
            }
        }

        return sequence.Blocks;
    }
}
